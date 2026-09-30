using System.Diagnostics.CodeAnalysis;

namespace Chess.Web.Domain;

/// <summary>Initial time per player plus a Fischer increment added after every move.</summary>
public sealed record TimeControl(TimeSpan Initial, TimeSpan Increment)
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 180;
    public const int MaxIncrementSeconds = 60;

    public static bool TryCreate(int minutes, int incrementSeconds, [NotNullWhen(true)] out TimeControl? timeControl)
    {
        timeControl = null;
        if (minutes is < MinMinutes or > MaxMinutes || incrementSeconds is < 0 or > MaxIncrementSeconds)
        {
            return false;
        }

        timeControl = new TimeControl(TimeSpan.FromMinutes(minutes), TimeSpan.FromSeconds(incrementSeconds));
        return true;
    }

    public override string ToString() => $"{Initial.TotalMinutes:0}+{Increment.TotalSeconds:0}";
}
