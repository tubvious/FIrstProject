using Chess.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Chess.Web.Services;

/// <summary>Flags players whose clock runs out, even if nobody sends a move.</summary>
public sealed class ClockWatchdogService(
    IGameService games,
    IOptions<GameOptions> options,
    TimeProvider timeProvider,
    ILogger<ClockWatchdogService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ClockCheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await games.EnforceTimeControlsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.ClockCheckFailed(ex);
            }
        }
    }
}

/// <summary>Periodically unloads idle games from memory; they are reloaded from the database on demand.</summary>
public sealed class GameEvictionService(
    GameRegistry registry,
    IOptions<GameOptions> options,
    TimeProvider timeProvider,
    ILogger<GameEvictionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.EvictionSweepInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var evicted = registry.EvictIdle(options.Value.IdleGameEvictionAfter);
            if (evicted > 0)
            {
                logger.GamesEvicted(evicted);
            }
        }
    }
}
