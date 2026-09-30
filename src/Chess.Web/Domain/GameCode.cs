using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Chess.Web.Domain;

/// <summary>Short, shareable game identifiers such as "K7QD2MXA".</summary>
public static class GameCode
{
    public const int Length = 8;

    // No 0/O or 1/I/L so codes are easy to read aloud and type.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Accepts user input in any case and with surrounding whitespace.</summary>
    public static bool TryNormalize([NotNullWhen(true)] string? input, [NotNullWhen(true)] out string? code)
    {
        code = null;
        var candidate = input?.Trim().ToUpperInvariant();
        if (candidate is null || candidate.Length != Length || !candidate.All(c => Alphabet.Contains(c, StringComparison.Ordinal)))
        {
            return false;
        }

        code = candidate;
        return true;
    }
}
