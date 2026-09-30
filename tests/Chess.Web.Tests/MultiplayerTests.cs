using Chess.Engine;
using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Services;
using Chess.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests;

/// <summary>End-to-end tests over real SignalR connections against the in-memory server.</summary>
public sealed class MultiplayerTests : IAsyncLifetime
{
    private readonly ChessAppFactory _factory = new();
    private readonly List<TestPlayer> _players = [];

    public Task InitializeAsync()
    {
        _ = _factory.Server; // start the host
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

    private async Task<TestPlayer> ConnectAsync()
    {
        var player = await TestPlayer.ConnectAsync(_factory);
        _players.Add(player);
        return player;
    }

    /// <summary>Creates a game and seats White (the creator) and Black (the first visitor).</summary>
    private async Task<(string Code, TestPlayer White, TestPlayer Black)> StartGameAsync(int? minutes = null)
    {
        var created = await TestPlayer.CreateGameAsync(_factory, ColorPreference.White, minutes, name: "Alice");
        var white = await ConnectAsync();
        await white.JoinAsync(created.Code, created.SeatToken);
        var black = await ConnectAsync();
        await black.JoinAsync(created.Code, name: "Bob");
        await white.WaitForStateAsync(s => s.Status == GameStatus.InProgress);
        return (created.Code, white, black);
    }

    [Fact]
    public async Task CreatorAndFriend_AreSeated_AndTheGameStartsAutomatically()
    {
        var created = await TestPlayer.CreateGameAsync(_factory, ColorPreference.White, name: "Alice");
        var white = await ConnectAsync();
        var whiteJoin = await white.JoinAsync(created.Code, created.SeatToken);
        Assert.Equal(ParticipantRole.White, whiteJoin.Role);
        Assert.Equal(GameStatus.WaitingForOpponent, whiteJoin.State!.Status);

        var black = await ConnectAsync();
        var blackJoin = await black.JoinAsync(created.Code, name: "Bob");

        Assert.Equal(ParticipantRole.Black, blackJoin.Role);
        Assert.True(SeatToken.IsWellFormed(blackJoin.SeatToken));
        var seen = await white.WaitForStateAsync(s => s.Status == GameStatus.InProgress);
        Assert.Equal("Bob", seen.Black!.Name);
        Assert.Equal(GameEventType.PlayerJoined, (await white.WaitForNotificationAsync(GameEventType.PlayerJoined)).Type);
        Assert.Equal(20, seen.LegalMoves.Count);
    }

    [Fact]
    public async Task Moves_AreBroadcastToBothPlayersInRealTime()
    {
        var (_, white, black) = await StartGameAsync();

        Assert.True((await white.MoveAsync("e2e4")).Success);

        var blackView = await black.WaitForStateAsync(s => s.Moves.Count == 1);
        Assert.Equal("e4", blackView.Moves[0].San);
        Assert.Equal(PieceColor.Black, blackView.Turn);
        Assert.Contains("e7e5", blackView.LegalMoves);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", blackView.Fen);

        Assert.True((await black.MoveAsync("e7e5")).Success);
        var whiteView = await white.WaitForStateAsync(s => s.Moves.Count == 2);
        Assert.Equal(PieceColor.White, whiteView.Turn);
    }

    [Fact]
    public async Task Server_RejectsOutOfTurnIllegalAndMalformedMoves()
    {
        var (_, white, black) = await StartGameAsync();

        Assert.Equal(nameof(GameError.NotYourTurn), (await black.MoveAsync("e7e5")).Error);
        Assert.Equal(nameof(GameError.IllegalMove), (await white.MoveAsync("e2e5")).Error);
        Assert.Equal(nameof(GameError.IllegalMove), (await white.MoveAsync("e7e5")).Error); // opponent's piece
        Assert.Equal(nameof(GameError.InvalidMove), (await white.MoveAsync("hello")).Error);
        Assert.Empty(white.Latest!.Moves);
    }

    [Fact]
    public async Task ThirdVisitor_Spectates_AndCannotMoveOrTakeASeat()
    {
        var (code, white, _) = await StartGameAsync();
        var intruder = await ConnectAsync();

        var join = await intruder.JoinAsync(code, seatToken: new string('A', 64), knownTokens: [new string('B', 64)], name: "Mallory");

        Assert.Equal(ParticipantRole.Spectator, join.Role);
        Assert.Null(join.SeatToken);
        Assert.Equal(nameof(GameError.NotAPlayer), (await intruder.MoveAsync("e2e4")).Error);
        Assert.Equal(nameof(GameError.NotAPlayer), (await intruder.InvokeAsync("Resign")).Error);
        await white.WaitForStateAsync(s => s.SpectatorCount == 1);

        Assert.True((await white.MoveAsync("e2e4")).Success);
        await intruder.WaitForStateAsync(s => s.Moves.Count == 1);
    }

    [Fact]
    public async Task CommandsWithoutJoiningAGame_AreRejected()
    {
        var stranger = await ConnectAsync();

        Assert.Equal(nameof(GameError.NotInGame), (await stranger.MoveAsync("e2e4")).Error);
        Assert.False((await stranger.JoinAsync("ZZZZZZZZ")).Success);
    }

    [Fact]
    public async Task DisconnectedPlayer_CanReconnectWithTheirToken_AndResumeTheSeat()
    {
        var (code, white, black) = await StartGameAsync();
        await white.MoveAsync("d2d4");
        var token = black.SeatToken!;

        await black.DisconnectAsync();
        await white.WaitForNotificationAsync(GameEventType.PlayerDisconnected);
        var whileAway = await white.WaitForStateAsync(s => s.Black is { Connected: false });
        Assert.NotNull(whileAway.Black!.DisconnectedForMs);

        var returning = await ConnectAsync();
        var join = await returning.JoinAsync(code, seatToken: token);

        Assert.Equal(ParticipantRole.Black, join.Role);
        Assert.Single(join.State!.Moves);
        await white.WaitForNotificationAsync(GameEventType.PlayerReconnected);
        Assert.True((await returning.MoveAsync("d7d5")).Success);
    }

    [Fact]
    public async Task ClockRunsOnTheServer_AndFlagFallEndsTheGame()
    {
        var (_, white, black) = await StartGameAsync(minutes: 1);
        await white.MoveAsync("e2e4");
        await black.MoveAsync("e7e5");
        var running = await white.WaitForStateAsync(s => s.Moves.Count == 2);
        Assert.Equal(PieceColor.White, running.Clock!.Running);

        _factory.Time.Advance(TimeSpan.FromSeconds(61));
        await _factory.Services.GetRequiredService<IGameService>().EnforceTimeControlsAsync();

        var final = await black.WaitForStateAsync(s => s.Status == GameStatus.Finished);
        Assert.Equal(GameEndReason.Timeout, final.Outcome!.Reason);
        Assert.Equal(PieceColor.Black, final.Outcome.Winner);
        Assert.Equal(0, final.Clock!.WhiteMs);
        Assert.Equal(nameof(GameError.GameFinished), (await white.MoveAsync("g1f3")).Error);
    }

    [Fact]
    public async Task DrawOffer_IsDeliveredToTheOpponent_AndCanBeAccepted()
    {
        var (_, white, black) = await StartGameAsync();

        Assert.True((await white.InvokeAsync("OfferDraw")).Success);
        var offer = await black.WaitForNotificationAsync(GameEventType.DrawOffered);
        Assert.Equal(PieceColor.White, offer.Color);

        Assert.True((await black.InvokeAsync("RespondToDrawOffer", true)).Success);
        var final = await white.WaitForStateAsync(s => s.Status == GameStatus.Finished);
        Assert.Equal(GameOutcome.Draw(GameEndReason.Agreement).Result, final.Outcome!.Result);
    }

    [Fact]
    public async Task Rematch_CreatesANewGame_WithColoursSwapped()
    {
        var (_, white, black) = await StartGameAsync();
        await white.MoveAsync("e2e4");
        await black.MoveAsync("e7e5");
        await white.InvokeAsync("Resign");

        Assert.True((await white.InvokeAsync("RequestRematch")).Success);
        await black.WaitForNotificationAsync(GameEventType.RematchRequested);
        Assert.True((await black.InvokeAsync("RequestRematch")).Success);
        var finished = await white.WaitForStateAsync(s => s.RematchCode is not null);

        var nextWhite = await black.JoinAsync(finished.RematchCode!, black.SeatToken);
        var nextBlack = await white.JoinAsync(finished.RematchCode!, white.SeatToken);

        Assert.Equal(ParticipantRole.White, nextWhite.Role);
        Assert.Equal(ParticipantRole.Black, nextBlack.Role);
        Assert.Equal(GameStatus.InProgress, nextBlack.State!.Status);
        Assert.Equal("Bob", nextBlack.State.White!.Name);
    }

    [Fact]
    public async Task Games_AreIsolatedFromEachOther()
    {
        var (_, white1, black1) = await StartGameAsync();
        var (_, white2, black2) = await StartGameAsync();

        await white1.MoveAsync("e2e4");
        await white2.MoveAsync("d2d4");

        Assert.Equal("e4", (await black1.WaitForStateAsync(s => s.Moves.Count == 1)).Moves[0].San);
        Assert.Equal("d4", (await black2.WaitForStateAsync(s => s.Moves.Count == 1)).Moves[0].San);
        Assert.Equal(nameof(GameError.NotYourTurn), (await white2.MoveAsync("e2e4")).Error);
    }

    [Fact]
    public async Task GamesSurviveBeingUnloadedFromMemory_ByReloadingFromTheDatabase()
    {
        var (code, white, black) = await StartGameAsync();
        await white.MoveAsync("e2e4");
        await black.MoveAsync("c7c5");
        await white.WaitForStateAsync(s => s.Moves.Count == 2);
        var whiteToken = white.SeatToken!;
        await white.DisconnectAsync();
        await black.DisconnectAsync();

        var registry = _factory.Services.GetRequiredService<GameRegistry>();
        _factory.Time.Advance(TimeSpan.FromHours(1));
        await WaitUntilAsync(() => registry.EvictIdle(TimeSpan.FromMinutes(30)) >= 0 && !registry.IsLoaded(code));

        var returning = await ConnectAsync();
        var join = await returning.JoinAsync(code, whiteToken);

        Assert.Equal(ParticipantRole.White, join.Role);
        Assert.Equal(["e4", "c5"], join.State!.Moves.Select(m => m.San));
        Assert.True((await returning.MoveAsync("g1f3")).Success);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, cts.Token);
        }
    }
}
