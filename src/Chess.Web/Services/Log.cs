using Chess.Engine;
using Chess.Web.Domain;

namespace Chess.Web.Services;

/// <summary>Source-generated, allocation-free log messages for the game server.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Game {Code} created (time control: {TimeControl}, creator plays {Color})")]
    public static partial void GameCreated(this ILogger logger, string code, TimeControl? timeControl, PieceColor color);

    [LoggerMessage(Level = LogLevel.Information, Message = "Game {Code}: {Role} joined, game started")]
    public static partial void PlayerJoined(this ILogger logger, string code, ParticipantRole role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Game {Code}: rematch started as {RematchCode}")]
    public static partial void RematchStarted(this ILogger logger, string code, string rematchCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Game {Code} finished: {Result} by {Reason}")]
    public static partial void GameFinished(this ILogger logger, string code, GameResult result, GameEndReason reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist game {Code}")]
    public static partial void PersistenceFailed(this ILogger logger, Exception exception, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Game {Code} could not be restored from the database")]
    public static partial void RestoreFailed(this ILogger logger, Exception exception, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Clock check failed")]
    public static partial void ClockCheckFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unloaded {Count} idle game(s) from memory")]
    public static partial void GamesEvicted(this ILogger logger, int count);
}
