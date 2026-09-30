using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckedTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckedTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TicketNumber = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    IsWinner = table.Column<bool>(type: "INTEGER", nullable: false),
                    Prize = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckedTickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckedTickets_DrawDate_Province_TicketNumber",
                table: "CheckedTickets",
                columns: new[] { "DrawDate", "Province", "TicketNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckedTickets");
        }
    }
}
