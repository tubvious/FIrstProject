using System.Diagnostics.CodeAnalysis;

namespace Chess.Engine;

/// <summary>A board square addressed by 0-based file (a..h) and rank (1..8).</summary>
public readonly record struct Square
{
    public Square(int file, int rank)
    {
        if (!IsOnBoard(file, rank))
        {
            throw new ArgumentOutOfRangeException(nameof(file), $"Square ({file}, {rank}) is off the board.");
        }

        File = file;
        Rank = rank;
    }

    public int File { get; }

    public int Rank { get; }

    /// <summary>0..63, a1 = 0, h1 = 7, a8 = 56, h8 = 63.</summary>
    public int Index => Rank * 8 + File;

    public bool IsLight => (File + Rank) % 2 == 1;

    public static Square FromIndex(int index) => new(index % 8, index / 8);

    public static bool IsOnBoard(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;

    public static bool TryCreate(int file, int rank, out Square square)
    {
        if (IsOnBoard(file, rank))
        {
            square = new Square(file, rank);
            return true;
        }

        square = default;
        return false;
    }

    public bool TryOffset(int fileDelta, int rankDelta, out Square square) =>
        TryCreate(File + fileDelta, Rank + rankDelta, out square);

    public static Square Parse(string text) =>
        TryParse(text, out var square) ? square : throw new FormatException($"'{text}' is not a valid square.");

    public static bool TryParse([NotNullWhen(true)] string? text, out Square square)
    {
        square = default;
        if (text is null || text.Length != 2)
        {
            return false;
        }

        return TryCreate(char.ToLowerInvariant(text[0]) - 'a', text[1] - '1', out square);
    }

    public override string ToString() => $"{(char)('a' + File)}{(char)('1' + Rank)}";
}
