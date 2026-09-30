/* global signalR */

// Delays between automatic reconnect attempts; after these, keep trying every 10 seconds for a while.
const RETRY_DELAYS_MS = [0, 1000, 2000, 4000, 8000];
const GIVE_UP_AFTER_MS = 10 * 60 * 1000;

/**
 * Thin wrapper around the SignalR hub connection for a game.
 * Commands resolve to the server's { success, error, message } result and never throw.
 */
export class GameConnection {
  #hub;

  /**
   * @param {{ onState?: Function, onNotification?: Function, onReconnecting?: Function,
   *           onReconnected?: Function, onClosed?: Function }} handlers
   */
  constructor(handlers) {
    this.#hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/game')
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount, elapsedMilliseconds }) =>
          elapsedMilliseconds > GIVE_UP_AFTER_MS ? null : RETRY_DELAYS_MS[previousRetryCount] ?? 10_000,
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.#hub.on('GameState', (state) => handlers.onState?.(state));
    this.#hub.on('Notification', (notification) => handlers.onNotification?.(notification));
    this.#hub.onreconnecting(() => handlers.onReconnecting?.());
    this.#hub.onreconnected(() => handlers.onReconnected?.());
    this.#hub.onclose(() => handlers.onClosed?.());
  }

  get isConnected() {
    return this.#hub.state === signalR.HubConnectionState.Connected;
  }

  get isDisconnected() {
    return this.#hub.state === signalR.HubConnectionState.Disconnected;
  }

  start() {
    return this.#hub.start();
  }

  async invoke(method, ...args) {
    try {
      return await this.#hub.invoke(method, ...args);
    } catch (error) {
      console.warn(`Hub call ${method} failed`, error);
      return { success: false, error: 'ConnectionError', message: 'Connection problem. Please try again.' };
    }
  }
}
