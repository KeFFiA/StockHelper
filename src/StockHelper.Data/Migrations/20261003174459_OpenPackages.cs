using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockHelper.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpenPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpenPackageId",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpenPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SourceIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenPackages_Issues_SourceIssueId",
                        column: x => x.SourceIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenPackages_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenPackages_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_OpenPackageId",
                table: "Issues",
                column: "OpenPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPackages_ItemId_ClosedAt",
                table: "OpenPackages",
                columns: new[] { "ItemId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenPackages_SourceIssueId",
                table: "OpenPackages",
                column: "SourceIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenPackages_UnitId",
                table: "OpenPackages",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_OpenPackages_OpenPackageId",
                table: "Issues",
                column: "OpenPackageId",
                principalTable: "OpenPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Issues_OpenPackages_OpenPackageId",
                table: "Issues");

            migrationBuilder.DropTable(
                name: "OpenPackages");

            migrationBuilder.DropIndex(
                name: "IX_Issues_OpenPackageId",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "OpenPackageId",
                table: "Issues");
        }
    }
}
