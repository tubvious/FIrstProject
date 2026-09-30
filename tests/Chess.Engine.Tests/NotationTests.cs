namespace Chess.Engine.Tests;

public class NotationTests
{
    [Theory]
    [InlineData(Fen.StartingPosition)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2")]
    [InlineData("8/8/8/4k3/8/8/8/4K3 b - - 12 40")]
    public void Fen_RoundTrips(string fen)
    {
        Assert.Equal(fen, Fen.Parse(fen).ToFen());
    }

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]            // 7 ranks
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]   // bad side to move
    [InlineData("rnbqqbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]   // no black king
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]   // too many squares
    [InlineData("4k3/8/8/8/8/8/8/4K2r b - - 0 1")]                             // side not to move in check
    public void Fen_RejectsInvalidInput(string fen)
    {
        Assert.False(Fen.TryParse(fen, out _));
    }

    [Fact]
    public void Fen_DropsImpossibleCastlingRights()
    {
        var position = Fen.Parse("4k3/8/8/8/8/8/8/4K3 w KQkq - 0 1");

        Assert.Equal(CastlingRights.None, position.CastlingRights);
    }

    [Fact]
    public void San_DisambiguatesByFile()
    {
        var position = Fen.Parse("4k3/8/8/8/8/8/8/R4RK1 w - - 0 1");

        Assert.Equal("Rad1", San.Format(position, Move.ParseUci("a1d1")));
    }

    [Fact]
    public void San_DisambiguatesByRank()
    {
        var position = Fen.Parse("4k3/R7/8/8/8/8/8/R3K3 w - - 0 1");

        Assert.Equal("R1a4", San.Format(position, Move.ParseUci("a1a4")));
    }

    [Fact]
    public void San_DisambiguatesByFileAndRank()
    {
        var position = Fen.Parse("4k3/8/8/8/8/Q1Q5/8/Q3K3 w - - 0 1");

        Assert.Equal("Qa3b2", San.Format(position, Move.ParseUci("a3b2")));
    }

    [Fact]
    public void San_ParsesCommonForms()
    {
        var position = Position.Initial;

        Assert.Equal(Move.ParseUci("g1f3"), San.Parse(position, "Nf3"));
        Assert.Equal(Move.ParseUci("e2e4"), San.Parse(position, "e4"));
        Assert.False(San.TryParse(position, "Ke2", out _));
    }

    [Theory]
    [InlineData("e2e4", true)]
    [InlineData("e7e8q", true)]
    [InlineData("e7e8k", false)]
    [InlineData("e2e2", false)]
    [InlineData("i2e4", false)]
    [InlineData("e2", false)]
    public void Uci_ParsingValidatesFormat(string uci, bool valid)
    {
        Assert.Equal(valid, Move.TryParseUci(uci, out _));
    }

    [Fact]
    public void PlayedMove_ReportsMoveNumbers()
    {
        var game = TestGames.Play("e4", "e5", "Nf3");

        Assert.Equal([1, 1, 2], game.Moves.Select(m => m.MoveNumber));
        Assert.Equal(["e4", "e5", "Nf3"], game.Moves.Select(m => m.San));
    }
}
