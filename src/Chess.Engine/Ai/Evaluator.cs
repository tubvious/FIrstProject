namespace Chess.Engine.Ai;

/// <summary>
/// Static position evaluation in centipawns from the side to move's point of view:
/// material, piece-square tables (Chess Programming Wiki "Simplified Evaluation Function"),
/// a king table that blends from middlegame to endgame, and a "mop-up" term that helps
/// convert won endgames by driving the lone king to the edge.
/// </summary>
public static class Evaluator
{
    // Tables are written from White's point of view with rank 8 first, as they usually appear in print.
    private static readonly int[] PawnTable =
    [
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0,
    ];

    private static readonly int[] KnightTable =
    [
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
    ];

    private static readonly int[] BishopTable =
    [
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    ];

    private static readonly int[] RookTable =
    [
          0,  0,  0,  0,  0,  0,  0,  0,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  0,  5,  5,  0,  0,  0,
    ];

    private static readonly int[] QueenTable =
    [
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
    ];

    private static readonly int[] KingMiddlegameTable =
    [
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20,
    ];

    private static readonly int[] KingEndgameTable =
    [
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50,
    ];

    private const int MaxPhase = 24;

    public static int PieceValue(PieceType type) => type switch
    {
        PieceType.Pawn => 100,
        PieceType.Knight => 320,
        PieceType.Bishop => 330,
        PieceType.Rook => 500,
        PieceType.Queen => 900,
        _ => 0,
    };

    /// <summary>Score in centipawns; positive is good for the side to move.</summary>
    public static int Evaluate(Position position)
    {
        Span<int> material = stackalloc int[2];
        Span<int> placement = stackalloc int[2];
        Span<int> kingMiddle = stackalloc int[2];
        Span<int> kingEnd = stackalloc int[2];
        Span<int> kingSquare = stackalloc int[2];
        var phase = 0;

        foreach (var (square, piece) in position.Pieces)
        {
            var side = (int)piece.Color;
            var index = TableIndex(square, piece.Color);
            material[side] += PieceValue(piece.Type);

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    placement[side] += PawnTable[index];
                    break;
                case PieceType.Knight:
                    placement[side] += KnightTable[index];
                    phase += 1;
                    break;
                case PieceType.Bishop:
                    placement[side] += BishopTable[index];
                    phase += 1;
                    break;
                case PieceType.Rook:
                    placement[side] += RookTable[index];
                    phase += 2;
                    break;
                case PieceType.Queen:
                    placement[side] += QueenTable[index];
                    phase += 4;
                    break;
                case PieceType.King:
                    kingMiddle[side] = KingMiddlegameTable[index];
                    kingEnd[side] = KingEndgameTable[index];
                    kingSquare[side] = square.Index;
                    break;
            }
        }

        phase = Math.Min(phase, MaxPhase);
        var white = (int)PieceColor.White;
        var black = (int)PieceColor.Black;
        var score = material[white] - material[black] + placement[white] - placement[black];
        score += ((kingMiddle[white] - kingMiddle[black]) * phase + (kingEnd[white] - kingEnd[black]) * (MaxPhase - phase)) / MaxPhase;
        score += MopUp(material, kingSquare, white, black) - MopUp(material, kingSquare, black, white);

        return position.SideToMove == PieceColor.White ? score : -score;
    }

    /// <summary>When clearly winning an endgame, reward cornering the enemy king and approaching it.</summary>
    private static int MopUp(ReadOnlySpan<int> material, ReadOnlySpan<int> kingSquare, int us, int them)
    {
        if (material[them] > PieceValue(PieceType.Bishop) || material[us] - material[them] < PieceValue(PieceType.Rook))
        {
            return 0;
        }

        var enemyKing = Square.FromIndex(kingSquare[them]);
        var ourKing = Square.FromIndex(kingSquare[us]);
        var enemyFromCenter = Math.Max(3 - enemyKing.File, enemyKing.File - 4) + Math.Max(3 - enemyKing.Rank, enemyKing.Rank - 4);
        var kingsApart = Math.Abs(ourKing.File - enemyKing.File) + Math.Abs(ourKing.Rank - enemyKing.Rank);
        return 10 * enemyFromCenter + 4 * (14 - kingsApart);
    }

    private static int TableIndex(Square square, PieceColor color) =>
        color == PieceColor.White ? (7 - square.Rank) * 8 + square.File : square.Rank * 8 + square.File;
}
