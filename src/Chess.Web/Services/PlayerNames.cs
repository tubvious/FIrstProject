using System.Text;
using Chess.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Chess.Web.Services;

/// <summary>Cleans up user-supplied display names.</summary>
public sealed class PlayerNames(IOptions<GameOptions> options)
{
    private const string Fallback = "Anonymous";

    public string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        // Drop control/format characters and collapse runs of whitespace.
        var builder = new StringBuilder(name.Length);
        var previousWasSpace = false;
        foreach (var c in name.Trim())
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            var isSpace = char.IsWhiteSpace(c);
            if (!(isSpace && previousWasSpace))
            {
                builder.Append(isSpace ? ' ' : c);
            }

            previousWasSpace = isSpace;
        }

        var maxLength = options.Value.MaxPlayerNameLength;
        var cleaned = builder.ToString().Trim();
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].TrimEnd();
        }

        return cleaned.Length > 0 ? cleaned : Fallback;
    }
}
