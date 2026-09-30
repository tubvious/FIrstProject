namespace Chess.Web.Data.Entities;

public sealed class MoveEntity
{
    public int Id { get; set; }

    public required string GameCode { get; set; }

    /// <summary>1-based half-move number.</summary>
    public int Ply { get; set; }

    public required string Uci { get; set; }

    public required string San { get; set; }

    public required string FenAfter { get; set; }

    public long? WhiteTimeRemainingMs { get; set; }

    public long? BlackTimeRemainingMs { get; set; }

    public DateTime PlayedAt { get; set; }
}
