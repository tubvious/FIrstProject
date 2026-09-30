namespace Chess.Engine.Tests;

public class SpecialMoveTests
{
    [Fact]
    public void Castling_KingSide_MovesKingAndRook()
    {
        var game = TestGames.Play("e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "O-O");

        var position = game.CurrentPosition;
        Assert.Equal(new Piece(PieceType.King, PieceColor.White), position[Square.Parse("g1")]);
        Assert.Equal(new Piece(PieceType.Rook, PieceColor.White), position[Square.Parse("f1")]);
        Assert.Null(position[Square.Parse("h1")]);
        Assert.Null(position[Square.Parse("e1")]);
        Assert.True(game.LastMove!.Flags.HasFlag(MoveFlags.Castle));
        Assert.Equal("O-O", game.LastMove.San);
        Assert.Equal(CastlingRights.Black, position.CastlingRights);
    }

    [Fact]
    public void Castling_QueenSide_MovesKingAndRook()
    {
        var position = Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");

        var after = position.MakeMove(Move.ParseUci("e8c8"));

        Assert.Equal(new Piece(PieceType.King, PieceColor.Black), after[Square.Parse("c8")]);
        Assert.Equal(new Piece(PieceType.Rook, PieceColor.Black), after[Square.Parse("d8")]);
        Assert.Null(after[Square.Parse("a8")]);
        Assert.Equal(CastlingRights.White, after.CastlingRights);
    }

    [Theory]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", true)]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", true)]
    // Cannot castle out of check.
    [InlineData("4r1k1/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1g1", false)]
    // Cannot castle through an attacked square (f1 covered by the rook on f8).
    [InlineData("5rk1/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1g1", false)]
    // Cannot castle into check (g1 covered by the rook on g8).
    [InlineData("6rk/7p/8/8/8/8/8/R3K2R w KQ - 0 1", "e1g1", false)]
    // Queen-side: b1 may be attacked, only the king's path (d1, c1) matters.
    [InlineData("1r4k1/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1c1", true)]
    // Queen-side: blocked by a piece on b1.
    [InlineData("6k1/8/8/8/8/8/8/RN2K2R w KQ - 0 1", "e1c1", false)]
    // No castling rights left.
    [InlineData("6k1/8/8/8/8/8/8/R3K2R w - - 0 1", "e1g1", false)]
    public void Castling_RespectsRules(string fen, string uci, bool expectedLegal)
    {
        var position = Fen.Parse(fen);

        Assert.Equal(expectedLegal, position.IsLegal(Move.ParseUci(uci)));
    }

    [Fact]
    public void Castling_RightsAreLostAfterKingOrRookMoves()
    {
        var game = new ChessGame(Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1"));

        game.MakeMove("h1h2");
        Assert.Equal(CastlingRights.WhiteQueenSide | CastlingRights.Black, game.CurrentPosition.CastlingRights);

        game.MakeMove("e8d8");
        Assert.Equal(CastlingRights.WhiteQueenSide, game.CurrentPosition.CastlingRights);

        game.MakeMove("h2h1");
        game.MakeMove("d8e8");
        Assert.False(game.CurrentPosition.IsLegal(Move.ParseUci("e1g1")));
    }

    [Fact]
    public void Castling_RightIsLostWhenRookIsCaptured()
    {
        var position = Fen.Parse("r3k2r/8/8/8/8/8/6b1/R3K2R b KQkq - 0 1");

        var after = position.MakeMove(Move.ParseUci("g2h1"));

        Assert.False(after.CastlingRights.HasFlag(CastlingRights.WhiteKingSide));
        Assert.True(after.CastlingRights.HasFlag(CastlingRights.WhiteQueenSide));
    }

    [Fact]
    public void EnPassant_CapturesThePassedPawn()
    {
        var game = TestGames.Play("e4", "a6", "e5", "d5");

        Assert.Equal(Square.Parse("d6"), game.CurrentPosition.EnPassantSquare);
        var played = game.MakeMove("exd6");

        Assert.True(played.Flags.HasFlag(MoveFlags.EnPassant));
        Assert.Equal(new Piece(PieceType.Pawn, PieceColor.Black), played.CapturedPiece);
        Assert.Null(game.CurrentPosition[Square.Parse("d5")]);
        Assert.Equal(new Piece(PieceType.Pawn, PieceColor.White), game.CurrentPosition[Square.Parse("d6")]);
        Assert.Equal("exd6", played.San);
    }

    [Fact]
    public void EnPassant_ExpiresAfterOneMove()
    {
        var game = TestGames.Play("e4", "a6", "e5", "d5", "Nf3", "Nf6");

        Assert.False(game.CurrentPosition.IsLegal(Move.ParseUci("e5d6")));
    }

    [Fact]
    public void EnPassant_IsIllegalWhenItExposesTheKing()
    {
        // Capturing d5xe6 e.p. would remove both pawns from the 5th rank and expose the king on a5 to the rook on h5.
        var position = Fen.Parse("8/8/8/K2Pp2r/8/8/8/7k w - e6 0 1");

        Assert.False(position.IsLegal(Move.ParseUci("d5e6")));
    }

    [Theory]
    [InlineData("e7e8q", PieceType.Queen, "e8=Q+")]
    [InlineData("e7e8r", PieceType.Rook, "e8=R+")]
    [InlineData("e7e8b", PieceType.Bishop, "e8=B")]
    [InlineData("e7e8n", PieceType.Knight, "e8=N")]
    public void Promotion_ReplacesPawnWithChosenPiece(string uci, PieceType expected, string expectedSan)
    {
        var game = new ChessGame(Fen.Parse("k7/4P3/8/8/8/8/8/4K3 w - - 0 1"));

        var played = game.MakeMove(Move.ParseUci(uci));

        Assert.Equal(new Piece(expected, PieceColor.White), game.CurrentPosition[Square.Parse("e8")]);
        Assert.True(played.Flags.HasFlag(MoveFlags.Promotion));
        Assert.Equal(expectedSan, played.San);
    }

    [Fact]
    public void Promotion_RequiresAPromotionPiece()
    {
        var position = Fen.Parse("k7/4P3/8/8/8/8/8/4K3 w - - 0 1");

        Assert.False(position.IsLegal(Move.ParseUci("e7e8")));
        Assert.Equal(4, position.LegalMoves.Count(m => m.From == Square.Parse("e7")));
    }

    [Fact]
    public void Promotion_WithCapture()
    {
        var game = new ChessGame(Fen.Parse("3rk3/4P3/8/8/8/8/8/4K3 w - - 0 1"));

        var played = game.MakeMove("exd8=Q+");

        Assert.Equal(new Piece(PieceType.Rook, PieceColor.Black), played.CapturedPiece);
        Assert.Equal(new Piece(PieceType.Queen, PieceColor.White), game.CurrentPosition[Square.Parse("d8")]);
    }
}
