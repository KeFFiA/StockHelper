using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockHelper.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssuesAndPackageUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "Units",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Factor",
                table: "Units",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "Receipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitQuantity",
                table: "Receipts",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    UnitQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IssuedTo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StorageLocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ExpectReturn = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReturnedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Issues_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Issues_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Units_BaseUnitId",
                table: "Units",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_UnitId",
                table: "Receipts",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Date",
                table: "Issues",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ItemId_Date",
                table: "Issues",
                columns: new[] { "ItemId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ReturnedAt",
                table: "Issues",
                column: "ReturnedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_StorageLocationId",
                table: "Issues",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_UnitId",
                table: "Issues",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_Units_UnitId",
                table: "Receipts",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Units_Units_BaseUnitId",
                table: "Units",
                column: "BaseUnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_Units_UnitId",
                table: "Receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_Units_Units_BaseUnitId",
                table: "Units");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Units_BaseUnitId",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_UnitId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Factor",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "UnitQuantity",
                table: "Receipts");
        }
    }
}
