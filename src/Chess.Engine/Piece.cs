namespace Chess.Engine;

public enum PieceType : byte
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

public readonly record struct Piece(PieceType Type, PieceColor Color)
{
    /// <summary>FEN letter: upper case for White, lower case for Black.</summary>
    public char ToFenChar()
    {
        var letter = Type.ToLetter();
        return Color == PieceColor.White ? letter : char.ToLowerInvariant(letter);
    }

    public static bool TryFromFenChar(char c, out Piece piece)
    {
        piece = default;
        if (!PieceTypeExtensions.TryFromLetter(c, out var type))
        {
            return false;
        }

        piece = new Piece(type, char.IsUpper(c) ? PieceColor.White : PieceColor.Black);
        return true;
    }

    public override string ToString() => ToFenChar().ToString();
}

public static class PieceTypeExtensions
{
    /// <summary>Upper-case letter used in SAN/FEN (P, N, B, R, Q, K).</summary>
    public static char ToLetter(this PieceType type) => type switch
    {
        PieceType.Pawn => 'P',
        PieceType.Knight => 'N',
        PieceType.Bishop => 'B',
        PieceType.Rook => 'R',
        PieceType.Queen => 'Q',
        PieceType.King => 'K',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static bool TryFromLetter(char letter, out PieceType type)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'P': type = PieceType.Pawn; return true;
            case 'N': type = PieceType.Knight; return true;
            case 'B': type = PieceType.Bishop; return true;
            case 'R': type = PieceType.Rook; return true;
            case 'Q': type = PieceType.Queen; return true;
            case 'K': type = PieceType.King; return true;
            default: type = default; return false;
        }
    }

    /// <summary>Conventional material value used for display purposes (king has no material value).</summary>
    public static int MaterialValue(this PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight => 3,
        PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0,
    };

    public static bool IsValidPromotion(this PieceType type) =>
        type is PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.Knight;
}
