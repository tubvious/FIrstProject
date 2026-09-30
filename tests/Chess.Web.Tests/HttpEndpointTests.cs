using System.Net;
using System.Net.Http.Json;
using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Chess.Web.Tests;

public sealed class HttpEndpointTests : IClassFixture<ChessAppFactory>
{
    private readonly ChessAppFactory _factory;

    public HttpEndpointTests(ChessAppFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateGame_ReturnsShareableCodeAndSeatToken()
    {
        var created = await TestPlayer.CreateGameAsync(_factory, ColorPreference.Black, minutes: 5, increment: 3, name: "Alice");

        Assert.True(GameCode.TryNormalize(created.Code, out _));
        Assert.True(SeatToken.IsWellFormed(created.SeatToken));
        Assert.Equal(Engine.PieceColor.Black, created.Color);
        Assert.Equal($"/game/{created.Code}", created.Url);

        using var client = _factory.CreateClient();
        var summary = await client.GetFromJsonAsync<GameSummaryDto>($"/api/games/{created.Code}", TestPlayer.Json);
        Assert.NotNull(summary);
        Assert.Equal(GameStatus.WaitingForOpponent, summary.Status);
        Assert.Equal("5+3", summary.TimeControl);
        Assert.Equal("Alice", summary.BlackName);
        Assert.True(summary.HasOpenSeat);
    }

    [Theory]
    [InlineData("{\"playerName\":\"x\",\"minutes\":0,\"color\":\"white\"}")]
    [InlineData("{\"playerName\":\"x\",\"minutes\":999,\"color\":\"white\"}")]
    [InlineData("{\"playerName\":\"x\",\"minutes\":5,\"incrementSeconds\":120,\"color\":\"white\"}")]
    [InlineData("{\"playerName\":\"x\",\"color\":\"purple\"}")]
    public async Task CreateGame_RejectsInvalidSettings(string body)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/games", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/games/ZZZZZZZZ")]
    [InlineData("/api/games/not-a-code")]
    public async Task GetGame_UnknownCode_IsNotFound(string url)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pages_Render_AndGamePageUsesCanonicalCode()
    {
        var created = await TestPlayer.CreateGameAsync(_factory);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Play chess with your friends.", await home.Content.ReadAsStringAsync());

        var game = await client.GetAsync($"/game/{created.Code}");
        Assert.Equal(HttpStatusCode.OK, game.StatusCode);
        Assert.Contains($"data-game-code=\"{created.Code}\"", await game.Content.ReadAsStringAsync());

        var lowercase = await client.GetAsync($"/game/{created.Code.ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.Redirect, lowercase.StatusCode);
        Assert.Equal($"/game/{created.Code}", lowercase.Headers.Location?.OriginalString);

        var missing = await client.GetAsync("/game/ZZZZZZZZ");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("Game not found", await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GamePage_WarnsWhenTheShareLinkOnlyWorksOnThisComputer()
    {
        var created = await TestPlayer.CreateGameAsync(_factory);
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync($"/game/{created.Code}");

        Assert.Contains("data-share-local-only=\"true\"", html);
        Assert.Contains("This link only works on this computer", html);
    }

    [Fact]
    public async Task GamePage_UsesTheConfiguredPublicAddressForShareLinks()
    {
        using var configured = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Chess:PublicBaseUrl", "https://chess.example.com/"));
        using var client = configured.CreateClient();
        var created = await TestPlayer.CreateGameAsync(_factory);

        var html = await client.GetStringAsync($"/game/{created.Code}");

        Assert.Contains($"value=\"https://chess.example.com/game/{created.Code}\"", html);
        Assert.Contains("data-share-local-only=\"false\"", html);
        Assert.DoesNotContain("This link only works on this computer", html);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
