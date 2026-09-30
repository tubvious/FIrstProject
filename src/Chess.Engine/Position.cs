namespace Chess.Engine;

/// <summary>
/// An immutable chess position: piece placement plus side to move, castling rights,
/// en passant target, and the move counters. Applying a move yields a new position.
/// </summary>
public sealed class Position
{
    private readonly Piece?[] _board;
    private IReadOnlyList<Move>? _legalMoves;

    internal Position(
        Piece?[] board,
        PieceColor sideToMove,
        CastlingRights castlingRights,
        Square? enPassantSquare,
        int halfmoveClock,
        int fullmoveNumber)
    {
        _board = board;
        SideToMove = sideToMove;
        CastlingRights = castlingRights;
        EnPassantSquare = enPassantSquare;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public static Position Initial { get; } = Fen.Parse(Fen.StartingPosition);

    public PieceColor SideToMove { get; }

    public CastlingRights CastlingRights { get; }

    /// <summary>The square a pawn skipped over on the previous double push, if any.</summary>
    public Square? EnPassantSquare { get; }

    /// <summary>Half-moves since the last capture or pawn move (fifty-move rule).</summary>
    public int HalfmoveClock { get; }

    public int FullmoveNumber { get; }

    public Piece? this[Square square] => _board[square.Index];

    public IEnumerable<(Square Square, Piece Piece)> Pieces
    {
        get
        {
            for (var index = 0; index < 64; index++)
            {
                if (_board[index] is { } piece)
                {
                    yield return (Square.FromIndex(index), piece);
                }
            }
        }
    }

    /// <summary>All legal moves for the side to move (computed once and cached).</summary>
    public IReadOnlyList<Move> LegalMoves => _legalMoves ??= MoveGenerator.GenerateLegalMoves(this);

    public bool IsCheck => IsInCheck(SideToMove);

    public bool IsCheckmate => IsCheck && LegalMoves.Count == 0;

    public bool IsStalemate => !IsCheck && LegalMoves.Count == 0;

    public bool IsLegal(Move move) => LegalMoves.Contains(move);

    /// <summary>Applies a legal move and returns the resulting position.</summary>
    /// <exception cref="InvalidOperationException">The move is not legal in this position.</exception>
    public Position MakeMove(Move move)
    {
        if (!IsLegal(move))
        {
            throw new InvalidOperationException($"Move {move} is not legal in position {ToFen()}.");
        }

        return Apply(move);
    }

    public Square? FindKing(PieceColor color)
    {
        var king = new Piece(PieceType.King, color);
        for (var index = 0; index < 64; index++)
        {
            if (_board[index] == king)
            {
                return Square.FromIndex(index);
            }
        }

        return null;
    }

    public bool IsInCheck(PieceColor color) =>
        FindKing(color) is { } kingSquare && IsSquareAttacked(kingSquare, color.Opposite());

    public bool IsSquareAttacked(Square target, PieceColor attacker)
    {
        // A pawn attacks diagonally forward, so an attacking pawn sits one rank "behind" the target.
        var pawn = new Piece(PieceType.Pawn, attacker);
        var pawnRank = target.Rank - attacker.PawnDirection();
        if ((Square.TryCreate(target.File - 1, pawnRank, out var left) && this[left] == pawn) ||
            (Square.TryCreate(target.File + 1, pawnRank, out var right) && this[right] == pawn))
        {
            return true;
        }

        if (IsAttackedByStepper(target, MoveGenerator.KnightOffsets, new Piece(PieceType.Knight, attacker)) ||
            IsAttackedByStepper(target, MoveGenerator.KingOffsets, new Piece(PieceType.King, attacker)))
        {
            return true;
        }

        return IsAttackedBySlider(target, MoveGenerator.DiagonalDirections, attacker, PieceType.Bishop) ||
               IsAttackedBySlider(target, MoveGenerator.StraightDirections, attacker, PieceType.Rook);
    }

    /// <summary>Describes the nature of a (legal) move in this position.</summary>
    public MoveFlags GetMoveFlags(Move move)
    {
        if (this[move.From] is not { } piece)
        {
            return MoveFlags.None;
        }

        var flags = MoveFlags.None;
        if (this[move.To] is not null)
        {
            flags |= MoveFlags.Capture;
        }

        if (piece.Type == PieceType.Pawn)
        {
            if (IsEnPassantCapture(move, piece))
            {
                flags |= MoveFlags.Capture | MoveFlags.EnPassant;
            }

            if (Math.Abs(move.To.Rank - move.From.Rank) == 2)
            {
                flags |= MoveFlags.DoublePawnPush;
            }

            if (move.Promotion is not null)
            {
                flags |= MoveFlags.Promotion;
            }
        }
        else if (IsCastling(move, piece))
        {
            flags |= MoveFlags.Castle;
        }

        return flags;
    }

    /// <summary>The piece captured by a move, including the pawn removed by en passant.</summary>
    public Piece? GetCapturedPiece(Move move)
    {
        if (this[move.To] is { } captured)
        {
            return captured;
        }

        return this[move.From] is { } piece && IsEnPassantCapture(move, piece)
            ? new Piece(PieceType.Pawn, piece.Color.Opposite())
            : null;
    }

    /// <summary>
    /// True when neither side can possibly deliver checkmate: K v K, K+minor v K, or only
    /// same-coloured bishops besides the kings.
    /// </summary>
    public bool HasInsufficientMaterial()
    {
        var minorPieces = 0;
        var bishopsOnLight = false;
        var bishopsOnDark = false;
        var hasKnight = false;

        foreach (var (square, piece) in Pieces)
        {
            switch (piece.Type)
            {
                case PieceType.King:
                    continue;
                case PieceType.Pawn or PieceType.Rook or PieceType.Queen:
                    return false;
                case PieceType.Knight:
                    hasKnight = true;
                    break;
                case PieceType.Bishop when square.IsLight:
                    bishopsOnLight = true;
                    break;
                case PieceType.Bishop:
                    bishopsOnDark = true;
                    break;
            }

            minorPieces++;
        }

        if (minorPieces <= 1)
        {
            return true;
        }

        // Any number of bishops that all live on the same square colour can never mate.
        return !hasKnight && !(bishopsOnLight && bishopsOnDark);
    }

    /// <summary>
    /// Whether <paramref name="color"/> still has enough material that it could win on time.
    /// A lone king, or king plus a single bishop or knight, is treated as insufficient.
    /// </summary>
    public bool HasMatingMaterial(PieceColor color)
    {
        var minorPieces = 0;
        foreach (var (_, piece) in Pieces)
        {
            if (piece.Color != color || piece.Type == PieceType.King)
            {
                continue;
            }

            if (piece.Type is PieceType.Pawn or PieceType.Rook or PieceType.Queen)
            {
                return true;
            }

            minorPieces++;
        }

        return minorPieces >= 2;
    }

    public string ToFen() => Fen.Serialize(this);

    public override string ToString() => ToFen();

    /// <summary>
    /// Identity of the position for repetition detection: placement, side to move, castling rights and
    /// the en passant square, the latter only when an en passant capture is actually available.
    /// </summary>
    internal string RepetitionKey
    {
        get
        {
            var enPassant = EnPassantSquare is { } target &&
                            LegalMoves.Any(m => m.To == target && this[m.From]?.Type == PieceType.Pawn)
                ? target.ToString()
                : "-";
            return $"{Fen.SerializePlacement(this)} {(SideToMove == PieceColor.White ? 'w' : 'b')} {(int)CastlingRights} {enPassant}";
        }
    }

    /// <summary>Applies a move without checking legality. Callers must pass a pseudo-legal move.</summary>
    internal Position Apply(Move move)
    {
        var board = (Piece?[])_board.Clone();
        var piece = board[move.From.Index] ?? throw new InvalidOperationException($"No piece on {move.From}.");
        var captured = board[move.To.Index];
        Square? enPassantSquare = null;

        if (piece.Type == PieceType.Pawn && IsEnPassantCapture(move, piece))
        {
            var capturedPawnSquare = new Square(move.To.File, move.From.Rank);
            captured = board[capturedPawnSquare.Index];
            board[capturedPawnSquare.Index] = null;
        }
        else if (piece.Type == PieceType.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2)
        {
            enPassantSquare = new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2);
        }
        else if (IsCastling(move, piece))
        {
            var (rookFromFile, rookToFile) = move.To.File > move.From.File ? (7, 5) : (0, 3);
            var rookFrom = new Square(rookFromFile, move.From.Rank);
            var rookTo = new Square(rookToFile, move.From.Rank);
            board[rookTo.Index] = board[rookFrom.Index];
            board[rookFrom.Index] = null;
        }

        board[move.To.Index] = move.Promotion is { } promotion ? new Piece(promotion, piece.Color) : piece;
        board[move.From.Index] = null;

        var castlingRights = CastlingRights & ~(RightsLostBy(move.From) | RightsLostBy(move.To));
        var halfmoveClock = piece.Type == PieceType.Pawn || captured is not null ? 0 : HalfmoveClock + 1;
        var fullmoveNumber = SideToMove == PieceColor.Black ? FullmoveNumber + 1 : FullmoveNumber;

        return new Position(board, SideToMove.Opposite(), castlingRights, enPassantSquare, halfmoveClock, fullmoveNumber);
    }

