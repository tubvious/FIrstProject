using Chess.Engine;
using Chess.Engine.Ai;
using Chess.Web.Configuration;
using Chess.Web.Contracts;
using Chess.Web.Data;
using Chess.Web.Domain;
using Microsoft.Extensions.Options;

namespace Chess.Web.Services;

public interface IGameService
{
    Task<CreateGameResponse> CreateGameAsync(CreateGameCommand command, CancellationToken cancellationToken = default);

    Task<GameSummaryDto?> GetSummaryAsync(string code, CancellationToken cancellationToken = default);

    Task<JoinGameResponse> JoinGameAsync(string code, string connectionId, JoinGameRequest request);

    Task LeaveGameAsync(string code, string connectionId);

    Task<HubResult> MakeMoveAsync(string code, string connectionId, string uci);

    Task<HubResult> ResignAsync(string code, string connectionId);

    Task<HubResult> OfferDrawAsync(string code, string connectionId);

    Task<HubResult> RespondToDrawOfferAsync(string code, string connectionId, bool accept);

    Task<HubResult> RequestRematchAsync(string code, string connectionId);

    Task<HubResult> DeclineRematchAsync(string code, string connectionId);

    Task<HubResult> ClaimAbandonedGameAsync(string code, string connectionId, bool scoreAsDraw);

    /// <summary>Ends every game whose running clock has hit zero.</summary>
    Task EnforceTimeControlsAsync(CancellationToken cancellationToken = default);
}

/// <param name="BotLevel">Set to play the computer at this level (1-5); null to play a friend.</param>
public sealed record CreateGameCommand(string? PlayerName, TimeControl? TimeControl, ColorPreference ColorPreference, int? BotLevel = null);

