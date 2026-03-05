using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyMarketingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignExecutions");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "AudienceSize",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "BudgetAmount",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "LastExecutedUtc",
                table: "Campaigns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "EmailNotifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AudienceSize",
                table: "Campaigns",
                type: "int",
                nullable: false,
                defaultValue: 0);

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
                name: "EndDate",
                table: "Campaigns",
                type: "datetime2",
                nullable: true);

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
                    AttributedRevenue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    AudienceSize = table.Column<int>(type: "int", nullable: false),
                    CampaignCost = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConversionCount = table.Column<int>(type: "int", nullable: false),
                    DeliveredCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    OpenCount = table.Column<int>(type: "int", nullable: false),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_CampaignID",
                table: "CampaignExecutions",
                column: "CampaignID");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_OwnerUserID",
                table: "CampaignExecutions",
                column: "OwnerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignExecutions_TriggeredByUserID",
                table: "CampaignExecutions",
                column: "TriggeredByUserID");
        }
    }
}
