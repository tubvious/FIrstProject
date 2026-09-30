using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Chess.Web.Domain;

/// <summary>
/// Secret bearer tokens that prove ownership of a seat in a game. Only a SHA-256 hash is kept on
/// the server, so a leaked database cannot be used to take over seats.
/// </summary>
public static class SeatToken
{
    private const int ByteLength = 32;

    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(ByteLength));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool IsWellFormed([NotNullWhen(true)] string? token) =>
        token is { Length: ByteLength * 2 } && token.All(char.IsAsciiHexDigit);

    public static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
