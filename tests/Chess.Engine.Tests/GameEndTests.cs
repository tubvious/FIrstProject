namespace Chess.Engine.Tests;

public class GameEndTests
{
    [Fact]
    public void FoolsMate_IsCheckmateForBlack()
    {
        var game = TestGames.Play("f3", "e5", "g4", "Qh4#");

        Assert.True(game.IsOver);
        Assert.Equal(GameOutcome.Win(PieceColor.Black, GameEndReason.Checkmate), game.Outcome);
        Assert.True(game.LastMove!.IsCheckmate);
        Assert.Empty(game.LegalMoves);
    }

    [Fact]
    public void ScholarsMate_IsCheckmateForWhite()
    {
        var game = TestGames.Play("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#");

        Assert.Equal(GameOutcome.Win(PieceColor.White, GameEndReason.Checkmate), game.Outcome);
        Assert.Equal("Qxf7#", game.LastMove!.San);
    }

    [Fact]
    public void Check_IsDetectedAndMustBeAnswered()
    {
        var game = TestGames.Play("e4", "d5", "Bb5+");

        Assert.True(game.CurrentPosition.IsCheck);
        Assert.False(game.IsOver);
        Assert.Equal("Bb5+", game.LastMove!.San);
        // Every legal reply must resolve the check.
        Assert.All(game.LegalMoves, move => Assert.False(game.CurrentPosition.Apply(move).IsInCheck(PieceColor.Black)));
        Assert.False(game.CurrentPosition.IsLegal(Move.ParseUci("a7a6")));
    }

    [Fact]
    public void PinnedPiece_CannotLeaveThePin()
    {
        var position = Fen.Parse("4k3/4r3/8/8/8/8/4N3/4K3 w - - 0 1");

        Assert.DoesNotContain(position.LegalMoves, move => move.From == Square.Parse("e2"));
    }

    [Fact]
    public void Stalemate_IsADraw()
    {
        var game = new ChessGame(Fen.Parse("7k/8/6Q1/8/8/8/8/K7 w - - 0 1"));

        game.MakeMove("Qf7");

        Assert.Equal(GameOutcome.Draw(GameEndReason.Stalemate), game.Outcome);
        Assert.True(game.CurrentPosition.IsStalemate);
    }

    [Theory]
    [InlineData("8/8/8/4k3/8/8/8/4K3 w - - 0 1", true)]          // K v K
    [InlineData("8/8/8/4k3/8/8/8/4KB2 w - - 0 1", true)]         // K+B v K
    [InlineData("8/8/8/4k3/8/8/8/4KN2 w - - 0 1", true)]         // K+N v K
    [InlineData("8/8/8/3bk3/8/8/8/4KB2 w - - 0 1", true)]        // bishops on the same colour (d5, f1)
    [InlineData("8/8/8/2b1k3/8/8/8/4KB2 w - - 0 1", false)]      // bishops on opposite colours (c5, f1)
    [InlineData("8/8/8/4k3/8/8/8/3NKN2 w - - 0 1", false)]       // K+N+N v K (mate is possible)
    [InlineData("8/8/8/4k3/8/8/4P3/4K3 w - - 0 1", false)]       // a pawn can promote
    [InlineData("8/8/8/4k3/8/8/8/4KR2 w - - 0 1", false)]
    public void InsufficientMaterial_IsDetected(string fen, bool expected)
    {
        Assert.Equal(expected, Fen.Parse(fen).HasInsufficientMaterial());
    }

    [Fact]
    public void CapturingLastPiece_EndsInDrawByInsufficientMaterial()
    {
        var game = new ChessGame(Fen.Parse("8/8/8/4k3/4r3/8/8/4K2B w - - 0 1"));

        game.MakeMove("Bxe4");

        Assert.Equal(GameOutcome.Draw(GameEndReason.InsufficientMaterial), game.Outcome);
    }

    [Fact]
    public void ThreefoldRepetition_IsADraw()
    {
        var game = TestGames.Play("Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1");
        Assert.False(game.IsOver);

        game.MakeMove("Ng8");

        Assert.Equal(GameOutcome.Draw(GameEndReason.ThreefoldRepetition), game.Outcome);
    }

    [Fact]
    public void FiftyMoveRule_IsADraw()
    {
        var game = new ChessGame(Fen.Parse("8/8/8/4k3/8/8/1R6/4K3 w - - 99 80"));

        game.MakeMove("Rb3");

        Assert.Equal(GameOutcome.Draw(GameEndReason.FiftyMoveRule), game.Outcome);
    }

    [Fact]
    public void CheckmateOnTheHundredthHalfMove_TakesPrecedence()
    {
        var game = new ChessGame(Fen.Parse("k7/8/1K6/8/8/8/8/7R w - - 99 80"));

        game.MakeMove("Rh8#");

        Assert.Equal(GameOutcome.Win(PieceColor.White, GameEndReason.Checkmate), game.Outcome);
    }

    [Fact]
    public void Timeout_WinsForOpponentWithMatingMaterial()
    {
        var game = new ChessGame();

        game.Timeout(PieceColor.White);

        Assert.Equal(GameOutcome.Win(PieceColor.Black, GameEndReason.Timeout), game.Outcome);
    }

    [Fact]
    public void Timeout_IsADrawWhenOpponentCannotMate()
    {
        var game = new ChessGame(Fen.Parse("8/8/8/4k3/8/8/4P3/4KQ2 w - - 0 1"));

        game.Timeout(PieceColor.White);

        Assert.Equal(GameOutcome.Draw(GameEndReason.TimeoutVsInsufficientMaterial), game.Outcome);
    }

    [Fact]
    public void Resignation_AndAgreement_EndTheGame()
    {
        var resigned = new ChessGame();
        resigned.Resign(PieceColor.Black);
        Assert.Equal(GameOutcome.Win(PieceColor.White, GameEndReason.Resignation), resigned.Outcome);

        var agreed = new ChessGame();
        agreed.AgreeDraw();
        Assert.Equal(GameOutcome.Draw(GameEndReason.Agreement), agreed.Outcome);
    }

    [Fact]
    public void NoMovesAreAccepted_AfterTheGameIsOver()
    {
        var game = TestGames.Play("f3", "e5", "g4", "Qh4#");

        Assert.False(game.TryMakeMove(Move.ParseUci("a2a3"), out _));
        Assert.Throws<InvalidOperationException>(() => game.Resign(PieceColor.White));
    }

    [Fact]
    public void IllegalMoves_AreRejected()
    {
        var game = new ChessGame();

        Assert.False(game.TryMakeMove(Move.ParseUci("e2e5"), out _)); // pawn cannot jump three squares
        Assert.False(game.TryMakeMove(Move.ParseUci("e7e5"), out _)); // not Black's turn
        Assert.False(game.TryMakeMove(Move.ParseUci("f1c4"), out _)); // bishop is blocked
        Assert.False(game.TryMakeMove(Move.ParseUci("g1g3"), out _)); // knights do not move like that
        Assert.Empty(game.Moves);
    }
}
