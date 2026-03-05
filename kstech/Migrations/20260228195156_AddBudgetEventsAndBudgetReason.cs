using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetEventsAndBudgetReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetEvents",
                columns: table => new
                {
                    BudgetEventID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    BudgetID = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    BeforeAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    AfterAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PerformedByUserID = table.Column<int>(type: "int", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetEvents", x => x.BudgetEventID);
                    table.ForeignKey(
                        name: "FK_BudgetEvents_FinancialBudgets_BudgetID",
                        column: x => x.BudgetID,
                        principalTable: "FinancialBudgets",
                        principalColumn: "BudgetID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetEvents_Users_PerformedByUserID",
                        column: x => x.PerformedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEvents_BudgetID_EventType_OccurredAtUtc",
                table: "BudgetEvents",
                columns: new[] { "BudgetID", "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEvents_OwnerUserID_BudgetID_OccurredAtUtc",
                table: "BudgetEvents",
                columns: new[] { "OwnerUserID", "BudgetID", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEvents_PerformedByUserID",
                table: "BudgetEvents",
                column: "PerformedByUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetEvents");
        }
    }
}
