using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCustomerAndOpsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Customers
                SET FullName = LTRIM(RTRIM(CONCAT(ISNULL(FirstName, ''), ' ', ISNULL(LastName, ''))))
                WHERE LTRIM(RTRIM(ISNULL(FullName, ''))) = ''
                  AND (LTRIM(RTRIM(ISNULL(FirstName, ''))) <> '' OR LTRIM(RTRIM(ISNULL(LastName, ''))) <> '');

                UPDATE Customers
                SET Phone = ContactNumber
                WHERE LTRIM(RTRIM(ISNULL(Phone, ''))) = ''
                  AND LTRIM(RTRIM(ISNULL(ContactNumber, ''))) <> '';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryDiscrepancies_Users_DetectedByUserID",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropTable(
                name: "IntegrationEvents");

            migrationBuilder.DropIndex(
                name: "IX_InventoryDiscrepancies_DetectedByUserID",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "DetectedByUserID",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectedByUserID",
                table: "InventoryDiscrepancies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "InventoryDiscrepancies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceId",
                table: "InventoryDiscrepancies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "InventoryDiscrepancies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "InventoryDiscrepancies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "Customers",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "IntegrationEvents",
                columns: table => new
                {
                    EventID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEvents", x => x.EventID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiscrepancies_DetectedByUserID",
                table: "InventoryDiscrepancies",
                column: "DetectedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_OccurredAtUtc",
                table: "IntegrationEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_OwnerUserID_OccurredAtUtc",
                table: "IntegrationEvents",
                columns: new[] { "OwnerUserID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_Source_Status",
                table: "IntegrationEvents",
                columns: new[] { "Source", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryDiscrepancies_Users_DetectedByUserID",
                table: "InventoryDiscrepancies",
                column: "DetectedByUserID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
