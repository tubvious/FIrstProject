namespace Chess.Engine;

public enum GameResult
{
    WhiteWins,
    BlackWins,
    Draw,

    /// <summary>The game was cancelled before it really started; nobody wins.</summary>
    Aborted,
}

public enum GameEndReason
{
    Checkmate,
    Resignation,
    Timeout,
    Abandonment,
    Stalemate,
    InsufficientMaterial,
    ThreefoldRepetition,
    FiftyMoveRule,
    Agreement,
    TimeoutVsInsufficientMaterial,
    Aborted,
}

public sealed record GameOutcome(GameResult Result, GameEndReason Reason)
{
    public PieceColor? Winner => Result switch
    {
        GameResult.WhiteWins => PieceColor.White,
        GameResult.BlackWins => PieceColor.Black,
        _ => null,
    };

    public static GameOutcome Win(PieceColor winner, GameEndReason reason) =>
        new(winner == PieceColor.White ? GameResult.WhiteWins : GameResult.BlackWins, reason);

    public static GameOutcome Draw(GameEndReason reason) => new(GameResult.Draw, reason);

    public static GameOutcome Abort() => new(GameResult.Aborted, GameEndReason.Aborted);
}
