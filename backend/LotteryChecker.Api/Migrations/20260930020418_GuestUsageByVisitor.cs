using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class GuestUsageByVisitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Số đếm cũ theo IP không chuyển sang được (và chính nó làm khách chung IP bị chặn oan) —
            // xoá hết, mọi khách bắt đầu lại. Không xoá thì mọi dòng nhận Key "" → trùng khoá chính.
            migrationBuilder.Sql("DELETE FROM GuestUsages;");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuestUsages",
                table: "GuestUsages");

            migrationBuilder.DropColumn(
                name: "Ip",
                table: "GuestUsages");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "GuestUsages",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuestUsages",
                table: "GuestUsages",
                column: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM GuestUsages;");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuestUsages",
                table: "GuestUsages");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "GuestUsages");

            migrationBuilder.AddColumn<string>(
                name: "Ip",
                table: "GuestUsages",
                type: "TEXT",
                maxLength: 45,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuestUsages",
                table: "GuestUsages",
                column: "Ip");
        }
    }
}
