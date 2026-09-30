using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Chess.Engine;

/// <summary>Forsyth–Edwards Notation parsing and serialization.</summary>
public static class Fen
{
    public const string StartingPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static Position Parse(string fen) =>
        TryParse(fen, out var position, out var error) ? position : throw new FormatException($"Invalid FEN '{fen}': {error}");

    public static bool TryParse([NotNullWhen(true)] string? fen, [NotNullWhen(true)] out Position? position) =>
        TryParse(fen, out position, out _);

    public static bool TryParse(
        [NotNullWhen(true)] string? fen,
        [NotNullWhen(true)] out Position? position,
        [NotNullWhen(false)] out string? error)
    {
        position = null;
        var fields = fen?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (fields.Length is < 4 or > 6)
        {
            error = "expected 4 to 6 space-separated fields";
            return false;
        }

        if (!TryParsePlacement(fields[0], out var board, out error))
        {
            return false;
        }

        PieceColor sideToMove;
        switch (fields[1])
        {
            case "w": sideToMove = PieceColor.White; break;
            case "b": sideToMove = PieceColor.Black; break;
            default: error = "side to move must be 'w' or 'b'"; return false;
        }

        if (!TryParseCastling(fields[2], out var castling))
        {
            error = "invalid castling field";
            return false;
        }

        Square? enPassant = null;
        if (fields[3] != "-")
        {
            if (!Square.TryParse(fields[3], out var square) || square.Rank != (sideToMove == PieceColor.White ? 5 : 2))
            {
                error = "invalid en passant square";
                return false;
            }

            enPassant = square;
        }

        var halfmove = 0;
        var fullmove = 1;
        if ((fields.Length > 4 && (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out halfmove))) ||
            (fields.Length > 5 && (!int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out fullmove) || fullmove < 1)))
        {
            error = "invalid move counters";
            return false;
        }

        castling = SanitizeCastlingRights(board, castling);
        var candidate = new Position(board, sideToMove, castling, enPassant, halfmove, fullmove);
        if (candidate.IsInCheck(sideToMove.Opposite()))
        {
            error = "the side not to move is in check";
            return false;
        }

        position = candidate;
        error = null;
        return true;
    }

    public static string Serialize(Position position)
    {
        var builder = new StringBuilder(SerializePlacement(position));
        builder.Append(position.SideToMove == PieceColor.White ? " w " : " b ");
        builder.Append(SerializeCastling(position.CastlingRights));
        builder.Append(' ').Append(position.EnPassantSquare?.ToString() ?? "-");
        builder.Append(' ').Append(position.HalfmoveClock.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ').Append(position.FullmoveNumber.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    internal static string SerializePlacement(Position position)
    {
        var builder = new StringBuilder(72);
        for (var rank = 7; rank >= 0; rank--)
        {
            var emptyRun = 0;
            for (var file = 0; file < 8; file++)
            {
                if (position[new Square(file, rank)] is { } piece)
                {
                    if (emptyRun > 0)
                    {
                        builder.Append(emptyRun);
                        emptyRun = 0;
                    }

                    builder.Append(piece.ToFenChar());
                }
                else
                {
                    emptyRun++;
                }
            }

            if (emptyRun > 0)
            {
                builder.Append(emptyRun);
            }

            if (rank > 0)
            {
                builder.Append('/');
            }
        }

        return builder.ToString();
    }

    private static bool TryParsePlacement(string placement, out Piece?[] board, [NotNullWhen(false)] out string? error)
    {
        board = new Piece?[64];
        var ranks = placement.Split('/');
        if (ranks.Length != 8)
        {
            error = "placement must have 8 ranks";
            return false;
        }

        var kings = new Dictionary<PieceColor, int> { [PieceColor.White] = 0, [PieceColor.Black] = 0 };
        for (var i = 0; i < 8; i++)
        {
            var rank = 7 - i;
            var file = 0;
            foreach (var c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    file += c - '0';
                    continue;
                }

                if (!Piece.TryFromFenChar(c, out var piece) || file > 7)
                {
                    error = $"invalid rank '{ranks[i]}'";
                    return false;
                }

                if (piece.Type == PieceType.Pawn && rank is 0 or 7)
                {
                    error = "pawns cannot stand on the first or last rank";
                    return false;
                }

                if (piece.Type == PieceType.King)
                {
                    kings[piece.Color]++;
                }

                board[new Square(file, rank).Index] = piece;
                file++;
            }

            if (file != 8)
            {
                error = $"rank '{ranks[i]}' does not describe 8 squares";
                return false;
            }
        }

        if (kings[PieceColor.White] != 1 || kings[PieceColor.Black] != 1)
        {
            error = "each side must have exactly one king";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseCastling(string field, out CastlingRights rights)
    {
        rights = CastlingRights.None;
        if (field == "-")
        {
            return true;
        }

        foreach (var c in field)
        {
            var flag = c switch
            {
                'K' => CastlingRights.WhiteKingSide,
                'Q' => CastlingRights.WhiteQueenSide,
                'k' => CastlingRights.BlackKingSide,
                'q' => CastlingRights.BlackQueenSide,
                _ => (CastlingRights?)null,
            };

            if (flag is null || rights.HasFlag(flag.Value))
            {
                return false;
            }

            rights |= flag.Value;
        }

        return true;
    }

    private static string SerializeCastling(CastlingRights rights)
    {
        if (rights == CastlingRights.None)
        {
            return "-";
        }

        var builder = new StringBuilder(4);
        if (rights.HasFlag(CastlingRights.WhiteKingSide)) builder.Append('K');
        if (rights.HasFlag(CastlingRights.WhiteQueenSide)) builder.Append('Q');
        if (rights.HasFlag(CastlingRights.BlackKingSide)) builder.Append('k');
        if (rights.HasFlag(CastlingRights.BlackQueenSide)) builder.Append('q');
        return builder.ToString();
    }

    /// <summary>Drops castling rights whose king or rook is not on its original square.</summary>
    private static CastlingRights SanitizeCastlingRights(Piece?[] board, CastlingRights rights)
    {
        bool Has(int index, PieceType type, PieceColor color) => board[index] == new Piece(type, color);

        if (!Has(4, PieceType.King, PieceColor.White)) rights &= ~CastlingRights.White;
        if (!Has(7, PieceType.Rook, PieceColor.White)) rights &= ~CastlingRights.WhiteKingSide;
        if (!Has(0, PieceType.Rook, PieceColor.White)) rights &= ~CastlingRights.WhiteQueenSide;
        if (!Has(60, PieceType.King, PieceColor.Black)) rights &= ~CastlingRights.Black;
        if (!Has(63, PieceType.Rook, PieceColor.Black)) rights &= ~CastlingRights.BlackKingSide;
        if (!Has(56, PieceType.Rook, PieceColor.Black)) rights &= ~CastlingRights.BlackQueenSide;
        return rights;
    }
}
