using Chess.Engine;

namespace Chess.Web.Domain;

/// <summary>
/// The authoritative, in-memory state of one multiplayer game: the chess game itself, the two seats,
/// spectators, the clock and pending offers. Every rule about who may do what lives here.
/// </summary>
/// <remarks>
/// Not thread-safe. Callers must hold <see cref="Gate"/> while reading or mutating a session.
/// All methods take the current time explicitly so behaviour is deterministic.
/// </remarks>
public sealed class GameSession
{
    private readonly PlayerSeat _white = new(PieceColor.White);
    private readonly PlayerSeat _black = new(PieceColor.Black);
    private readonly HashSet<string> _spectators = new(StringComparer.Ordinal);
    private readonly List<LoggedMove> _moveLog = [];
    private readonly ChessGame _game;
    private long _flagFallTicks = long.MaxValue;

    private GameSession(
        string code,
        TimeControl? timeControl,
        ColorPreference colorPreference,
        DateTimeOffset createdAt,
        string? rematchOfCode,
        Position? initialPosition = null)
    {
        Code = code;
        TimeControl = timeControl;
        ColorPreference = colorPreference;
        Clock = timeControl is null ? null : new ChessClock(timeControl);
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
        RematchOfCode = rematchOfCode;
        _game = new ChessGame(initialPosition ?? Position.Initial);
    }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public string Code { get; }

    public TimeControl? TimeControl { get; }

    public ColorPreference ColorPreference { get; }

    public ChessClock? Clock { get; private set; }

    public ChessGame Game => _game;

    public IReadOnlyList<LoggedMove> MoveLog => _moveLog;

    public GameStatus Status { get; private set; } = GameStatus.WaitingForOpponent;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>Increases with every observable change so clients can discard stale snapshots.</summary>
    public long Version { get; private set; } = 1;

    public PieceColor? DrawOfferedBy { get; private set; }

    public string? RematchOfCode { get; }

    public string? RematchCode { get; private set; }

    public int SpectatorCount => _spectators.Count;

    /// <summary>Whether any browser is watching or playing (the computer does not count).</summary>
    public bool HasConnections => _white.HasBrowserConnections || _black.HasBrowserConnections || _spectators.Count > 0;

    /// <summary>The colour the computer plays in a game against the computer.</summary>
    public PieceColor? BotColor => _white.IsBot ? PieceColor.White : _black.IsBot ? PieceColor.Black : null;

    public bool IsBotToMove => Status == GameStatus.InProgress && BotColor == _game.SideToMove;

    /// <summary>Set while the computer is thinking, so only one move is ever computed at a time.</summary>
    public bool BotMovePending { get; set; }

    public bool IsRematchAgreed => Status == GameStatus.Finished && _white.RematchRequested && _black.RematchRequested;

    /// <summary>Set when the session has been unloaded from memory; a fresh copy must be loaded instead.</summary>
    public bool IsEvicted { get; internal set; }

    /// <summary>
    /// UTC ticks at which the running clock's flag falls (long.MaxValue when no clock is running).
    /// Safe to read without holding <see cref="Gate"/>; used by the clock watchdog to skip idle games cheaply.
    /// </summary>
    public long FlagFallUtcTicks => Volatile.Read(ref _flagFallTicks);

    public PlayerSeat Seat(PieceColor color) => color == PieceColor.White ? _white : _black;

    public static GameSession Create(
        string code,
        TimeControl? timeControl,
        ColorPreference colorPreference,
        PieceColor creatorColor,
        string creatorName,
        string creatorTokenHash,
        DateTimeOffset now)
    {
        var session = new GameSession(code, timeControl, colorPreference, now, rematchOfCode: null);
        session.Seat(creatorColor).Occupy(creatorName, creatorTokenHash, now);
        return session;
    }

    /// <summary>Creates a game against the computer. It starts straight away; there is nobody to wait for.</summary>
    public static GameSession CreateAgainstBot(
        string code,
        TimeControl? timeControl,
        ColorPreference colorPreference,
        PieceColor humanColor,
        string humanName,
        string humanTokenHash,
        int botLevel,
        DateTimeOffset now)
    {
        var session = Create(code, timeControl, colorPreference, humanColor, humanName, humanTokenHash, now);
        session.Seat(humanColor.Opposite()).OccupyWithBot(BotName(botLevel), botLevel);
        session.Status = GameStatus.InProgress;
        session.StartedAt = now;
        return session;
    }

    public static string BotName(int level) => $"Computer ({Engine.Ai.BotLevel.Get(level).Name})";

