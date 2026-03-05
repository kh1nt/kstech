using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class RefactorFinancialBudgetRevisionsAndLocalDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateUtc_PeriodEndDateUtc",
                table: "FinancialBudgets");

            migrationBuilder.RenameColumn(
                name: "PeriodStartDateUtc",
                table: "FinancialBudgets",
                newName: "PeriodStartDateLocal");

            migrationBuilder.RenameColumn(
                name: "PeriodEndDateUtc",
                table: "FinancialBudgets",
                newName: "PeriodEndDateLocal");

            migrationBuilder.AddColumn<Guid>(
                name: "BudgetSeriesId",
                table: "FinancialBudgets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentRevision",
                table: "FinancialBudgets",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "FinancialBudgets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "FinancialBudgets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [FinancialBudgets]
                SET
                    [BudgetSeriesId] = NEWID(),
                    [RevisionNumber] = 1,
                    [IsCurrentRevision] = 1,
                    [Status] = 'Active'
                WHERE [BudgetSeriesId] IS NULL
                   OR [RevisionNumber] IS NULL
                   OR [IsCurrentRevision] IS NULL
                   OR [Status] IS NULL
                   OR [Status] = '';
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "BudgetSeriesId",
                table: "FinancialBudgets",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsCurrentRevision",
                table: "FinancialBudgets",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RevisionNumber",
                table: "FinancialBudgets",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "FinancialBudgets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "BudgetSeriesId",
                table: "FinancialBudgets");

            migrationBuilder.DropColumn(
                name: "IsCurrentRevision",
                table: "FinancialBudgets");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "FinancialBudgets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "FinancialBudgets");

            migrationBuilder.RenameColumn(
                name: "PeriodStartDateLocal",
                table: "FinancialBudgets",
                newName: "PeriodStartDateUtc");

            migrationBuilder.RenameColumn(
                name: "PeriodEndDateLocal",
                table: "FinancialBudgets",
                newName: "PeriodEndDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialBudgets_OwnerUserID_PeriodStartDateUtc_PeriodEndDateUtc",
                table: "FinancialBudgets",
                columns: new[] { "OwnerUserID", "PeriodStartDateUtc", "PeriodEndDateUtc" },
                unique: true,
                filter: "[OwnerUserID] IS NOT NULL");
        }
    }
}
