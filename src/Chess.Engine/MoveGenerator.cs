namespace Chess.Engine;

/// <summary>
/// Generates legal moves by producing pseudo-legal moves and discarding those that leave
/// the mover's own king in check.
/// </summary>
internal static class MoveGenerator
{
    internal static readonly (int File, int Rank)[] KnightOffsets =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

    internal static readonly (int File, int Rank)[] KingOffsets =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    internal static readonly (int File, int Rank)[] DiagonalDirections = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    internal static readonly (int File, int Rank)[] StraightDirections = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static readonly (int File, int Rank)[] AllDirections = [.. DiagonalDirections, .. StraightDirections];

    private static readonly PieceType[] PromotionPieces =
        [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    public static IReadOnlyList<Move> GenerateLegalMoves(Position position)
    {
        var candidates = new List<Move>(48);
        GeneratePseudoLegalMoves(position, candidates);

        var mover = position.SideToMove;
        var legal = new List<Move>(candidates.Count);
        foreach (var move in candidates)
        {
            if (!position.Apply(move).IsInCheck(mover))
            {
                legal.Add(move);
            }
        }

        return legal.AsReadOnly();
    }

    private static void GeneratePseudoLegalMoves(Position position, List<Move> moves)
    {
        foreach (var (square, piece) in position.Pieces)
        {
            if (piece.Color != position.SideToMove)
            {
                continue;
            }

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(position, square, piece.Color, moves);
                    break;
                case PieceType.Knight:
                    AddStepMoves(position, square, piece.Color, KnightOffsets, moves);
                    break;
                case PieceType.Bishop:
                    AddSlidingMoves(position, square, piece.Color, DiagonalDirections, moves);
                    break;
                case PieceType.Rook:
                    AddSlidingMoves(position, square, piece.Color, StraightDirections, moves);
                    break;
                case PieceType.Queen:
                    AddSlidingMoves(position, square, piece.Color, AllDirections, moves);
                    break;
                case PieceType.King:
                    AddStepMoves(position, square, piece.Color, KingOffsets, moves);
                    AddCastlingMoves(position, square, piece.Color, moves);
                    break;
            }
        }
    }

    private static void AddPawnMoves(Position position, Square from, PieceColor color, List<Move> moves)
    {
        var direction = color.PawnDirection();
        var startRank = color == PieceColor.White ? 1 : 6;

        if (from.TryOffset(0, direction, out var oneStep) && position[oneStep] is null)
        {
            AddPawnMove(from, oneStep, color, moves);

            if (from.Rank == startRank &&
                from.TryOffset(0, 2 * direction, out var twoSteps) &&
                position[twoSteps] is null)
            {
                moves.Add(new Move(from, twoSteps));
            }
        }

        foreach (var fileDelta in (ReadOnlySpan<int>)[-1, 1])
        {
            if (!from.TryOffset(fileDelta, direction, out var target))
            {
                continue;
            }

            if (position[target] is { } victim ? victim.Color != color : target == position.EnPassantSquare)
            {
                AddPawnMove(from, target, color, moves);
            }
        }
    }

    private static void AddPawnMove(Square from, Square to, PieceColor color, List<Move> moves)
    {
        var promotionRank = color == PieceColor.White ? 7 : 0;
        if (to.Rank != promotionRank)
        {
            moves.Add(new Move(from, to));
            return;
        }

        foreach (var promotion in PromotionPieces)
        {
            moves.Add(new Move(from, to, promotion));
        }
    }

    private static void AddStepMoves(
        Position position, Square from, PieceColor color, (int File, int Rank)[] offsets, List<Move> moves)
    {
        foreach (var (fileDelta, rankDelta) in offsets)
        {
            if (from.TryOffset(fileDelta, rankDelta, out var to) && position[to]?.Color != color)
            {
                moves.Add(new Move(from, to));
            }
        }
    }

    private static void AddSlidingMoves(
        Position position, Square from, PieceColor color, (int File, int Rank)[] directions, List<Move> moves)
    {
        foreach (var (fileDelta, rankDelta) in directions)
        {
            var to = from;
            while (to.TryOffset(fileDelta, rankDelta, out to))
            {
                var occupant = position[to];
                if (occupant?.Color == color)
                {
                    break;
                }

                moves.Add(new Move(from, to));
                if (occupant is not null)
                {
                    break;
                }
            }
        }
    }

    private static void AddCastlingMoves(Position position, Square kingSquare, PieceColor color, List<Move> moves)
    {
        var backRank = color.BackRank();
        if (kingSquare != new Square(4, backRank) || position.IsInCheck(color))
        {
            return;
        }

        var (kingSide, queenSide) = color == PieceColor.White
            ? (CastlingRights.WhiteKingSide, CastlingRights.WhiteQueenSide)
            : (CastlingRights.BlackKingSide, CastlingRights.BlackQueenSide);

        if (position.CastlingRights.HasFlag(kingSide) &&
            CanCastle(position, color, rookFile: 7, emptyFiles: [5, 6], kingPathFiles: [5, 6]))
        {
            moves.Add(new Move(kingSquare, new Square(6, backRank)));
        }

        if (position.CastlingRights.HasFlag(queenSide) &&
            CanCastle(position, color, rookFile: 0, emptyFiles: [1, 2, 3], kingPathFiles: [3, 2]))
        {
            moves.Add(new Move(kingSquare, new Square(2, backRank)));
        }
    }

    private static bool CanCastle(
        Position position, PieceColor color, int rookFile, ReadOnlySpan<int> emptyFiles, ReadOnlySpan<int> kingPathFiles)
    {
        var backRank = color.BackRank();
        if (position[new Square(rookFile, backRank)] != new Piece(PieceType.Rook, color))
        {
            return false;
        }

        foreach (var file in emptyFiles)
        {
            if (position[new Square(file, backRank)] is not null)
            {
                return false;
            }
        }

        // The king may not pass through or land on an attacked square.
        foreach (var file in kingPathFiles)
        {
            if (position.IsSquareAttacked(new Square(file, backRank), color.Opposite()))
            {
                return false;
            }
        }

        return true;
    }
}
