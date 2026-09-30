using Chess.Engine;

namespace Chess.Web.Domain;

public enum GameStatus
{
    WaitingForOpponent,
    InProgress,
    Finished,
}

public enum ColorPreference
{
    Random,
    White,
    Black,
}

public enum ParticipantRole
{
    White,
    Black,
    Spectator,
}

public enum GameError
{
    None,
    GameNotFound,
    NotInGame,
    NotAPlayer,
    InvalidMove,
    GameNotStarted,
    GameFinished,
    NotYourTurn,
    IllegalMove,
    DrawAlreadyOffered,
    DrawOfferLimitReached,
    NoDrawOffer,
    RematchUnavailable,
    NoRematchRequest,
    OpponentConnected,
    ReconnectGracePending,
}

/// <summary>Things that happened in a game that clients may want to surface as notifications.</summary>
public enum GameEventType
{
    PlayerJoined,
    PlayerDisconnected,
    PlayerReconnected,
    DrawOffered,
    DrawDeclined,
    RematchRequested,
    RematchDeclined,
}

public sealed record GameEvent(GameEventType Type, PieceColor Color);

/// <summary>How much a command changed: nothing, only who is connected, or the game itself.</summary>
public enum ChangeKind
{
    None,
    Presence,
    Game,
}

public sealed record GameActionResult(GameError Error, ChangeKind Change, IReadOnlyList<GameEvent> Events)
{
    public static readonly GameActionResult Unchanged = new(GameError.None, ChangeKind.None, []);

    public bool Succeeded => Error == GameError.None;

    public static GameActionResult Fail(GameError error, ChangeKind change = ChangeKind.None) => new(error, change, []);

    public static GameActionResult GameChanged(params GameEvent[] events) => new(GameError.None, ChangeKind.Game, events);

    public static GameActionResult PresenceChanged(params GameEvent[] events) => new(GameError.None, ChangeKind.Presence, events);
}

public sealed record JoinOutcome(ParticipantRole Role, string? MatchedTokenHash, bool ClaimedNewSeat, GameActionResult Result);

/// <summary>A move as recorded in the game log, with the clock readings right after it.</summary>
public sealed record LoggedMove(PlayedMove Move, DateTimeOffset PlayedAt, TimeSpan? WhiteRemaining, TimeSpan? BlackRemaining);
