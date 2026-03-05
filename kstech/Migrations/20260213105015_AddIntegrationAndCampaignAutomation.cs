using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationAndCampaignAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMarketPriceSyncUtc",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketPriceSource",
                table: "Products",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SteamAppId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Payments",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDateUtc",
                table: "Payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Payments",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Payments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CampaignID",
                table: "EmailNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "EmailNotifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "EmailNotifications",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalMessageId",
                table: "EmailNotifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "MarketingOptIn",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetAmount",
                table: "Campaigns",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "Campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastExecutedUtc",
                table: "Campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignExecutions",
                columns: table => new
                {
                    ExecutionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CampaignID = table.Column<int>(type: "int", nullable: false),
                    TriggeredByUserID = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AudienceSize = table.Column<int>(type: "int", nullable: false),
                    DeliveredCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    OpenCount = table.Column<int>(type: "int", nullable: false),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
                    ConversionCount = table.Column<int>(type: "int", nullable: false),
                    AttributedRevenue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CampaignCost = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignExecutions", x => x.ExecutionID);
                    table.ForeignKey(
                        name: "FK_CampaignExecutions_Campaigns_CampaignID",
                        column: x => x.CampaignID,
                        principalTable: "Campaigns",
                        principalColumn: "CampaignID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignExecutions_Users_TriggeredByUserID",
                        column: x => x.TriggeredByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationEvents",
                columns: table => new
                {
                    EventID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEvents", x => x.EventID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotifications_CampaignID",
                table: "EmailNotifications",
                column: "CampaignID");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_CampaignID",
                table: "CampaignExecutions",
                column: "CampaignID");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_TriggeredByUserID",
                table: "CampaignExecutions",
                column: "TriggeredByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_OccurredAtUtc",
                table: "IntegrationEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEvents_Source_Status",
                table: "IntegrationEvents",
                columns: new[] { "Source", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmailNotifications_Campaigns_CampaignID",
                table: "EmailNotifications",
                column: "CampaignID",
                principalTable: "Campaigns",
                principalColumn: "CampaignID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailNotifications_Campaigns_CampaignID",
                table: "EmailNotifications");

            migrationBuilder.DropTable(
                name: "CampaignExecutions");

            migrationBuilder.DropTable(
                name: "IntegrationEvents");

            migrationBuilder.DropIndex(
                name: "IX_EmailNotifications_CampaignID",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "LastMarketPriceSyncUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MarketPriceSource",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SteamAppId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentDateUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CampaignID",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "ExternalMessageId",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "MarketingOptIn",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BudgetAmount",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "LastExecutedUtc",
                table: "Campaigns");
        }
    }
}
