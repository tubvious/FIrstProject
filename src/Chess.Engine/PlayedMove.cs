namespace Chess.Engine;

/// <summary>A move that has been played in a <see cref="ChessGame"/>, with its notation and effects.</summary>
public sealed record PlayedMove(
    int Ply,
    Move Move,
    string San,
    Piece Piece,
    Piece? CapturedPiece,
    MoveFlags Flags,
    Position PositionAfter)
{
    public PieceColor Color => Piece.Color;

    public string Uci => Move.ToUci();

    public bool IsCapture => CapturedPiece is not null;

    public bool IsCheck => PositionAfter.IsCheck;

    public bool IsCheckmate => PositionAfter.IsCheckmate;

    /// <summary>1-based full move number this ply belongs to (both 1. e4 and 1... e5 are move 1).</summary>
    public int MoveNumber => (Ply + 1) / 2;
}
