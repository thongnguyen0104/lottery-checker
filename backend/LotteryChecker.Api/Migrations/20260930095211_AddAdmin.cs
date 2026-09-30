using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <summary>
    /// Thêm quyền admin + tạo sẵn tài khoản quản trị đầu tiên. Chỉ lưu HASH mật khẩu (repo public), và bắt
    /// đổi mật khẩu ở lần đăng nhập đầu — mật khẩu mặc định không dùng được lâu dài ở production.
    /// Username đã có người đăng ký thì bỏ qua (không cấp quyền admin cho tài khoản của người khác).
    /// </summary>
    public partial class AddAdmin : Migration
    {
        public const string SeedUsername = "superadmin";

        /// <summary>PasswordHasher (Identity V3) của mật khẩu mặc định — xem test AdminSeedTests.</summary>
        public const string SeedPasswordHash =
            "AQAAAAIAAYagAAAAEEXvNrAWsHrKs8vR5V9tDiUu+9AKY660sF2Gii21KK2YKLcNqICC2y/xUb/0nebiKw==";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql($"""
                INSERT INTO Users (Username, PasswordHash, CreatedAt, Balance, IsAdmin, MustChangePassword)
                SELECT '{SeedUsername}', '{SeedPasswordHash}', strftime('%Y-%m-%d %H:%M:%f', 'now'), 0, 1, 1
                WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = '{SeedUsername}');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM Users WHERE Username = '{SeedUsername}' AND IsAdmin = 1;");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");
        }
    }
}
