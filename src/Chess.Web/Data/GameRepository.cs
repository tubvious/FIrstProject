using Chess.Engine;
using Chess.Web.Data.Entities;
using Chess.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chess.Web.Data;

public interface IGameRepository
{
    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<RestoredGame?> LoadAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Upserts the game row, any newly seated players and any moves not yet stored.</summary>
    Task SaveAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class GameRepository(IDbContextFactory<ChessDbContext> contextFactory) : IGameRepository
{
    public async Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Games.AnyAsync(g => g.Code == code, cancellationToken);
    }

    public async Task<RestoredGame?> LoadAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await db.Games
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Players)
            .Include(g => g.Moves.OrderBy(m => m.Ply))
            .SingleOrDefaultAsync(g => g.Code == code, cancellationToken);

        return game is null ? null : ToRestoredGame(game);
    }

    public async Task SaveAsync(GameSession session, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await db.Games
            .Include(g => g.Players)
            .SingleOrDefaultAsync(g => g.Code == session.Code, cancellationToken);

        if (game is null)
        {
            game = new GameEntity
            {
                Code = session.Code,
                InitialFen = session.Game.InitialPosition.ToFen(),
                CurrentFen = session.Game.CurrentPosition.ToFen(),
                CreatedAt = session.CreatedAt.UtcDateTime,
            };
            db.Games.Add(game);
        }

        ApplyGameState(game, session, now);
        AddNewPlayers(game, session, now);
        await AddNewMovesAsync(db, session, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyGameState(GameEntity game, GameSession session, DateTimeOffset now)
    {
        game.Status = session.Status;
        game.CurrentFen = session.Game.CurrentPosition.ToFen();
        game.InitialTimeSeconds = session.TimeControl is { } timeControl ? (int)timeControl.Initial.TotalSeconds : null;
        game.IncrementSeconds = (int)(session.TimeControl?.Increment.TotalSeconds ?? 0);
        game.ColorPreference = session.ColorPreference;
        game.WhiteTimeRemainingMs = RemainingMs(session.Clock, PieceColor.White, now);
        game.BlackTimeRemainingMs = RemainingMs(session.Clock, PieceColor.Black, now);
        game.Result = session.Game.Outcome?.Result;
        game.EndReason = session.Game.Outcome?.Reason;
        game.RematchOfCode = session.RematchOfCode;
        game.RematchCode = session.RematchCode;
        game.UpdatedAt = now.UtcDateTime;
        game.StartedAt = session.StartedAt?.UtcDateTime;
        game.FinishedAt = session.FinishedAt?.UtcDateTime;
    }

    private static void AddNewPlayers(GameEntity game, GameSession session, DateTimeOffset now)
    {
        foreach (var color in (ReadOnlySpan<PieceColor>)[PieceColor.White, PieceColor.Black])
        {
            var seat = session.Seat(color);
            if (seat.IsOccupied && game.Players.All(p => p.Color != color))
            {
                game.Players.Add(new PlayerEntity
                {
                    GameCode = session.Code,
                    Color = color,
                    DisplayName = seat.Name!,
                    SeatTokenHash = seat.TokenHash!,
                    JoinedAt = now.UtcDateTime,
                });
            }
        }
    }

    private static async Task AddNewMovesAsync(ChessDbContext db, GameSession session, CancellationToken cancellationToken)
    {
        var storedPlies = await db.Moves.CountAsync(m => m.GameCode == session.Code, cancellationToken);
        foreach (var logged in session.MoveLog.Skip(storedPlies))
        {
            db.Moves.Add(new MoveEntity
            {
                GameCode = session.Code,
                Ply = logged.Move.Ply,
                Uci = logged.Move.Uci,
                San = logged.Move.San,
                FenAfter = logged.Move.PositionAfter.ToFen(),
                WhiteTimeRemainingMs = (long?)logged.WhiteRemaining?.TotalMilliseconds,
                BlackTimeRemainingMs = (long?)logged.BlackRemaining?.TotalMilliseconds,
                PlayedAt = logged.PlayedAt.UtcDateTime,
            });
        }
    }

    private static RestoredGame ToRestoredGame(GameEntity game)
    {
        TimeControl? timeControl = game.InitialTimeSeconds is { } seconds
            ? new TimeControl(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(game.IncrementSeconds))
            : null;
        GameOutcome? outcome = game.Result is { } result && game.EndReason is { } reason ? new GameOutcome(result, reason) : null;

        return new RestoredGame(
            game.Code,
            game.Status,
            game.InitialFen,
            timeControl,
            game.ColorPreference,
            FromMs(game.WhiteTimeRemainingMs),
            FromMs(game.BlackTimeRemainingMs),
            outcome,
            game.RematchOfCode,
            game.RematchCode,
            AsUtc(game.CreatedAt),
            game.StartedAt is { } started ? AsUtc(started) : null,
            game.FinishedAt is { } finished ? AsUtc(finished) : null,
            game.Players.Select(p => new RestoredPlayer(p.Color, p.DisplayName, p.SeatTokenHash)).ToList(),
            game.Moves.Select(m => new RestoredMove(
                m.Ply, m.Uci, AsUtc(m.PlayedAt), FromMs(m.WhiteTimeRemainingMs), FromMs(m.BlackTimeRemainingMs))).ToList());
    }

    private static long? RemainingMs(ChessClock? clock, PieceColor color, DateTimeOffset now) =>
        clock is null ? null : (long)clock.GetRemaining(color, now).TotalMilliseconds;

    private static TimeSpan? FromMs(long? milliseconds) => milliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
