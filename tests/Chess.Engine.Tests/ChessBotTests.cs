using System.Diagnostics;
using Chess.Engine.Ai;

namespace Chess.Engine.Tests;

public class ChessBotTests
{
    public static TheoryData<int> StrongLevels => new() { 3, 4, 5 };

    public static TheoryData<int> AllLevels => new() { 1, 2, 3, 4, 5 };

    [Theory]
    [MemberData(nameof(StrongLevels))]
    public void FindsMateInOne(int level)
    {
        // Back-rank mate: Ra8#.
        var position = Fen.Parse("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1");

        var move = ChessBot.ChooseMove(position, BotLevel.Get(level), random: new Random(1));

        Assert.Equal(Move.ParseUci("a1a8"), move);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void FindsMateInTwo(int level)
    {
        // Rook ladder: there is no mate in one, but e.g. 1. Rb7 Kg8 2. Ra8# forces mate.
        var position = Fen.Parse("7k/8/8/8/8/8/R7/1R4K1 w - - 0 1");
        Assert.DoesNotContain(position.LegalMoves, m => position.Apply(m).IsCheckmate);

        var move = ChessBot.ChooseMove(position, BotLevel.Get(level), random: new Random(1))!.Value;

        var after = position.Apply(move);
        Assert.NotEmpty(after.LegalMoves);
        Assert.All(after.LegalMoves, reply =>
        {
            var next = after.Apply(reply);
            Assert.Contains(next.LegalMoves, finish => next.Apply(finish).IsCheckmate);
        });
    }

    [Theory]
    [MemberData(nameof(StrongLevels))]
    public void CapturesAHangingQueen(int level)
    {
        var position = Fen.Parse("rnb1kbnr/pppp1ppp/8/4p1q1/3P4/2N5/PPP1PPPP/R1BQKBNR w KQkq - 0 1");

        var move = ChessBot.ChooseMove(position, BotLevel.Get(level), random: new Random(1));

        Assert.Equal(Move.ParseUci("c1g5"), move);
    }

    [Theory]
    [MemberData(nameof(StrongLevels))]
    public void DoesNotLeaveItsQueenEnPrise(int level)
    {
        // The white queen on d4 is attacked by the knight on c6 and must move (or be defended).
        var game = new ChessGame(Fen.Parse("r1bqkbnr/pppp1ppp/2n5/8/3Q4/8/PPP1PPPP/RNB1KBNR w KQkq - 0 1"));

        var move = ChessBot.ChooseMove(game.CurrentPosition, BotLevel.Get(level), random: new Random(1))!.Value;
        var after = game.CurrentPosition.Apply(move);

        Assert.DoesNotContain(after.LegalMoves, reply => after.GetCapturedPiece(reply)?.Type == PieceType.Queen);
    }

    [Theory]
    [MemberData(nameof(AllLevels))]
    public void AlwaysPlaysLegalMoves_ThroughAWholeGame(int level)
    {
        var game = new ChessGame();
        var random = new Random(level);
        var bot = BotLevel.Get(level) with { ThinkTime = TimeSpan.FromMilliseconds(60) };

        while (!game.IsOver && game.Moves.Count < 40)
        {
            var move = ChessBot.ChooseMove(game.CurrentPosition, bot, random: random);
            Assert.NotNull(move);
            Assert.True(game.TryMakeMove(move.Value, out _), $"Level {level} chose illegal move {move} in {game.CurrentPosition.ToFen()}");
        }
    }

    [Fact]
    public void ReturnsNull_WhenThereAreNoLegalMoves()
    {
        var mated = TestGames.Play("f3", "e5", "g4", "Qh4#").CurrentPosition;

        Assert.Null(ChessBot.ChooseMove(mated, BotLevel.Get(5)));
    }

    [Fact]
    public void RespectsTheTimeBudget()
    {
        var middlegame = Fen.Parse("r1bq1rk1/pp2bppp/2n1pn2/3p4/2PP4/2N1PN2/PP2BPPP/R2QKB1R w KQ - 0 8");
        var stopwatch = Stopwatch.StartNew();

        var move = ChessBot.ChooseMove(middlegame, BotLevel.Get(5), timeBudget: TimeSpan.FromMilliseconds(300));

        Assert.NotNull(move);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void Evaluation_IsSymmetric()
    {
        Assert.Equal(0, Evaluator.Evaluate(Position.Initial));

        var whiteUp = Fen.Parse("4k3/8/8/8/8/8/8/3QK3 w - - 0 1");
        var sameFromBlack = Fen.Parse("4k3/8/8/8/8/8/8/3QK3 b - - 0 1");
        Assert.True(Evaluator.Evaluate(whiteUp) > 800);
        Assert.Equal(-Evaluator.Evaluate(whiteUp), Evaluator.Evaluate(sameFromBlack));
    }

    [Fact]
    public void Levels_AreDefinedFromOneToFive()
    {
        Assert.Equal([1, 2, 3, 4, 5], BotLevel.All.Select(l => l.Level));
        Assert.False(BotLevel.IsValid(0));
        Assert.False(BotLevel.IsValid(6));
        Assert.Equal("Medium", BotLevel.Get(3).Name);
    }
}
