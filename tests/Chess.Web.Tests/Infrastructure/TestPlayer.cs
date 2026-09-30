using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Chess.Web.Contracts;
using Chess.Web.Domain;
using Chess.Web.Hubs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Chess.Web.Tests.Infrastructure;

/// <summary>A real SignalR client connected to the in-memory server, recording everything it receives.</summary>
public sealed class TestPlayer : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private readonly Channel<GameStateDto> _states = Channel.CreateUnbounded<GameStateDto>();
    private readonly Channel<GameNotificationDto> _notifications = Channel.CreateUnbounded<GameNotificationDto>();

    private TestPlayer(HubConnection connection)
    {
        _connection = connection;
        _connection.On<GameStateDto>(nameof(IGameClient.GameState), state =>
        {
            Latest = state;
            _states.Writer.TryWrite(state);
        });
        _connection.On<GameNotificationDto>(nameof(IGameClient.Notification), n => _notifications.Writer.TryWrite(n));
    }

    public GameStateDto? Latest { get; private set; }

    public ParticipantRole? Role { get; private set; }

    public string? SeatToken { get; private set; }

    public static async Task<TestPlayer> ConnectAsync(ChessAppFactory factory)
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, GameHub.Route), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
            .Build();

        await connection.StartAsync();
        return new TestPlayer(connection);
    }

    /// <summary>Creates a game over HTTP (as the home page does) and returns the creator's code and seat token.</summary>
    public static async Task<CreateGameResponse> CreateGameAsync(
        ChessAppFactory factory, ColorPreference color = ColorPreference.White, int? minutes = null, int increment = 0, string name = "Creator")
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/games", new CreateGameRequest(name, minutes, increment, color), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateGameResponse>(Json))!;
    }

    public async Task<JoinGameResponse> JoinAsync(string code, string? seatToken = null, IReadOnlyList<string>? knownTokens = null, string name = "Player")
    {
        var response = await _connection.InvokeAsync<JoinGameResponse>(
            nameof(GameHub.JoinGame), new JoinGameRequest(code, seatToken, knownTokens, name));
        Role = response.Role;
        SeatToken = response.SeatToken ?? SeatToken;
        if (response.State is { } state)
        {
            Latest = state;
        }

        return response;
    }

    public Task<HubResult> MoveAsync(string uci) => InvokeAsync(nameof(GameHub.MakeMove), uci);

    public Task<HubResult> InvokeAsync(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<HubResult>(method, args);

    /// <summary>Waits until a broadcast state satisfies the predicate.</summary>
    public async Task<GameStateDto> WaitForStateAsync(Func<GameStateDto, bool> predicate, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        if (Latest is { } latest && predicate(latest))
        {
            return latest;
        }

        while (true)
        {
            var state = await _states.Reader.ReadAsync(cts.Token);
            if (predicate(state))
            {
                return state;
            }
        }
    }

    public async Task<GameNotificationDto> WaitForNotificationAsync(GameEventType type, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        while (true)
        {
            var notification = await _notifications.Reader.ReadAsync(cts.Token);
            if (notification.Type == type)
            {
                return notification;
            }
        }
    }

    /// <summary>Drops the connection, as a closed tab or lost network would.</summary>
    public Task DisconnectAsync() => _connection.StopAsync();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
