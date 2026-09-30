using Chess.Engine;

namespace Chess.Web.Data.Entities;

/// <summary>A player seated in a game. Players are anonymous; the seat token proves ownership.</summary>
public sealed class PlayerEntity
{
    public int Id { get; set; }

    public required string GameCode { get; set; }

    public PieceColor Color { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>SHA-256 of the seat token; the raw token is never stored.</summary>
    public required string SeatTokenHash { get; set; }

    public DateTime JoinedAt { get; set; }
}
