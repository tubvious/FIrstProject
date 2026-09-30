using Chess.Web.Contracts;

namespace Chess.Web.Services;

/// <summary>Pushes game updates to everyone watching a game. Decouples game logic from SignalR.</summary>
public interface IGameNotifier
{
    Task PublishStateAsync(GameStateDto state);

    Task PublishNotificationAsync(string gameCode, GameNotificationDto notification);
}
