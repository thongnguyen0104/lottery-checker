using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVietlottDraws : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VietlottDraws",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Game = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    DrawId = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Numbers = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Bonus = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VietlottDraws", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VietlottDraws_Game_DrawDate",
                table: "VietlottDraws",
                columns: new[] { "Game", "DrawDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VietlottDraws_Game_DrawId",
                table: "VietlottDraws",
                columns: new[] { "Game", "DrawId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VietlottDraws");
        }
    }
}
