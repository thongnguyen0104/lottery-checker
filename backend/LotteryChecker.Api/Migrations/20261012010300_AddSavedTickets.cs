using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SavedTicketId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SavedTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketNumber = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Prize = table.Column<long>(type: "INTEGER", nullable: false),
                    PrizeTiers = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderStage = table.Column<byte>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedTickets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SavedTicketId",
                table: "Notifications",
                column: "SavedTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedTickets_Status_DrawDate_Province",
                table: "SavedTickets",
                columns: new[] { "Status", "DrawDate", "Province" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedTickets_UserId_DrawDate_Province_TicketNumber",
                table: "SavedTickets",
                columns: new[] { "UserId", "DrawDate", "Province", "TicketNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_SavedTickets_SavedTicketId",
                table: "Notifications",
                column: "SavedTicketId",
                principalTable: "SavedTickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_SavedTickets_SavedTicketId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "SavedTickets");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_SavedTicketId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SavedTicketId",
                table: "Notifications");
        }
    }
}
