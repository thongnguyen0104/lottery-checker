using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScratchTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScratchTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    WinningNumber = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    Prize = table.Column<long>(type: "INTEGER", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScratchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScratchTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScratchTickets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScratchTickets_Status_DrawDate",
                table: "ScratchTickets",
                columns: new[] { "Status", "DrawDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScratchTickets_UserId_PurchasedAt",
                table: "ScratchTickets",
                columns: new[] { "UserId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_UserId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScratchTickets");

            migrationBuilder.DropTable(
                name: "WalletTransactions");
        }
    }
}
