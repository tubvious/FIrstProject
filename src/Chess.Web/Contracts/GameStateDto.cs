using Chess.Engine;
using Chess.Web.Domain;

namespace Chess.Web.Contracts;

/// <summary>
/// A complete snapshot of a game as seen by any participant. The server broadcasts one after every
/// change, so clients never have to reconstruct state from partial updates.
/// </summary>
public sealed record GameStateDto(
    string Code,
    long Version,
    GameStatus Status,
    string Fen,
    PieceColor Turn,
    IReadOnlyList<MoveDto> Moves,
    IReadOnlyList<string> LegalMoves,
    string? CheckSquare,
    PlayerDto? White,
    PlayerDto? Black,
    ClockDto? Clock,
    PieceColor? DrawOfferedBy,
    bool CanAbort,
    OutcomeDto? Outcome,
    CapturedPiecesDto Captured,
    int MaterialBalance,
    int SpectatorCount,
    string? RematchCode,
    string? RematchOfCode,
    long ReconnectGraceMs,
    long ServerTime);

public sealed record MoveDto(
    int Ply,
    string San,
    string Uci,
    string From,
    string To,
    bool IsCapture,
    bool IsCheck,
    bool IsCastle,
    bool IsEnPassant,
    PieceType? Promotion,
    string Fen);

public sealed record PlayerDto(string Name, bool Connected, long? DisconnectedForMs, bool RematchRequested, bool IsBot);

/// <summary>Remaining times are as of <see cref="GameStateDto.ServerTime"/>; clients count down locally.</summary>
public sealed record ClockDto(long InitialMs, long IncrementMs, long WhiteMs, long BlackMs, PieceColor? Running);

public sealed record OutcomeDto(GameResult Result, GameEndReason Reason, PieceColor? Winner);

/// <summary>Pieces each side has captured, cheapest first.</summary>
public sealed record CapturedPiecesDto(IReadOnlyList<PieceType> ByWhite, IReadOnlyList<PieceType> ByBlack);

public sealed record GameNotificationDto(GameEventType Type, PieceColor Color);
