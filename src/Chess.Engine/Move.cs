using System.Diagnostics.CodeAnalysis;

namespace Chess.Engine;

/// <summary>
/// A move expressed as a player's intent: source, destination and (for pawns reaching the last rank)
/// the promotion piece. Castling is expressed as the king moving two squares, as in UCI.
/// </summary>
public readonly record struct Move(Square From, Square To, PieceType? Promotion = null)
{
    public string ToUci() =>
        Promotion is { } promotion
            ? $"{From}{To}{char.ToLowerInvariant(promotion.ToLetter())}"
            : $"{From}{To}";

    public static Move ParseUci(string uci) =>
        TryParseUci(uci, out var move) ? move : throw new FormatException($"'{uci}' is not a valid UCI move.");

    public static bool TryParseUci([NotNullWhen(true)] string? uci, out Move move)
    {
        move = default;
        if (uci is null || uci.Length is not (4 or 5))
        {
            return false;
        }

        if (!Square.TryParse(uci[..2], out var from) || !Square.TryParse(uci[2..4], out var to) || from == to)
        {
            return false;
        }

        PieceType? promotion = null;
        if (uci.Length == 5)
        {
            if (!PieceTypeExtensions.TryFromLetter(uci[4], out var type) || !type.IsValidPromotion())
            {
                return false;
            }

            promotion = type;
        }

        move = new Move(from, to, promotion);
        return true;
    }

    public override string ToString() => ToUci();
}

[Flags]
public enum MoveFlags
{
    None = 0,
    Capture = 1,
    EnPassant = 2,
    Castle = 4,
    Promotion = 8,
    DoublePawnPush = 16,
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    White = WhiteKingSide | WhiteQueenSide,
    Black = BlackKingSide | BlackQueenSide,
    All = White | Black,
}