    /// <summary>Rebuilds a session from persisted data by replaying its moves through the rules engine.</summary>
    public static GameSession Restore(RestoredGame data, DateTimeOffset now)
    {
        var session = new GameSession(
            data.Code, data.TimeControl, data.ColorPreference, data.CreatedAt, data.RematchOfCode, Fen.Parse(data.InitialFen));

        foreach (var player in data.Players)
        {
            if (player.BotLevel is { } level)
            {
                session.Seat(player.Color).OccupyWithBot(player.Name, level);
            }
            else
            {
                session.Seat(player.Color).Occupy(player.Name, player.TokenHash, now);
            }
        }

        foreach (var move in data.Moves)
        {
            if (!Move.TryParseUci(move.Uci, out var parsed) || !session._game.TryMakeMove(parsed, out var played))
            {
                throw new InvalidDataException($"Stored move {move.Ply} ({move.Uci}) of game {data.Code} is not legal.");
            }

            session._moveLog.Add(new LoggedMove(played, move.PlayedAt, move.WhiteRemaining, move.BlackRemaining));
        }

        if (data.Outcome is { } outcome)
        {
            session._game.RestoreOutcome(outcome);
        }

        session.Status = data.Status;
        session.StartedAt = data.StartedAt;
        session.FinishedAt = data.FinishedAt;
        session.RematchCode = data.RematchCode;

        if (data.TimeControl is { } timeControl)
        {
            session.Clock = new ChessClock(
                timeControl,
                data.WhiteRemaining ?? timeControl.Initial,
                data.BlackRemaining ?? timeControl.Initial);

            // Time that passed while the server was down is not charged to anyone.
            if (session.Status == GameStatus.InProgress && session._game.Moves.Count >= 2)
            {
                session.Clock.Start(session._game.SideToMove, now);
            }
        }

        session.UpdateFlagFallTime();
        return session;
    }

    /// <summary>
    /// Connects a client to the game. In order of preference the client (1) resumes the seat its tab owns,
    /// (2) resumes a seat this browser owned that nobody is using, (3) takes the free seat of a waiting
    /// game, or (4) watches as a spectator. A seat can never be taken over by someone without its token.
    /// </summary>
    public JoinOutcome Join(
        string connectionId,
        string? ownTokenHash,
        IReadOnlyList<string> knownTokenHashes,
        string newPlayerName,
        string newTokenHash,
        DateTimeOffset now)
    {
        // A connection joining again (e.g. a client retry) must not look like a disconnect/reconnect.
        var previousSeat = PlayerFor(connectionId)?.Color;
        RemoveConnection(connectionId, now);

        if (ownTokenHash is not null && FindSeatOwnedBy(ownTokenHash) is { } ownSeat)
        {
            return Resume(ownSeat, connectionId, ownTokenHash, rejoining: previousSeat == ownSeat.Color, now);
        }

        foreach (var tokenHash in knownTokenHashes)
        {
            if (FindSeatOwnedBy(tokenHash) is { IsConnected: false } seat)
            {
                return Resume(seat, connectionId, tokenHash, rejoining: previousSeat == seat.Color, now);
            }
        }

        if (Status == GameStatus.WaitingForOpponent && FreeSeat() is { } freeSeat)
        {
            freeSeat.Occupy(newPlayerName, newTokenHash, now);
            freeSeat.Attach(connectionId);
            Status = GameStatus.InProgress;
            StartedAt = now;
            Touch(now);
            return new JoinOutcome(
                ToRole(freeSeat.Color), newTokenHash, ClaimedNewSeat: true,
                GameActionResult.GameChanged(new GameEvent(GameEventType.PlayerJoined, freeSeat.Color)));
        }

        _spectators.Add(connectionId);
        Touch(now);
        return new JoinOutcome(ParticipantRole.Spectator, null, ClaimedNewSeat: false, GameActionResult.PresenceChanged());
    }

    public GameActionResult Leave(string connectionId, DateTimeOffset now)
    {
        var (removed, seatWentOffline) = RemoveConnection(connectionId, now);
        if (!removed)
        {
            return GameActionResult.Unchanged;
        }

        Touch(now);
        return seatWentOffline is { } color && Status == GameStatus.InProgress
            ? GameActionResult.PresenceChanged(new GameEvent(GameEventType.PlayerDisconnected, color))
            : GameActionResult.PresenceChanged();
    }

    public GameActionResult MakeMove(string connectionId, Move move, DateTimeOffset now) =>
        PlayerFor(connectionId) is { } seat ? PlayMove(seat, move, now) : GameActionResult.Fail(GameError.NotAPlayer);

    /// <summary>
    /// Plays the computer's move, provided the game is still where it was when the computer started
    /// thinking (the opponent may have resigned or the clock may have run out meanwhile).
    /// </summary>
    public GameActionResult PlayBotMove(Move move, int expectedPly, DateTimeOffset now)
    {
        BotMovePending = false;
        if (!IsBotToMove || _game.Moves.Count != expectedPly || BotColor is not { } botColor)
        {
            return GameActionResult.Unchanged;
        }

        return PlayMove(Seat(botColor), move, now);
    }

