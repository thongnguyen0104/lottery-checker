using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotteryResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PrizeTier = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotteryResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotteryResults_DrawDate_Province",
                table: "LotteryResults",
                columns: new[] { "DrawDate", "Province" });

            migrationBuilder.CreateIndex(
                name: "IX_LotteryResults_Number",
                table: "LotteryResults",
                column: "Number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotteryResults");
        }
    }
}
