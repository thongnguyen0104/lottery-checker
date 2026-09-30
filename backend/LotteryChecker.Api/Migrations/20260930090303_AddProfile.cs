using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Balance",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "CheckHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketNumber = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Province = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    IsWinner = table.Column<bool>(type: "INTEGER", nullable: false),
                    Prize = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckHistory_UserId_CheckedAt",
                table: "CheckHistory",
                columns: new[] { "UserId", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckHistory");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "Users");
        }
    }
}
