using Chess.Web.Contracts;
using Chess.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace Chess.Web.Hubs;

public sealed class SignalRGameNotifier(IHubContext<GameHub, IGameClient> hubContext) : IGameNotifier
{
    public Task PublishStateAsync(GameStateDto state) =>
        hubContext.Clients.Group(GameHub.GroupName(state.Code)).GameState(state);

    public Task PublishNotificationAsync(string gameCode, GameNotificationDto notification) =>
        hubContext.Clients.Group(GameHub.GroupName(gameCode)).Notification(notification);
}
