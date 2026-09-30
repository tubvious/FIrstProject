namespace Chess.Engine;

public enum PieceColor : byte
{
    White,
    Black,
}

public static class PieceColorExtensions
{
    public static PieceColor Opposite(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    /// <summary>The rank direction pawns of this color advance in (+1 for White, -1 for Black).</summary>
    internal static int PawnDirection(this PieceColor color) => color == PieceColor.White ? 1 : -1;

    /// <summary>The back rank (0-based) of this color, where the king and rooks start.</summary>
    internal static int BackRank(this PieceColor color) => color == PieceColor.White ? 0 : 7;
}
