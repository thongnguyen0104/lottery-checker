using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuestUsages",
                columns: table => new
                {
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Scans = table.Column<int>(type: "INTEGER", nullable: false),
                    Checks = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestUsages", x => x.Ip);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuestUsages");
        }
    }
}
