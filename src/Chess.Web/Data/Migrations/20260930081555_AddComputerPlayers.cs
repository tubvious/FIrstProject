using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chess.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComputerPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BotLevel",
                table: "Players",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BotLevel",
                table: "Players");
        }
    }
}
