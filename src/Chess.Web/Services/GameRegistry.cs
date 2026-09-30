using System.Collections.Concurrent;
using Chess.Web.Data;
using Chess.Web.Domain;

namespace Chess.Web.Services;

/// <summary>
/// Holds the live <see cref="GameSession"/>s in memory. Sessions that are not loaded yet are read from
/// the database on first access; idle ones are unloaded again to keep memory bounded.
/// </summary>
public sealed class GameRegistry(IGameRepository repository, TimeProvider timeProvider, ILogger<GameRegistry> logger)
{
    // Lazy<Task> guarantees a game is loaded from the database only once even under concurrent access.
    private readonly ConcurrentDictionary<string, Lazy<Task<GameSession?>>> _sessions = new(StringComparer.Ordinal);

    public IEnumerable<GameSession> LoadedSessions =>
        _sessions.Values
            .Where(entry => entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
            .Select(entry => entry.Value.Result)
            .OfType<GameSession>();

    public bool IsLoaded(string code) => _sessions.ContainsKey(code);

    public void Add(GameSession session)
    {
        if (!_sessions.TryAdd(session.Code, new Lazy<Task<GameSession?>>(Task.FromResult<GameSession?>(session))))
        {
            throw new InvalidOperationException($"Game {session.Code} is already registered.");
        }
    }

    public async Task<GameSession?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        var entry = _sessions.GetOrAdd(code, key => new Lazy<Task<GameSession?>>(() => LoadAsync(key)));
        GameSession? session;
        try
        {
            session = await entry.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _sessions.TryRemove(new KeyValuePair<string, Lazy<Task<GameSession?>>>(code, entry));
            throw;
        }

        if (session is null)
        {
            // Do not cache misses: the code may be created later.
            _sessions.TryRemove(new KeyValuePair<string, Lazy<Task<GameSession?>>>(code, entry));
        }

        return session;
    }

    /// <summary>
    /// Finds a session and acquires its lock. Retries transparently if the session was evicted while
    /// waiting, so callers always operate on the instance that is currently registered.
    /// </summary>
    public async Task<SessionLease?> AcquireAsync(string code, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var session = await FindAsync(code, cancellationToken);
            if (session is null)
            {
                return null;
            }

            await session.Gate.WaitAsync(cancellationToken);
            if (!session.IsEvicted)
            {
                return new SessionLease(session);
            }

            session.Gate.Release();
        }
    }

    /// <summary>Unloads sessions nobody is connected to that have been idle for at least <paramref name="idleFor"/>.</summary>
    public int EvictIdle(TimeSpan idleFor)
    {
        var now = timeProvider.GetUtcNow();
        var evicted = 0;
        foreach (var session in LoadedSessions)
        {
            // Never block the sweep on a busy game; it will be looked at again next time.
            if (!session.Gate.Wait(0))
            {
                continue;
            }

            try
            {
                var clockRunning = session.Status == GameStatus.InProgress && session.Clock?.RunningFor is not null;
                if (session.HasConnections || clockRunning || now - session.LastActivityAt < idleFor)
                {
                    continue;
                }

                session.IsEvicted = true;
                _sessions.TryRemove(session.Code, out _);
                evicted++;
            }
            finally
            {
                session.Gate.Release();
            }
        }

        return evicted;
    }

    private async Task<GameSession?> LoadAsync(string code)
    {
        var data = await repository.LoadAsync(code);
        if (data is null)
        {
            return null;
        }

        try
        {
            return GameSession.Restore(data, timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException)
        {
            logger.RestoreFailed(ex, code);
            return null;
        }
    }
}

/// <summary>Exclusive access to a session for the lifetime of the lease.</summary>
public sealed class SessionLease(GameSession session) : IDisposable
{
    private int _disposed;

    public GameSession Session { get; } = session;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Session.Gate.Release();
        }
    }
}
