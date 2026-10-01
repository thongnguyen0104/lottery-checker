using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShopMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "CommentId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ShopId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShopReviewId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShopWinReportId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShopImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Used = table.Column<bool>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Lat = table.Column<double>(type: "REAL", nullable: false),
                    Lng = table.Column<double>(type: "REAL", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    OpensAtMin = table.Column<int>(type: "INTEGER", nullable: true),
                    ClosesAtMin = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImageKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ConfirmCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RatingSum = table.Column<int>(type: "INTEGER", nullable: false),
                    RatingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WinReportCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shops_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopConfirms",
                columns: table => new
                {
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopConfirms", x => new { x.ShopId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ShopConfirms_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopConfirms_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopReports",
                columns: table => new
                {
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopReports", x => new { x.ShopId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ShopReports_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopReviews_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopWinReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProvinceCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PrizeTier = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ImageKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopWinReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopWinReports_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShopWinReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ShopId",
                table: "Notifications",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ShopReviewId",
                table: "Notifications",
                column: "ShopReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ShopWinReportId",
                table: "Notifications",
                column: "ShopWinReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopConfirms_UserId",
                table: "ShopConfirms",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopImages_Key",
                table: "ShopImages",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopImages_Used_CreatedAt",
                table: "ShopImages",
                columns: new[] { "Used", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopReports_UserId",
                table: "ShopReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopReviews_ShopId_UserId",
                table: "ShopReviews",
                columns: new[] { "ShopId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopReviews_UserId",
                table: "ShopReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_PublicId",
                table: "Shops",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Status_Lat_Lng",
                table: "Shops",
                columns: new[] { "Status", "Lat", "Lng" });

            migrationBuilder.CreateIndex(
                name: "IX_Shops_UserId_CreatedAt",
                table: "Shops",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopWinReports_ShopId_DrawDate",
                table: "ShopWinReports",
                columns: new[] { "ShopId", "DrawDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopWinReports_UserId",
                table: "ShopWinReports",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_ShopReviews_ShopReviewId",
                table: "Notifications",
                column: "ShopReviewId",
                principalTable: "ShopReviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_ShopWinReports_ShopWinReportId",
                table: "Notifications",
                column: "ShopWinReportId",
                principalTable: "ShopWinReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Shops_ShopId",
                table: "Notifications",
                column: "ShopId",
                principalTable: "Shops",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_ShopReviews_ShopReviewId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_ShopWinReports_ShopWinReportId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Shops_ShopId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "ShopConfirms");

            migrationBuilder.DropTable(
                name: "ShopImages");

            migrationBuilder.DropTable(
                name: "ShopReports");

            migrationBuilder.DropTable(
                name: "ShopReviews");

            migrationBuilder.DropTable(
                name: "ShopWinReports");

            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ShopId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ShopReviewId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ShopWinReportId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ShopReviewId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ShopWinReportId",
                table: "Notifications");

            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CommentId",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
