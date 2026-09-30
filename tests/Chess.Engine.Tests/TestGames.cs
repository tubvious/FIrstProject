namespace Chess.Engine.Tests;

internal static class TestGames
{
    /// <summary>Plays a sequence of SAN moves from the initial position.</summary>
    public static ChessGame Play(params string[] sanMoves)
    {
        var game = new ChessGame();
        foreach (var san in sanMoves)
        {
            game.MakeMove(san);
        }

        return game;
    }
}
