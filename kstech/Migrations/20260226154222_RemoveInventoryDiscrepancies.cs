using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInventoryDiscrepancies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryDiscrepancies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryDiscrepancies",
                columns: table => new
                {
                    DiscrepancyID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    ActualQuantity = table.Column<int>(type: "int", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "int", nullable: false),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Variance = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryDiscrepancies", x => x.DiscrepancyID);
                    table.CheckConstraint("CK_InventoryDiscrepancies_Status", "[Status] IN ('Open','Resolved')");
                    table.ForeignKey(
                        name: "FK_InventoryDiscrepancies_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiscrepancies_OwnerUserID_Status_DetectedAtUtc",
                table: "InventoryDiscrepancies",
                columns: new[] { "OwnerUserID", "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiscrepancies_ProductID",
                table: "InventoryDiscrepancies",
                column: "ProductID");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiscrepancies_Status_DetectedAtUtc",
                table: "InventoryDiscrepancies",
                columns: new[] { "Status", "DetectedAtUtc" });
        }
    }
}