    private bool IsEnPassantCapture(Move move, Piece piece) =>
        piece.Type == PieceType.Pawn &&
        move.To == EnPassantSquare &&
        move.From.File != move.To.File &&
        this[move.To] is null;

    private static bool IsCastling(Move move, Piece piece) =>
        piece.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2;

    /// <summary>Castling rights removed when a piece moves from or to <paramref name="square"/>.</summary>
    private static CastlingRights RightsLostBy(Square square) => square.Index switch
    {
        0 => CastlingRights.WhiteQueenSide,  // a1
        4 => CastlingRights.White,           // e1
        7 => CastlingRights.WhiteKingSide,   // h1
        56 => CastlingRights.BlackQueenSide, // a8
        60 => CastlingRights.Black,          // e8
        63 => CastlingRights.BlackKingSide,  // h8
        _ => CastlingRights.None,
    };

    private bool IsAttackedByStepper(Square target, (int File, int Rank)[] offsets, Piece attacker)
    {
        foreach (var (fileDelta, rankDelta) in offsets)
        {
            if (target.TryOffset(fileDelta, rankDelta, out var from) && this[from] == attacker)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAttackedBySlider(Square target, (int File, int Rank)[] directions, PieceColor attacker, PieceType sliderType)
    {
        foreach (var (fileDelta, rankDelta) in directions)
        {
            var current = target;
            while (current.TryOffset(fileDelta, rankDelta, out current))
            {
                if (this[current] is not { } piece)
                {
                    continue;
                }

                if (piece.Color == attacker && (piece.Type == sliderType || piece.Type == PieceType.Queen))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }
}