    private GameActionResult PlayMove(PlayerSeat seat, Move move, DateTimeOffset now)
    {
        if (CheckPlayable() is { } error)
        {
            return error;
        }

        if (seat.Color != _game.SideToMove)
        {
            return GameActionResult.Fail(GameError.NotYourTurn);
        }

        // A move that arrives after the flag fell loses on time, however fast the network was.
        if (CheckFlag(now).Change == ChangeKind.Game)
        {
            return GameActionResult.Fail(GameError.GameFinished, ChangeKind.Game);
        }

        if (!_game.TryMakeMove(move, out var played))
        {
            return GameActionResult.Fail(GameError.IllegalMove);
        }

        if (Clock is not null)
        {
            // Neither side's first move uses clock time; White's clock starts once Black has replied.
            if (played.Ply == 2)
            {
                Clock.Start(PieceColor.White, now);
            }
            else if (played.Ply > 2)
            {
                Clock.SwitchTurn(now);
            }
        }

        _moveLog.Add(new LoggedMove(
            played, now, Clock?.GetRemaining(PieceColor.White, now), Clock?.GetRemaining(PieceColor.Black, now)));

        // Making a move implicitly declines the opponent's pending draw offer.
        if (DrawOfferedBy == seat.Color.Opposite())
        {
            DrawOfferedBy = null;
        }

        if (_game.IsOver)
        {
            Finish(now);
        }
        else
        {
            Touch(now);
        }

        return GameActionResult.GameChanged();
    }

    /// <summary>Resigns, or aborts the game if neither side has completed a move yet.</summary>
    public GameActionResult Resign(string connectionId, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        if (CheckPlayable() is { } error)
        {
            return error;
        }

        if (CanAbort)
        {
            _game.Abort();
        }
        else
        {
            _game.Resign(seat.Color);
        }

        Finish(now);
        return GameActionResult.GameChanged();
    }

    public bool CanAbort => _game.Moves.Count < 2;

    public GameActionResult OfferDraw(string connectionId, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        if (CheckPlayable() is { } error)
        {
            return error;
        }

        if (BotColor is not null)
        {
            return GameActionResult.Fail(GameError.NotAvailableAgainstComputer);
        }

        if (DrawOfferedBy == seat.Color.Opposite())
        {
            // Both sides want a draw.
            _game.AgreeDraw();
            Finish(now);
            return GameActionResult.GameChanged();
        }

        if (DrawOfferedBy == seat.Color)
        {
            return GameActionResult.Fail(GameError.DrawAlreadyOffered);
        }

        if (seat.LastDrawOfferPly == _game.Moves.Count)
        {
            return GameActionResult.Fail(GameError.DrawOfferLimitReached);
        }

        DrawOfferedBy = seat.Color;
        seat.LastDrawOfferPly = _game.Moves.Count;
        Touch(now);
        return GameActionResult.GameChanged(new GameEvent(GameEventType.DrawOffered, seat.Color));
    }

    public GameActionResult RespondToDrawOffer(string connectionId, bool accept, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        if (CheckPlayable() is { } error)
        {
            return error;
        }

        if (DrawOfferedBy != seat.Color.Opposite())
        {
            return GameActionResult.Fail(GameError.NoDrawOffer);
        }

        if (accept)
        {
            _game.AgreeDraw();
            Finish(now);
            return GameActionResult.GameChanged();
        }

        DrawOfferedBy = null;
        Touch(now);
        return GameActionResult.GameChanged(new GameEvent(GameEventType.DrawDeclined, seat.Color));
    }

    public GameActionResult RequestRematch(string connectionId, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        if (Status != GameStatus.Finished || !_white.IsOccupied || !_black.IsOccupied || RematchCode is not null)
        {
            return GameActionResult.Fail(GameError.RematchUnavailable);
        }

        if (seat.RematchRequested)
        {
            return GameActionResult.Unchanged;
        }

        seat.RematchRequested = true;
        if (Seat(seat.Color.Opposite()).IsBot)
        {
            // The computer is always up for another game.
            Seat(seat.Color.Opposite()).RematchRequested = true;
        }

        Touch(now);
        return GameActionResult.GameChanged(new GameEvent(GameEventType.RematchRequested, seat.Color));
    }

    public GameActionResult DeclineRematch(string connectionId, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        var opponent = Seat(seat.Color.Opposite());
        if (!opponent.RematchRequested || RematchCode is not null)
        {
            return GameActionResult.Fail(GameError.NoRematchRequest);
        }

        opponent.RematchRequested = false;
        Touch(now);
        return GameActionResult.GameChanged(new GameEvent(GameEventType.RematchDeclined, seat.Color));
    }

