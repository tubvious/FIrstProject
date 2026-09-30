using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chess.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InitialFen = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CurrentFen = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InitialTimeSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    IncrementSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorPreference = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    WhiteTimeRemainingMs = table.Column<long>(type: "INTEGER", nullable: true),
                    BlackTimeRemainingMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    RematchOfCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RematchCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Moves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameCode = table.Column<string>(type: "TEXT", nullable: false),
                    Ply = table.Column<int>(type: "INTEGER", nullable: false),
                    Uci = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    San = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FenAfter = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    WhiteTimeRemainingMs = table.Column<long>(type: "INTEGER", nullable: true),
                    BlackTimeRemainingMs = table.Column<long>(type: "INTEGER", nullable: true),
                    PlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Moves_Games_GameCode",
                        column: x => x.GameCode,
                        principalTable: "Games",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameCode = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SeatTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Games_GameCode",
                        column: x => x.GameCode,
                        principalTable: "Games",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_Status",
                table: "Games",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Moves_GameCode_Ply",
                table: "Moves",
                columns: new[] { "GameCode", "Ply" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_GameCode_Color",
                table: "Players",
                columns: new[] { "GameCode", "Color" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Moves");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
