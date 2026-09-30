using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Chess.Engine;

/// <summary>Standard Algebraic Notation (e.g. "Nf3", "exd5", "O-O", "e8=Q#").</summary>
public static class San
{
    /// <summary>Formats a legal move in SAN, including disambiguation and check/mate suffixes.</summary>
    public static string Format(Position position, Move move)
    {
        if (!position.IsLegal(move))
        {
            throw new InvalidOperationException($"Move {move} is not legal in position {position.ToFen()}.");
        }

        return Format(position, move, position.Apply(move));
    }

    internal static string Format(Position before, Move move, Position after)
    {
        var builder = new StringBuilder(8);
        AppendMoveText(builder, before, move);

        if (after.IsCheck)
        {
            builder.Append(after.LegalMoves.Count == 0 ? '#' : '+');
        }

        return builder.ToString();
    }

    public static Move Parse(Position position, string san) =>
        TryParse(position, san, out var move) ? move : throw new FormatException($"'{san}' is not a legal move in {position.ToFen()}.");

    /// <summary>Resolves SAN text against the legal moves of <paramref name="position"/>.</summary>
    public static bool TryParse(Position position, [NotNullWhen(true)] string? san, out Move move)
    {
        move = default;
        if (string.IsNullOrWhiteSpace(san))
        {
            return false;
        }

        var wanted = Normalize(san);
        foreach (var candidate in position.LegalMoves)
        {
            var builder = new StringBuilder(8);
            AppendMoveText(builder, position, candidate);
            if (builder.ToString() == wanted)
            {
                move = candidate;
                return true;
            }
        }

        return false;
    }

    private static void AppendMoveText(StringBuilder builder, Position position, Move move)
    {
        var piece = position[move.From] ?? throw new InvalidOperationException($"No piece on {move.From}.");
        var flags = position.GetMoveFlags(move);
        var isCapture = flags.HasFlag(MoveFlags.Capture);

        if (flags.HasFlag(MoveFlags.Castle))
        {
            builder.Append(move.To.File > move.From.File ? "O-O" : "O-O-O");
            return;
        }

        if (piece.Type == PieceType.Pawn)
        {
            if (isCapture)
            {
                builder.Append((char)('a' + move.From.File)).Append('x');
            }

            builder.Append(move.To);
            if (move.Promotion is { } promotion)
            {
                builder.Append('=').Append(promotion.ToLetter());
            }

            return;
        }

        builder.Append(piece.Type.ToLetter());
        AppendDisambiguation(builder, position, move, piece.Type);
        if (isCapture)
        {
            builder.Append('x');
        }

        builder.Append(move.To);
    }

    private static void AppendDisambiguation(StringBuilder builder, Position position, Move move, PieceType type)
    {
        var rivals = position.LegalMoves
            .Where(m => m.To == move.To && m.From != move.From && position[m.From]?.Type == type)
            .Select(m => m.From)
            .ToList();

        if (rivals.Count == 0)
        {
            return;
        }

        var fileChar = (char)('a' + move.From.File);
        var rankChar = (char)('1' + move.From.Rank);
        if (rivals.All(square => square.File != move.From.File))
        {
            builder.Append(fileChar);
        }
        else if (rivals.All(square => square.Rank != move.From.Rank))
        {
            builder.Append(rankChar);
        }
        else
        {
            builder.Append(fileChar).Append(rankChar);
        }
    }

    private static string Normalize(string san) =>
        san.Trim().TrimEnd('+', '#', '!', '?').Replace("0-0-0", "O-O-O").Replace("0-0", "O-O");
}
