using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chess.Web.Pages;

public sealed class IndexModel : PageModel
{
    /// <summary>Time controls offered when creating a game. Minutes = null means no clock.</summary>
    public static IReadOnlyList<TimeControlOption> TimeControls { get; } =
    [
        new(null, 0, "∞", "No clock"),
        new(1, 0, "1+0", "Bullet"),
        new(3, 0, "3+0", "Blitz"),
        new(3, 2, "3+2", "Blitz"),
        new(5, 0, "5+0", "Blitz"),
        new(5, 3, "5+3", "Blitz"),
        new(10, 0, "10+0", "Rapid"),
        new(10, 5, "10+5", "Rapid"),
        new(15, 10, "15+10", "Rapid"),
    ];

    public const string DefaultTimeControl = "10+0";

    public const int DefaultBotLevel = 3;
}

public sealed record TimeControlOption(int? Minutes, int IncrementSeconds, string Label, string Category)
{
    public string Value => Minutes is null ? "none" : Label;
}
