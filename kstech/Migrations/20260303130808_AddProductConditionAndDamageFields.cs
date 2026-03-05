using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddProductConditionAndDamageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformanceTier",
                table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "ConditionNotes",
                table: "Products",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConditionStatus",
                table: "Products",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Good");

            migrationBuilder.AddColumn<int>(
                name: "DamagedQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConditionCheckUtc",
                table: "Products",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionNotes",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ConditionStatus",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DamagedQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastConditionCheckUtc",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "PerformanceTier",
                table: "Products",
                type: "int",
                nullable: true);
        }
    }
}
