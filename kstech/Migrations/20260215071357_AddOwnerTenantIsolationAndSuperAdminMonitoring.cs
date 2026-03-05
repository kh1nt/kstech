using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerTenantIsolationAndSuperAdminMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "TechnicalInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "SystemLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "LoyaltyLedgerEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "InventoryMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "InventoryDiscrepancies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "IntegrationEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "EmailNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "Campaigns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserID",
                table: "CampaignExecutions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_OwnerUserID",
                table: "Users",
                columns: new[] { "Role", "OwnerUserID" });

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalInquiries_OwnerUserID",
                table: "TechnicalInquiries",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_OwnerUserID_Timestamp",
                table: "SystemLogs",
                columns: new[] { "OwnerUserID", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerUserID",
                table: "Products",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OwnerUserID",
                table: "Payments",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OwnerUserID_OrderDate",
                table: "Orders",
                columns: new[] { "OwnerUserID", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyLedgerEntries_OwnerUserID_OccurredAtUtc",
                table: "LoyaltyLedgerEntries",
                columns: new[] { "OwnerUserID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_OwnerUserID_OccurredAtUtc",
                table: "InventoryMovements",
                columns: new[] { "OwnerUserID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiscrepancies_OwnerUserID_Status_DetectedAtUtc",
                table: "InventoryDiscrepancies",
                columns: new[] { "OwnerUserID", "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_OwnerUserID_OccurredAtUtc",
                table: "IntegrationEvents",
                columns: new[] { "OwnerUserID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_OwnerUserID",
                table: "Employees",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotifications_OwnerUserID",
                table: "EmailNotifications",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OwnerUserID",
                table: "Customers",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_OwnerUserID",
                table: "Categories",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_OwnerUserID",
                table: "Campaigns",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_OwnerUserID",
                table: "CampaignExecutions",
                column: "OwnerUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Role_OwnerUserID",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TechnicalInquiries_OwnerUserID",
                table: "TechnicalInquiries");

            migrationBuilder.DropIndex(
                name: "IX_SystemLogs_OwnerUserID_Timestamp",
                table: "SystemLogs");

            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerUserID",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Payments_OwnerUserID",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OwnerUserID_OrderDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyLedgerEntries_OwnerUserID_OccurredAtUtc",
                table: "LoyaltyLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_OwnerUserID_OccurredAtUtc",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryDiscrepancies_OwnerUserID_Status_DetectedAtUtc",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropIndex(
                name: "IX_IntegrationEvents_OwnerUserID_OccurredAtUtc",
                table: "IntegrationEvents");

            migrationBuilder.DropIndex(
                name: "IX_Employees_OwnerUserID",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmailNotifications_OwnerUserID",
                table: "EmailNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Customers_OwnerUserID",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Categories_OwnerUserID",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_OwnerUserID",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_CampaignExecutions_OwnerUserID",
                table: "CampaignExecutions");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "TechnicalInquiries");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "SystemLogs");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "LoyaltyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "InventoryDiscrepancies");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "IntegrationEvents");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "OwnerUserID",
                table: "CampaignExecutions");
        }
    }
}
