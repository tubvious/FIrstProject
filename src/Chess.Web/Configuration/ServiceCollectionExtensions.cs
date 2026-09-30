using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Chess.Web.Data;
using Chess.Web.Endpoints;
using Chess.Web.Hubs;
using Chess.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Chess.Web.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChessServices(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOptions<GameOptions>()
            .Bind(configuration.GetSection(GameOptions.SectionName))
            .Validate(options => options.IsValid(), "The 'Chess' configuration section contains invalid values.")
            .ValidateOnStart();

        services.AddDbContextFactory<ChessDbContext>(options =>
            options.UseSqlite(ResolveConnectionString(configuration, environment)));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGameRepository, GameRepository>();
        services.AddSingleton<GameRegistry>();
        services.AddSingleton<PlayerNames>();
        services.AddSingleton<IGameNotifier, SignalRGameNotifier>();
        services.AddSingleton<IGameService, GameService>();
        services.AddHostedService<ClockWatchdogService>();
        services.AddHostedService<GameEvictionService>();

        var enumConverter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(enumConverter));
        services.AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(enumConverter));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(GameEndpoints.CreateGameRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue(
                            $"{GameOptions.SectionName}:{nameof(GameOptions.GameCreationPerMinuteLimit)}", 20),
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });

        return services;
    }

    /// <summary>Applies pending EF Core migrations, creating the SQLite database on first run.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var factory = app.Services.GetRequiredService<IDbContextFactory<ChessDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    /// <summary>Relative SQLite paths are resolved against the content root so the app works from any working directory.</summary>
    private static string ResolveConnectionString(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var builder = new SqliteConnectionStringBuilder(
            configuration.GetConnectionString("Chess") ?? "Data Source=App_Data/chess.db");

        var isInMemory = builder.Mode == SqliteOpenMode.Memory || builder.DataSource == ":memory:";
        if (!isInMemory && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(environment.ContentRootPath, builder.DataSource);
        }

        if (!isInMemory && Path.GetDirectoryName(builder.DataSource) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }
}