/// <summary>
/// Coordinates game commands: finds the session, serializes access to it, applies the domain rule,
/// then persists and broadcasts the resulting state. The session itself decides what is allowed.
/// </summary>
public sealed class GameService(
    GameRegistry registry,
    IGameRepository repository,
    IGameNotifier notifier,
    PlayerNames playerNames,
    TimeProvider timeProvider,
    IOptions<GameOptions> options,
    ILogger<GameService> logger) : IGameService
{
    private readonly GameOptions _options = options.Value;

    public async Task<CreateGameResponse> CreateGameAsync(CreateGameCommand command, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var code = await GenerateUniqueCodeAsync(cancellationToken);
        var token = SeatToken.Generate();
        var color = command.ColorPreference switch
        {
            ColorPreference.White => PieceColor.White,
            ColorPreference.Black => PieceColor.Black,
            _ => Random.Shared.Next(2) == 0 ? PieceColor.White : PieceColor.Black,
        };

        var name = playerNames.Sanitize(command.PlayerName);
        var session = command.BotLevel is { } botLevel
            ? GameSession.CreateAgainstBot(code, command.TimeControl, command.ColorPreference, color, name, SeatToken.Hash(token), botLevel, now)
            : GameSession.Create(code, command.TimeControl, command.ColorPreference, color, name, SeatToken.Hash(token), now);

        await repository.SaveAsync(session, now, cancellationToken);
        registry.Add(session);
        logger.GameCreated(code, session.TimeControl, color);

        return new CreateGameResponse(code, token, color, $"/game/{code}");
    }

    public async Task<GameSummaryDto?> GetSummaryAsync(string code, CancellationToken cancellationToken = default)
    {
        using var lease = await registry.AcquireAsync(code, cancellationToken);
        return lease is null ? null : GameStateMapper.ToSummary(lease.Session);
    }

    public async Task<JoinGameResponse> JoinGameAsync(string code, string connectionId, JoinGameRequest request)
    {
        using var lease = await registry.AcquireAsync(code);
        if (lease is null)
        {
            return JoinGameResponse.Failed(GameError.GameNotFound);
        }

        var session = lease.Session;
        var now = timeProvider.GetUtcNow();

        // Map token hashes back to the raw tokens the client sent so we can tell it which one won.
        var tokensByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        string? ownTokenHash = null;
        if (SeatToken.IsWellFormed(request.SeatToken))
        {
            ownTokenHash = SeatToken.Hash(request.SeatToken);
            tokensByHash[ownTokenHash] = request.SeatToken;
        }

        var knownTokenHashes = new List<string>();
        foreach (var token in (request.KnownSeatTokens ?? []).Where(SeatToken.IsWellFormed).Take(10))
        {
            var hash = SeatToken.Hash(token);
            tokensByHash[hash] = token;
            knownTokenHashes.Add(hash);
        }

        var newToken = SeatToken.Generate();
        var newTokenHash = SeatToken.Hash(newToken);
        tokensByHash[newTokenHash] = newToken;

        var outcome = session.Join(connectionId, ownTokenHash, knownTokenHashes, playerNames.Sanitize(request.PlayerName), newTokenHash, now);
        await PublishAsync(session, outcome.Result, now);

        if (outcome.ClaimedNewSeat)
        {
            logger.PlayerJoined(code, outcome.Role);
        }

        var seatToken = outcome.MatchedTokenHash is { } matched ? tokensByHash[matched] : null;
        return new JoinGameResponse(true, null, null, outcome.Role, seatToken, Snapshot(session, now));
    }

    public async Task LeaveGameAsync(string code, string connectionId)
    {
        // A departing connection must never be able to resurrect an unloaded game.
        if (!registry.IsLoaded(code))
        {
            return;
        }

        await ExecuteAsync(code, (session, now) => session.Leave(connectionId, now));
    }

    public Task<HubResult> MakeMoveAsync(string code, string connectionId, string uci) =>
        Move.TryParseUci(uci, out var move)
            ? RunCommandAsync(code, (session, now) => session.MakeMove(connectionId, move, now))
            : Task.FromResult(HubResult.From(GameActionResult.Fail(GameError.InvalidMove)));

    public Task<HubResult> ResignAsync(string code, string connectionId) =>
        RunCommandAsync(code, (session, now) => session.Resign(connectionId, now));

    public Task<HubResult> OfferDrawAsync(string code, string connectionId) =>
        RunCommandAsync(code, (session, now) => session.OfferDraw(connectionId, now));

    public Task<HubResult> RespondToDrawOfferAsync(string code, string connectionId, bool accept) =>
        RunCommandAsync(code, (session, now) => session.RespondToDrawOffer(connectionId, accept, now));

    public Task<HubResult> DeclineRematchAsync(string code, string connectionId) =>
        RunCommandAsync(code, (session, now) => session.DeclineRematch(connectionId, now));

    public Task<HubResult> ClaimAbandonedGameAsync(string code, string connectionId, bool scoreAsDraw) =>
        RunCommandAsync(code, (session, now) =>
            session.ClaimAbandonedGame(connectionId, scoreAsDraw, _options.ReconnectGracePeriod, now));

    public async Task<HubResult> RequestRematchAsync(string code, string connectionId)
    {
        using var lease = await registry.AcquireAsync(code);
        if (lease is null)
        {
            return HubResult.From(GameActionResult.Fail(GameError.GameNotFound));
        }

        var session = lease.Session;
        var now = timeProvider.GetUtcNow();
        var result = session.RequestRematch(connectionId, now);

        if (result.Succeeded && session.IsRematchAgreed && session.RematchCode is null)
        {
            var rematch = session.CreateRematch(await GenerateUniqueCodeAsync(), now);
            await repository.SaveAsync(rematch, now);
            registry.Add(rematch);
            logger.RematchStarted(code, rematch.Code);
        }

        await PublishAsync(session, result, now);
        return HubResult.From(result);
    }

    public async Task EnforceTimeControlsAsync(CancellationToken cancellationToken = default)
    {
        var nowTicks = timeProvider.GetUtcNow().UtcTicks;
        foreach (var session in registry.LoadedSessions)
        {
            if (session.FlagFallUtcTicks <= nowTicks)
            {
                await ExecuteAsync(session.Code, (s, now) => s.CheckFlag(now), cancellationToken);
            }
        }
    }

    private async Task<HubResult> RunCommandAsync(string code, Func<GameSession, DateTimeOffset, GameActionResult> action) =>
        HubResult.From(await ExecuteAsync(code, action));

    private async Task<GameActionResult> ExecuteAsync(
        string code, Func<GameSession, DateTimeOffset, GameActionResult> action, CancellationToken cancellationToken = default)
    {
        using var lease = await registry.AcquireAsync(code, cancellationToken);
        if (lease is null)
        {
            return GameActionResult.Fail(GameError.GameNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var result = action(lease.Session, now);
        await PublishAsync(lease.Session, result, now);
        return result;
    }

    /// <summary>Persists (for game changes) and broadcasts the new state. Must be called while holding the session lock.</summary>
    private async Task PublishAsync(GameSession session, GameActionResult result, DateTimeOffset now)
    {
        if (result.Change == ChangeKind.None)
        {
            return;
        }

        if (result.Change == ChangeKind.Game)
        {
            try
            {
                await repository.SaveAsync(session, now);
            }
            catch (Exception ex)
            {
                // The in-memory session stays authoritative; a failed write must not stall a live game.
                logger.PersistenceFailed(ex, session.Code);
            }

            if (session.Status == GameStatus.Finished && session.Game.Outcome is { } outcome && session.FinishedAt == now)
            {
                logger.GameFinished(session.Code, outcome.Result, outcome.Reason);
            }
        }

        await notifier.PublishStateAsync(Snapshot(session, now));
        foreach (var gameEvent in result.Events)
        {
            await notifier.PublishNotificationAsync(session.Code, new GameNotificationDto(gameEvent.Type, gameEvent.Color));
        }

        ScheduleBotMove(session, now);
    }

    /// <summary>
    /// If it is the computer's turn, thinks on a background thread and then plays the move through the
    /// normal command path. Must be called while holding the session lock.
    /// </summary>
    private void ScheduleBotMove(GameSession session, DateTimeOffset now)
    {
        // Wait until the human is actually looking at the board before the computer opens the game.
        if (!session.IsBotToMove || session.BotMovePending || !session.HasConnections || session.BotColor is not { } botColor)
        {
            return;
        }

        session.BotMovePending = true;
        var position = session.Game.CurrentPosition;
        var ply = session.Game.Moves.Count;
        var level = BotLevel.Get(session.Seat(botColor).BotLevel!.Value);

        // Never let the computer lose on time: think for at most a small share of its remaining clock.
        TimeSpan? budget = session.Clock is { } clock ? clock.GetRemaining(botColor, now) / 30 : null;

        _ = Task.Run(async () =>
        {
            try
            {
                var started = timeProvider.GetTimestamp();
                var move = ChessBot.ChooseMove(position, level, budget);
                var pause = budget is { } cap && cap < _options.BotMinimumThinkTime ? cap : _options.BotMinimumThinkTime;
                var remainingPause = pause - timeProvider.GetElapsedTime(started);
                if (remainingPause > TimeSpan.Zero)
                {
                    await Task.Delay(remainingPause);
                }

                await ExecuteAsync(session.Code, (s, at) => move is { } chosen
                    ? s.PlayBotMove(chosen, ply, at)
                    : ClearPendingBotMove(s));
            }
            catch (Exception ex)
            {
                logger.BotMoveFailed(ex, session.Code);
                await ExecuteAsync(session.Code, (s, _) => ClearPendingBotMove(s));
            }
        });
    }

    private static GameActionResult ClearPendingBotMove(GameSession session)
    {
        session.BotMovePending = false;
        return GameActionResult.Unchanged;
    }

    private GameStateDto Snapshot(GameSession session, DateTimeOffset now) =>
        GameStateMapper.ToDto(session, now, _options.ReconnectGracePeriod);

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var code = GameCode.Generate();
            if (!registry.IsLoaded(code) && !await repository.ExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }
    }
}
