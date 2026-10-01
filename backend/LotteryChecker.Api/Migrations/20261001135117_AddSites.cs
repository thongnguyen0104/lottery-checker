using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LotteryChecker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SiteReservationId",
                table: "Notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SiteImages",
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
                    table.PrimaryKey("PK_SiteImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ShopId = table.Column<int>(type: "INTEGER", nullable: true),
                    DraftJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReportCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Sites_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SitePosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContentHtml = table.Column<string>(type: "TEXT", nullable: false),
                    CoverKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BlogPostId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SitePosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SitePosts_BlogPosts_BlogPostId",
                        column: x => x.BlogPostId,
                        principalTable: "BlogPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SitePosts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Price = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ImageKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Stock = table.Column<int>(type: "INTEGER", nullable: true),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteProducts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReports_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    RequesterKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReservations_SiteProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "SiteProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SiteReservations_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteReservations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SiteReservationId",
                table: "Notifications",
                column: "SiteReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteImages_Key",
                table: "SiteImages",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteImages_Used_CreatedAt",
                table: "SiteImages",
                columns: new[] { "Used", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteImages_UserId",
                table: "SiteImages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePosts_BlogPostId",
                table: "SitePosts",
                column: "BlogPostId");

            migrationBuilder.CreateIndex(
                name: "IX_SitePosts_PublicId",
                table: "SitePosts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SitePosts_SiteId_Status_PublishedAt",
                table: "SitePosts",
                columns: new[] { "SiteId", "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteProducts_SiteId_SortOrder",
                table: "SiteProducts",
                columns: new[] { "SiteId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteReports_SiteId_UserId",
                table: "SiteReports",
                columns: new[] { "SiteId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteReports_UserId",
                table: "SiteReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReservations_ProductId",
                table: "SiteReservations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReservations_PublicId",
                table: "SiteReservations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteReservations_SiteId_RequesterKey_Status",
                table: "SiteReservations",
                columns: new[] { "SiteId", "RequesterKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteReservations_SiteId_Status_Id",
                table: "SiteReservations",
                columns: new[] { "SiteId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteReservations_UserId",
                table: "SiteReservations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_OwnerUserId",
                table: "Sites",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_PublicId",
                table: "Sites",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_ShopId",
                table: "Sites",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_SiteReservations_SiteReservationId",
                table: "Notifications",
                column: "SiteReservationId",
                principalTable: "SiteReservations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_SiteReservations_SiteReservationId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "SiteImages");

            migrationBuilder.DropTable(
                name: "SitePosts");

            migrationBuilder.DropTable(
                name: "SiteReports");

            migrationBuilder.DropTable(
                name: "SiteReservations");

            migrationBuilder.DropTable(
                name: "SiteProducts");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_SiteReservationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SiteReservationId",
                table: "Notifications");
        }
    }
}
