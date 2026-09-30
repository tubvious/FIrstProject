using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chess.Web.Pages;

/// <summary>The game screen. Only checks that the game exists; live state arrives over SignalR.</summary>
public sealed class GameModel(IGameService games) : PageModel
{
    public string Code { get; private set; } = string.Empty;

    public GameSummaryDto? Summary { get; private set; }

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        if (!GameCode.TryNormalize(code, out var normalized) ||
            await games.GetSummaryAsync(normalized, cancellationToken) is not { } summary)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        // Keep one canonical URL per game so links look the same for everyone.
        if (!string.Equals(code, normalized, StringComparison.Ordinal))
        {
            return RedirectToPage(new { code = normalized });
        }

        Code = normalized;
        Summary = summary;
        return Page();
    }
}
