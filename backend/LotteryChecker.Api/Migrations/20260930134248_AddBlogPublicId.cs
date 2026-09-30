using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlogPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "BlogPosts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Bài đã có: cấp GUID v4 ngẫu nhiên trước khi tạo index unique. EF SQLite lưu Guid dạng
            // chuỗi HOA "XXXXXXXX-XXXX-4XXX-YXXX-XXXXXXXXXXXX" nên sinh đúng dạng đó để truy vấn khớp.
            migrationBuilder.Sql("""
                UPDATE BlogPosts SET PublicId = upper(
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' ||
                    substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_PublicId",
                table: "BlogPosts",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_PublicId",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BlogPosts");
        }
    }
}
