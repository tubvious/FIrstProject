namespace Chess.Engine.Tests;

/// <summary>
/// Perft counts every leaf of the legal move tree to a fixed depth. The expected values are the
/// well-known reference numbers from the Chess Programming Wiki; matching them exercises castling,
/// en passant, promotion, pins and checks far more thoroughly than hand-written cases.
/// </summary>
public class PerftTests
{
    [Theory]
    [InlineData(Fen.StartingPosition, 1, 20)]
    [InlineData(Fen.StartingPosition, 2, 400)]
    [InlineData(Fen.StartingPosition, 3, 8_902)]
    [InlineData(Fen.StartingPosition, 4, 197_281)]
    // "Kiwipete": dense tactics, castling both ways, en passant and promotions.
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2_039)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97_862)]
    // Position 3: en passant discovered-check edge cases in a sparse endgame.
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43_238)]
    // Position 4: promotions and castling out of check.
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9_467)]
    // Position 5.
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62_379)]
    // Position 6.
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89_890)]
    public void Perft_MatchesReferenceNodeCounts(string fen, int depth, long expectedNodes)
    {
        var position = Fen.Parse(fen);

        Assert.Equal(expectedNodes, Perft(position, depth));
    }

    private static long Perft(Position position, int depth)
    {
        var moves = position.LegalMoves;
        if (depth == 1)
        {
            return moves.Count;
        }

        long nodes = 0;
        foreach (var move in moves)
        {
            nodes += Perft(position.Apply(move), depth - 1);
        }

        return nodes;
    }
}
