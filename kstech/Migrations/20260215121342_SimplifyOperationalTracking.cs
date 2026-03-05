using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyOperationalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE InventoryDiscrepancies
                SET Status = CASE WHEN Status = 'Resolved' THEN 'Resolved' ELSE 'Open' END;

                UPDATE InventoryDiscrepancies
                SET ResolvedAtUtc = ISNULL(ResolvedAtUtc, DetectedAtUtc)
                WHERE Status = 'Resolved';

                DELETE FROM IntegrationEvents
                WHERE ISNULL(Status, '') NOT IN ('Failed', 'Investigate', 'CompletedWithErrors');
                """);

            migrationBuilder.DropTable(
                name: "LoyaltyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "IntegrationEvents");

            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                table: "IntegrationEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryDiscrepancies_Status",
                table: "InventoryDiscrepancies",
                sql: "[Status] IN ('Open','Resolved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryDiscrepancies_Status",
                table: "InventoryDiscrepancies");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "IntegrationEvents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "IntegrationEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LoyaltyLedgerEntries",
                columns: table => new
                {
                    EntryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<int>(type: "int", nullable: false),
                    OrderID = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MonetaryValue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    PointsDelta = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyLedgerEntries", x => x.EntryID);
                    table.ForeignKey(
                        name: "FK_LoyaltyLedgerEntries_Customers_CustomerID",
                        column: x => x.CustomerID,
                        principalTable: "Customers",
                        principalColumn: "CustomerID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoyaltyLedgerEntries_Orders_OrderID",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyLedgerEntries_CustomerID_OccurredAtUtc",
                table: "LoyaltyLedgerEntries",
                columns: new[] { "CustomerID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyLedgerEntries_OrderID",
                table: "LoyaltyLedgerEntries",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyLedgerEntries_OwnerUserID_OccurredAtUtc",
                table: "LoyaltyLedgerEntries",
                columns: new[] { "OwnerUserID", "OccurredAtUtc" });
        }
    }
}
