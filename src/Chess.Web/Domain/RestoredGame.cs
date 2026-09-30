using Chess.Engine;

namespace Chess.Web.Domain;

/// <summary>Everything needed to rebuild a <see cref="GameSession"/> from storage.</summary>
public sealed record RestoredGame(
    string Code,
    GameStatus Status,
    string InitialFen,
    TimeControl? TimeControl,
    ColorPreference ColorPreference,
    TimeSpan? WhiteRemaining,
    TimeSpan? BlackRemaining,
    GameOutcome? Outcome,
    string? RematchOfCode,
    string? RematchCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<RestoredPlayer> Players,
    IReadOnlyList<RestoredMove> Moves);

public sealed record RestoredPlayer(PieceColor Color, string Name, string TokenHash, int? BotLevel = null);

public sealed record RestoredMove(int Ply, string Uci, DateTimeOffset PlayedAt, TimeSpan? WhiteRemaining, TimeSpan? BlackRemaining);
