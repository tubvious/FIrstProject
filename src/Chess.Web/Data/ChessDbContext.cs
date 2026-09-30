using Chess.Web.Data.Entities;
using Chess.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chess.Web.Data;

public sealed class ChessDbContext(DbContextOptions<ChessDbContext> options) : DbContext(options)
{
    public DbSet<GameEntity> Games => Set<GameEntity>();

    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();

    public DbSet<MoveEntity> Moves => Set<MoveEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameEntity>(game =>
        {
            game.ToTable("Games");
            game.HasKey(g => g.Code);
            game.Property(g => g.Code).HasMaxLength(GameCode.Length);
            game.Property(g => g.InitialFen).HasMaxLength(100);
            game.Property(g => g.CurrentFen).HasMaxLength(100);
            game.Property(g => g.RematchOfCode).HasMaxLength(GameCode.Length);
            game.Property(g => g.RematchCode).HasMaxLength(GameCode.Length);

            // Enums are stored as text so the database stays readable.
            game.Property(g => g.Status).HasConversion<string>().HasMaxLength(32);
            game.Property(g => g.ColorPreference).HasConversion<string>().HasMaxLength(16);
            game.Property(g => g.Result).HasConversion<string>().HasMaxLength(16);
            game.Property(g => g.EndReason).HasConversion<string>().HasMaxLength(32);

            game.HasMany(g => g.Players).WithOne().HasForeignKey(p => p.GameCode).OnDelete(DeleteBehavior.Cascade);
            game.HasMany(g => g.Moves).WithOne().HasForeignKey(m => m.GameCode).OnDelete(DeleteBehavior.Cascade);
            game.HasIndex(g => g.Status);
        });

        modelBuilder.Entity<PlayerEntity>(player =>
        {
            player.ToTable("Players");
            player.Property(p => p.Color).HasConversion<string>().HasMaxLength(8);
            player.Property(p => p.DisplayName).HasMaxLength(64);
            player.Property(p => p.SeatTokenHash).HasMaxLength(64);
            player.HasIndex(p => new { p.GameCode, p.Color }).IsUnique();
        });

        modelBuilder.Entity<MoveEntity>(move =>
        {
            move.ToTable("Moves");
            move.Property(m => m.Uci).HasMaxLength(5);
            move.Property(m => m.San).HasMaxLength(16);
            move.Property(m => m.FenAfter).HasMaxLength(100);
            move.HasIndex(m => new { m.GameCode, m.Ply }).IsUnique();
        });
    }
}

/// <summary>Used by the EF Core tools (dotnet ef migrations add ...) without starting the web host.</summary>
internal sealed class ChessDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ChessDbContext>
{
    public ChessDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ChessDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
