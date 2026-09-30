namespace Chess.Engine.Ai;

/// <summary>
/// How strongly the computer plays. Weaker levels search less deeply, blur their judgement with
/// random noise, and occasionally play a random move, so they make human-like mistakes.
/// </summary>
/// <param name="Level">1 (weakest) to 5 (strongest).</param>
/// <param name="MaxDepth">Maximum search depth in half-moves (captures are always followed further).</param>
/// <param name="ThinkTime">Upper bound on thinking time per move.</param>
/// <param name="NoiseCentipawns">Random error added to each candidate move's score.</param>
/// <param name="RandomMoveChance">Probability of simply playing a random legal move.</param>
public sealed record BotLevel(int Level, string Name, int MaxDepth, TimeSpan ThinkTime, int NoiseCentipawns, double RandomMoveChance)
{
    public const int Min = 1;
    public const int Max = 5;

    public static IReadOnlyList<BotLevel> All { get; } =
    [
        new(1, "Beginner", MaxDepth: 1, TimeSpan.FromMilliseconds(300), NoiseCentipawns: 300, RandomMoveChance: 0.3),
        new(2, "Easy", MaxDepth: 2, TimeSpan.FromMilliseconds(500), NoiseCentipawns: 120, RandomMoveChance: 0.08),
        new(3, "Medium", MaxDepth: 3, TimeSpan.FromMilliseconds(900), NoiseCentipawns: 40, RandomMoveChance: 0.02),
        new(4, "Hard", MaxDepth: 4, TimeSpan.FromMilliseconds(1500), NoiseCentipawns: 0, RandomMoveChance: 0),
        new(5, "Expert", MaxDepth: 8, TimeSpan.FromMilliseconds(2500), NoiseCentipawns: 0, RandomMoveChance: 0),
    ];

    public static bool IsValid(int level) => level is >= Min and <= Max;

    public static BotLevel Get(int level) =>
        IsValid(level) ? All[level - 1] : throw new ArgumentOutOfRangeException(nameof(level), level, "Bot levels are 1 to 5.");
}