    /// <summary>Starts the agreed rematch: same time control, colours swapped, both players seated.</summary>
    public GameSession CreateRematch(string newCode, DateTimeOffset now)
    {
        if (!IsRematchAgreed || RematchCode is not null)
        {
            throw new InvalidOperationException("Both players must request a rematch first.");
        }

        var rematch = new GameSession(newCode, TimeControl, ColorPreference, now, rematchOfCode: Code);
        rematch._white.TakeOver(_black, now);
        rematch._black.TakeOver(_white, now);
        rematch.Status = GameStatus.InProgress;
        rematch.StartedAt = now;

        RematchCode = newCode;
        Touch(now);
        return rematch;
    }

    /// <summary>Lets the remaining player end a game whose opponent disconnected and never came back.</summary>
    public GameActionResult ClaimAbandonedGame(string connectionId, bool scoreAsDraw, TimeSpan gracePeriod, DateTimeOffset now)
    {
        if (PlayerFor(connectionId) is not { } seat)
        {
            return GameActionResult.Fail(GameError.NotAPlayer);
        }

        if (CheckPlayable() is { } error)
        {
            return error;
        }

        var opponent = Seat(seat.Color.Opposite());
        if (opponent.IsConnected)
        {
            return GameActionResult.Fail(GameError.OpponentConnected);
        }

        if (opponent.DisconnectedSince is not { } since || now - since < gracePeriod)
        {
            return GameActionResult.Fail(GameError.ReconnectGracePending);
        }

        _game.Abandon(opponent.Color, scoreAsDraw);
        Finish(now);
        return GameActionResult.GameChanged();
    }

    /// <summary>Ends the game on time if the running clock has run out.</summary>
    public GameActionResult CheckFlag(DateTimeOffset now)
    {
        if (Status != GameStatus.InProgress || Clock?.RunningFor is not { } running || !Clock.HasFlagged(running, now))
        {
            return GameActionResult.Unchanged;
        }

        _game.Timeout(running);
        Finish(now);
        return GameActionResult.GameChanged();
    }

    private PlayerSeat? PlayerFor(string connectionId) =>
        _white.HasConnection(connectionId) ? _white
        : _black.HasConnection(connectionId) ? _black
        : null;

    private PlayerSeat? FindSeatOwnedBy(string tokenHash) =>
        _white.IsOwnedBy(tokenHash) ? _white
        : _black.IsOwnedBy(tokenHash) ? _black
        : null;

    private PlayerSeat? FreeSeat() => !_white.IsOccupied ? _white : !_black.IsOccupied ? _black : null;

    private GameActionResult? CheckPlayable() => Status switch
    {
        GameStatus.WaitingForOpponent => GameActionResult.Fail(GameError.GameNotStarted),
        GameStatus.Finished => GameActionResult.Fail(GameError.GameFinished),
        _ => null,
    };

    private JoinOutcome Resume(PlayerSeat seat, string connectionId, string tokenHash, bool rejoining, DateTimeOffset now)
    {
        var wasAway = !seat.IsConnected && !rejoining && Status == GameStatus.InProgress;
        seat.Attach(connectionId);
        Touch(now);

        var result = wasAway
            ? GameActionResult.PresenceChanged(new GameEvent(GameEventType.PlayerReconnected, seat.Color))
            : GameActionResult.PresenceChanged();
        return new JoinOutcome(ToRole(seat.Color), tokenHash, ClaimedNewSeat: false, result);
    }

    /// <summary>Removes a connection from wherever it is and reports whether its seat just went offline.</summary>
    private (bool Removed, PieceColor? SeatWentOffline) RemoveConnection(string connectionId, DateTimeOffset now)
    {
        if (_spectators.Remove(connectionId))
        {
            return (true, null);
        }

        foreach (var seat in (ReadOnlySpan<PlayerSeat>)[_white, _black])
        {
            if (seat.HasConnection(connectionId))
            {
                return (true, seat.Detach(connectionId, now) ? seat.Color : null);
            }
        }

        return (false, null);
    }

    private void Finish(DateTimeOffset now)
    {
        Status = GameStatus.Finished;
        FinishedAt = now;
        DrawOfferedBy = null;
        Clock?.Stop(now);
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        LastActivityAt = now;
        Version++;
        UpdateFlagFallTime();
    }

    private void UpdateFlagFallTime()
    {
        var ticks = Status == GameStatus.InProgress && Clock?.GetFlagFallTime() is { } flagFall
            ? flagFall.UtcTicks
            : long.MaxValue;
        Volatile.Write(ref _flagFallTicks, ticks);
    }

    private static ParticipantRole ToRole(PieceColor color) =>
        color == PieceColor.White ? ParticipantRole.White : ParticipantRole.Black;
}
