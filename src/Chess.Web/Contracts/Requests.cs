using Chess.Engine;
using Chess.Web.Domain;

namespace Chess.Web.Contracts;

/// <summary>Body of POST /api/games. Omit <see cref="Minutes"/> for a game without a clock.</summary>
public sealed record CreateGameRequest(string? PlayerName, int? Minutes, int? IncrementSeconds, ColorPreference Color);

public sealed record CreateGameResponse(string Code, string SeatToken, PieceColor Color, string Url);

public sealed record GameSummaryDto(
    string Code,
    GameStatus Status,
    string? TimeControl,
    string? WhiteName,
    string? BlackName,
    bool HasOpenSeat);

/// <param name="Code">Game to join.</param>
/// <param name="SeatToken">Token this browser tab holds for the game, if any.</param>
/// <param name="KnownSeatTokens">Tokens this browser remembers from other tabs/sessions; used only for seats nobody is using.</param>
/// <param name="PlayerName">Display name to use if a new seat is claimed.</param>
public sealed record JoinGameRequest(string Code, string? SeatToken, IReadOnlyList<string>? KnownSeatTokens, string? PlayerName);

public sealed record JoinGameResponse(bool Success, string? Error, string? Message, ParticipantRole Role, string? SeatToken, GameStateDto? State)
{
    public static JoinGameResponse Failed(GameError error) =>
        new(false, error.ToString(), GameErrorMessages.For(error), ParticipantRole.Spectator, null, null);
}

/// <summary>Result of a hub command. <see cref="Error"/> is a stable code; <see cref="Message"/> is for people.</summary>
public sealed record HubResult(bool Success, string? Error, string? Message)
{
    public static readonly HubResult Ok = new(true, null, null);

    public static HubResult From(GameActionResult result) =>
        result.Succeeded ? Ok : new HubResult(false, result.Error.ToString(), GameErrorMessages.For(result.Error));
}

public static class GameErrorMessages
{
    public static string For(GameError error) => error switch
    {
        GameError.GameNotFound => "That game doesn't exist.",
        GameError.NotInGame => "You're not connected to a game.",
        GameError.NotAPlayer => "Spectators can't do that.",
        GameError.InvalidMove => "That move isn't formatted correctly.",
        GameError.GameNotStarted => "Waiting for your opponent to join.",
        GameError.GameFinished => "The game is over.",
        GameError.NotYourTurn => "It's not your turn.",
        GameError.IllegalMove => "That move isn't legal.",
        GameError.DrawAlreadyOffered => "You've already offered a draw.",
        GameError.DrawOfferLimitReached => "You can offer another draw after your next move.",
        GameError.NoDrawOffer => "There is no draw offer to answer.",
        GameError.RematchUnavailable => "A rematch isn't available.",
        GameError.NoRematchRequest => "There is no rematch request to decline.",
        GameError.OpponentConnected => "Your opponent is still connected.",
        GameError.ReconnectGracePending => "Give your opponent a little longer to reconnect.",
        _ => "Something went wrong.",
    };
}
