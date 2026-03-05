using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyFinancialBudgetRemoveRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_BudgetSeriesId_RevisionNumber",
                table: "FinancialBudgets");

            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_IsCurrentRevision_UpdatedAtUtc",
                table: "FinancialBudgets");

            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateLocal_PeriodEndDateLocal_IsCurrentRevision",
                table: "FinancialBudgets");

            migrationBuilder.Sql(
                """
                INSERT INTO SystemLogs (UserID, OwnerUserID, Action, Timestamp)
                SELECT
                    financialBudget.OwnerUserID,
                    financialBudget.OwnerUserID,
                    CONCAT(
                        'Budget Imported [BudgetId:', financialBudget.BudgetID, '] ',
                        '[Month:', CONVERT(varchar(7), financialBudget.PeriodStartDateLocal, 126), '] ',
                        'Amt:', CONVERT(varchar(20), CAST(financialBudget.BudgetAmount AS decimal(10,2))), ' ',
                        'Rev:', CONVERT(varchar(10), financialBudget.RevisionNumber), ' ',
                        'Cur:', CASE WHEN financialBudget.IsCurrentRevision = 1 THEN 'Y' ELSE 'N' END, ' ',
                        'St:', financialBudget.Status
                    ),
                    financialBudget.UpdatedAtUtc
                FROM FinancialBudgets AS financialBudget
                INNER JOIN Users AS ownerUser ON ownerUser.UserID = financialBudget.OwnerUserID
                WHERE financialBudget.OwnerUserID IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "BudgetSeriesId",
                table: "FinancialBudgets");

            migrationBuilder.DropColumn(
                name: "IsCurrentRevision",
                table: "FinancialBudgets");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "FinancialBudgets");

            migrationBuilder.Sql(
                """
                ;WITH RankedBudgets AS (
                    SELECT
                        BudgetID,
                        ROW_NUMBER() OVER (
                            PARTITION BY OwnerUserID, PeriodStartDateLocal, PeriodEndDateLocal
                            ORDER BY UpdatedAtUtc DESC, BudgetID DESC
                        ) AS RowRank,
                        FIRST_VALUE(BudgetID) OVER (
                            PARTITION BY OwnerUserID, PeriodStartDateLocal, PeriodEndDateLocal
                            ORDER BY UpdatedAtUtc DESC, BudgetID DESC
                        ) AS KeepBudgetID
                    FROM FinancialBudgets
                )
                UPDATE purchaseOrder
                SET purchaseOrder.BudgetID = ranked.KeepBudgetID
                FROM PurchaseOrders AS purchaseOrder
                INNER JOIN RankedBudgets AS ranked ON purchaseOrder.BudgetID = ranked.BudgetID
                WHERE ranked.RowRank > 1;
                """);

            migrationBuilder.Sql(
                """
                ;WITH RankedBudgets AS (
                    SELECT
                        BudgetID,
                        ROW_NUMBER() OVER (
                            PARTITION BY OwnerUserID, PeriodStartDateLocal, PeriodEndDateLocal
                            ORDER BY UpdatedAtUtc DESC, BudgetID DESC
                        ) AS RowRank,
                        FIRST_VALUE(BudgetID) OVER (
                            PARTITION BY OwnerUserID, PeriodStartDateLocal, PeriodEndDateLocal
                            ORDER BY UpdatedAtUtc DESC, BudgetID DESC
                        ) AS KeepBudgetID
                    FROM FinancialBudgets
                )
                UPDATE systemLog
                SET systemLog.Action = REPLACE(
                    systemLog.Action,
                    CONCAT('[BudgetId:', ranked.BudgetID, ']'),
                    CONCAT('[BudgetId:', ranked.KeepBudgetID, ']'))
                FROM SystemLogs AS systemLog
                INNER JOIN RankedBudgets AS ranked ON ranked.RowRank > 1
                WHERE CHARINDEX(CONCAT('[BudgetId:', ranked.BudgetID, ']'), systemLog.Action) > 0;
                """);

            migrationBuilder.Sql(
                """
                ;WITH RankedBudgets AS (
                    SELECT
                        BudgetID,
                        ROW_NUMBER() OVER (
                            PARTITION BY OwnerUserID, PeriodStartDateLocal, PeriodEndDateLocal
                            ORDER BY UpdatedAtUtc DESC, BudgetID DESC
                        ) AS RowRank
                    FROM FinancialBudgets
                )
                DELETE financialBudget
                FROM FinancialBudgets AS financialBudget
                INNER JOIN RankedBudgets AS ranked ON financialBudget.BudgetID = ranked.BudgetID
                WHERE ranked.RowRank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateLocal_PeriodEndDateLocal",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "PeriodStartDateLocal", "PeriodEndDateLocal" },
                unique: true,
                filter: "[OwnerUserID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_Status_UpdatedAtUtc",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateLocal_PeriodEndDateLocal",
                table: "FinancialBudgets");

            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_Status_UpdatedAtUtc",
                table: "FinancialBudgets");

            migrationBuilder.AddColumn<Guid>(
                name: "BudgetSeriesId",
                table: "FinancialBudgets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentRevision",
                table: "FinancialBudgets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "FinancialBudgets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_BudgetSeriesId_RevisionNumber",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "BudgetSeriesId", "RevisionNumber" },
                unique: true,
                filter: "[OwnerUserID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_IsCurrentRevision_UpdatedAtUtc",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "IsCurrentRevision", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateLocal_PeriodEndDateLocal_IsCurrentRevision",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "PeriodStartDateLocal", "PeriodEndDateLocal", "IsCurrentRevision" });
        }
    }
}
