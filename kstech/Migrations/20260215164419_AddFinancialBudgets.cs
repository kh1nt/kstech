using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialBudgets",
                columns: table => new
                {
                    BudgetID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true),
                    PeriodStartDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialBudgets", x => x.BudgetID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateUtc_PeriodEndDateUtc",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "PeriodStartDateUtc", "PeriodEndDateUtc" },
                unique: true,
                filter: "[OwnerUserID] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialBudgets");
        }
    }
}
