using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Seed = table.Column<uint>(type: "INTEGER", nullable: false),
                    Rewarded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Ticks = table.Column<int>(type: "INTEGER", nullable: false),
                    Reward = table.Column<long>(type: "INTEGER", nullable: false),
                    Inputs = table.Column<string>(type: "TEXT", maxLength: 40000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_PublicId",
                table: "GameSessions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_UserId_StartedAt",
                table: "GameSessions",
                columns: new[] { "UserId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSessions");
        }
    }
}
