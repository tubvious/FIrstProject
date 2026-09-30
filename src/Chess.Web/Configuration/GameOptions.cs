namespace Chess.Web.Configuration;

/// <summary>Tunable game-server behaviour, bound from the "Chess" configuration section.</summary>
public sealed class GameOptions
{
    public const string SectionName = "Chess";

    /// <summary>How long a disconnected player has to come back before the opponent may claim the game.</summary>
    public TimeSpan ReconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often running clocks are checked for flag falls.</summary>
    public TimeSpan ClockCheckInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Games with nobody connected are unloaded from memory after this long (they stay in the database).</summary>
    public TimeSpan IdleGameEvictionAfter { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan EvictionSweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int MaxPlayerNameLength { get; set; } = 24;

    /// <summary>Maximum number of games a single client IP may create per minute.</summary>
    public int GameCreationPerMinuteLimit { get; set; } = 20;

    internal bool IsValid() =>
        ReconnectGracePeriod > TimeSpan.Zero &&
        ClockCheckInterval > TimeSpan.Zero &&
        IdleGameEvictionAfter > TimeSpan.Zero &&
        EvictionSweepInterval > TimeSpan.Zero &&
        MaxPlayerNameLength is > 0 and <= 64 &&
        GameCreationPerMinuteLimit > 0;
}
