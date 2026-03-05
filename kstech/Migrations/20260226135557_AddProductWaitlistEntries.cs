using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddProductWaitlistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductWaitlistEntries",
                columns: table => new
                {
                    ProductWaitlistEntryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    ProductID = table.Column<int>(type: "int", nullable: false),
                    CustomerID = table.Column<int>(type: "int", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QuantityRequested = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWaitlistEntries", x => x.ProductWaitlistEntryID);
                    table.CheckConstraint("CK_ProductWaitlistEntries_QuantityRequested", "[QuantityRequested] >= 1");
                    table.CheckConstraint("CK_ProductWaitlistEntries_Status", "[Status] IN ('Pending','Notified','Cancelled')");
                    table.ForeignKey(
                        name: "FK_ProductWaitlistEntries_Customers_CustomerID",
                        column: x => x.CustomerID,
                        principalTable: "Customers",
                        principalColumn: "CustomerID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductWaitlistEntries_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWaitlistEntries_CustomerID",
                table: "ProductWaitlistEntries",
                column: "CustomerID");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWaitlistEntries_OwnerUserID_ProductID_Status_CreatedAtUtc",
                table: "ProductWaitlistEntries",
                columns: new[] { "OwnerUserID", "ProductID", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWaitlistEntries_ProductID_CustomerEmail_Status",
                table: "ProductWaitlistEntries",
                columns: new[] { "ProductID", "CustomerEmail", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductWaitlistEntries");
        }
    }
}
