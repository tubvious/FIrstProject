using Chess.Web.Configuration;
using Chess.Web.Domain;
using Chess.Web.Services;
using Microsoft.Extensions.Options;

namespace Chess.Web.Tests;

public class DomainValueTests
{
    [Fact]
    public void GameCodes_AreEightUnambiguousCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = GameCode.Generate();
            Assert.Equal(GameCode.Length, code.Length);
            Assert.DoesNotContain(code, c => "01IOL".Contains(c));
            Assert.True(GameCode.TryNormalize(code, out _));
        }
    }

    [Theory]
    [InlineData(" k7qd2mxa ", "K7QD2MXA")]
    [InlineData("ABCD2345", "ABCD2345")]
    public void GameCodes_AreNormalized(string input, string expected)
    {
        Assert.True(GameCode.TryNormalize(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCD1234")] // contains '1'
    [InlineData("ABCD-234")]
    [InlineData("ABCDEFGH9")]
    public void GameCodes_RejectInvalidInput(string? input)
    {
        Assert.False(GameCode.TryNormalize(input, out _));
    }

    [Fact]
    public void SeatTokens_AreRandom_AndOnlyTheirHashIsComparable()
    {
        var token = SeatToken.Generate();

        Assert.True(SeatToken.IsWellFormed(token));
        Assert.NotEqual(token, SeatToken.Generate());
        Assert.NotEqual(token, SeatToken.Hash(token));
        Assert.True(SeatToken.HashesEqual(SeatToken.Hash(token), SeatToken.Hash(token)));
        Assert.False(SeatToken.IsWellFormed("not-a-token"));
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(180, 60, true)]
    [InlineData(0, 0, false)]
    [InlineData(181, 0, false)]
    [InlineData(5, 61, false)]
    [InlineData(5, -1, false)]
    public void TimeControls_AreBounded(int minutes, int increment, bool valid)
    {
        Assert.Equal(valid, TimeControl.TryCreate(minutes, increment, out _));
    }

    [Theory]
    [InlineData(null, "Anonymous")]
    [InlineData("   ", "Anonymous")]
    [InlineData("  Magnus   Carlsen ", "Magnus Carlsen")]
    [InlineData("Bad\u0007Name​", "BadName")]
    [InlineData("A very very long player name indeed", "A very very long player")]
    public void PlayerNames_AreSanitized(string? input, string expected)
    {
        var names = new PlayerNames(Options.Create(new GameOptions { MaxPlayerNameLength = 24 }));

        Assert.Equal(expected, names.Sanitize(input));
    }
}
