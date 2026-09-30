using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace Chess.Web.Hubs;

/// <summary>Messages the server pushes to clients.</summary>
public interface IGameClient
{
    Task GameState(GameStateDto state);

    Task Notification(GameNotificationDto notification);
}

/// <summary>
/// Real-time channel for a game. A connection joins exactly one game at a time; every command acts on
/// that game and on the seat bound to this connection, so a client can never act for someone else.
/// </summary>
public sealed class GameHub(IGameService games) : Hub<IGameClient>
{
    public const string Route = "/hubs/game";

    private const string GameCodeKey = "game-code";

    public static string GroupName(string code) => $"game:{code}";

    public async Task<JoinGameResponse> JoinGame(JoinGameRequest request)
    {
        if (!GameCode.TryNormalize(request?.Code, out var code))
        {
            return JoinGameResponse.Failed(GameError.GameNotFound);
        }

        // Re-joining the same game (e.g. a retry) is handled by the session; only switching games leaves the old one.
        if (CurrentGameCode != code)
        {
            await LeaveCurrentGameAsync();
        }

        // Join the group first so no broadcast issued during the join is missed.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(code));
        var response = await games.JoinGameAsync(code, Context.ConnectionId, request!);
        if (response.Success)
        {
            Context.Items[GameCodeKey] = code;
        }
        else
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(code));
        }

        return response;
    }

    public Task<HubResult> MakeMove(string uci) =>
        InCurrentGame(code => games.MakeMoveAsync(code, Context.ConnectionId, uci ?? string.Empty));

    public Task<HubResult> Resign() => InCurrentGame(code => games.ResignAsync(code, Context.ConnectionId));

    public Task<HubResult> OfferDraw() => InCurrentGame(code => games.OfferDrawAsync(code, Context.ConnectionId));

    public Task<HubResult> RespondToDrawOffer(bool accept) =>
        InCurrentGame(code => games.RespondToDrawOfferAsync(code, Context.ConnectionId, accept));

    public Task<HubResult> RequestRematch() => InCurrentGame(code => games.RequestRematchAsync(code, Context.ConnectionId));

    public Task<HubResult> DeclineRematch() => InCurrentGame(code => games.DeclineRematchAsync(code, Context.ConnectionId));

    public Task<HubResult> ClaimAbandonedGame(bool scoreAsDraw) =>
        InCurrentGame(code => games.ClaimAbandonedGameAsync(code, Context.ConnectionId, scoreAsDraw));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentGameAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private string? CurrentGameCode => Context.Items.TryGetValue(GameCodeKey, out var value) ? value as string : null;

    private Task<HubResult> InCurrentGame(Func<string, Task<HubResult>> command) =>
        CurrentGameCode is { } code
            ? command(code)
            : Task.FromResult(new HubResult(false, nameof(GameError.NotInGame), GameErrorMessages.For(GameError.NotInGame)));

    private async Task LeaveCurrentGameAsync()
    {
        if (CurrentGameCode is not { } code)
        {
            return;
        }

        Context.Items.Remove(GameCodeKey);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(code));
        await games.LeaveGameAsync(code, Context.ConnectionId);
    }
}
