using Chess.Engine;
using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Services;
using Chess.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Chess.Web.Tests;

/// <summary>Playing against the computer, through the real server and SignalR.</summary>
public sealed class ComputerOpponentTests : IAsyncLifetime
{
    private readonly ChessAppFactory _factory = new();
    private readonly List<TestPlayer> _players = [];

    public Task InitializeAsync()
    {
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var player in _players)
        {
            await player.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    private async Task<(string Code, TestPlayer Human)> StartAsync(ColorPreference color, int level = 2)
    {
        var created = await TestPlayer.CreateGameAsync(_factory, color, botLevel: level, name: "Human");
        var human = await TestPlayer.ConnectAsync(_factory);
        _players.Add(human);
        var join = await human.JoinAsync(created.Code, created.SeatToken);
        Assert.True(join.Success);
        return (created.Code, human);
    }

    [Fact]
    public async Task GameAgainstTheComputer_StartsImmediately_AndTheComputerReplies()
    {
        var (_, human) = await StartAsync(ColorPreference.White, level: 3);
        Assert.Equal(GameStatus.InProgress, human.Latest!.Status);
        Assert.True(human.Latest.Black!.IsBot);
        Assert.True(human.Latest.Black.Connected);
        Assert.Equal("Computer (Medium)", human.Latest.Black.Name);

        for (var ply = 0; ply < 3; ply++)
        {
            var state = human.Latest!;
            Assert.True((await human.MoveAsync(state.LegalMoves[0])).Success);
            await human.WaitForStateAsync(s => s.Moves.Count == state.Moves.Count + 2);
        }

        Assert.Equal(PieceColor.White, human.Latest!.Turn);
    }

    [Fact]
    public async Task ComputerMovesFirst_WhenTheHumanPlaysBlack()
    {
        var (_, human) = await StartAsync(ColorPreference.Black);

        var state = await human.WaitForStateAsync(s => s.Moves.Count == 1);

        Assert.Equal(PieceColor.Black, state.Turn);
        Assert.True(state.White!.IsBot);
    }

    [Fact]
    public async Task HumanCannotMoveForTheComputer_OrOfferItADraw()
    {
        var (_, human) = await StartAsync(ColorPreference.White);
        // Moving one of the computer's pieces is simply not a legal move for White.
        Assert.Equal(nameof(GameError.IllegalMove), (await human.MoveAsync("e7e5")).Error);

        Assert.Equal(nameof(GameError.NotAvailableAgainstComputer), (await human.InvokeAsync("OfferDraw")).Error);
    }

    [Fact]
    public async Task Rematch_IsAcceptedByTheComputer_WithColoursSwapped()
    {
        var (_, human) = await StartAsync(ColorPreference.White, level: 1);
        await human.MoveAsync("e2e4");
        await human.WaitForStateAsync(s => s.Moves.Count == 2);
        await human.InvokeAsync("Resign");

        Assert.True((await human.InvokeAsync("RequestRematch")).Success);
        var finished = await human.WaitForStateAsync(s => s.RematchCode is not null);
        var next = await human.JoinAsync(finished.RematchCode!, human.SeatToken);

        Assert.Equal(ParticipantRole.Black, next.Role);
        Assert.True(next.State!.White!.IsBot);
        Assert.Single((await human.WaitForStateAsync(s => s.Code == finished.RematchCode && s.Moves.Count == 1)).Moves);
    }

    [Fact]
    public async Task ComputerGame_SurvivesBeingReloadedFromTheDatabase()
    {
        var (code, human) = await StartAsync(ColorPreference.White);
        await human.MoveAsync("d2d4");
        await human.WaitForStateAsync(s => s.Moves.Count == 2);
        var token = human.SeatToken!;
        await human.DisconnectAsync();

        var registry = _factory.Services.GetRequiredService<GameRegistry>();
        _factory.Time.Advance(TimeSpan.FromHours(1));
        registry.EvictIdle(TimeSpan.FromMinutes(30));
        Assert.False(registry.IsLoaded(code));

        var returning = await TestPlayer.ConnectAsync(_factory);
        _players.Add(returning);
        var join = await returning.JoinAsync(code, token);
        Assert.True(join.State!.Black!.IsBot);
        Assert.True((await returning.MoveAsync(join.State.LegalMoves[0])).Success);
        await returning.WaitForStateAsync(s => s.Moves.Count == 4);
    }

    [Theory]
    [InlineData("{\"color\":\"white\",\"opponent\":\"computer\"}")]
    [InlineData("{\"color\":\"white\",\"opponent\":\"computer\",\"botLevel\":0}")]
    [InlineData("{\"color\":\"white\",\"opponent\":\"computer\",\"botLevel\":6}")]
    public async Task CreatingAComputerGame_RequiresAValidLevel(string body)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/games", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ComputerGame_HasNoFreeSeat_ForVisitors()
    {
        var (code, _) = await StartAsync(ColorPreference.White);
        using var client = _factory.CreateClient();

        var summary = await client.GetFromJsonAsync<GameSummaryDto>($"/api/games/{code}", TestPlayer.Json);
        var visitor = await TestPlayer.ConnectAsync(_factory);
        _players.Add(visitor);
        var join = await visitor.JoinAsync(code);

        Assert.False(summary!.HasOpenSeat);
        Assert.Equal(ParticipantRole.Spectator, join.Role);
    }
}
