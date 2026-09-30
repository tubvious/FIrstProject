using Chess.Engine;
using Chess.Web.Domain;

namespace Chess.Web.Data.Entities;

public sealed class GameEntity
{
    public required string Code { get; set; }

    public GameStatus Status { get; set; }

    public required string InitialFen { get; set; }

    public required string CurrentFen { get; set; }

    /// <summary>Null for games without a clock.</summary>
    public int? InitialTimeSeconds { get; set; }

    public int IncrementSeconds { get; set; }

    public ColorPreference ColorPreference { get; set; }

    public long? WhiteTimeRemainingMs { get; set; }

    public long? BlackTimeRemainingMs { get; set; }

    public GameResult? Result { get; set; }

    public GameEndReason? EndReason { get; set; }

    public string? RematchOfCode { get; set; }

    public string? RematchCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public List<PlayerEntity> Players { get; set; } = [];

    public List<MoveEntity> Moves { get; set; } = [];
}
