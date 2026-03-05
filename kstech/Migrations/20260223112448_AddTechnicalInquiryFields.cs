using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalInquiryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "TechnicalInquiries");

            migrationBuilder.AlterColumn<string>(
                name: "InquiryMessage",
                table: "TechnicalInquiries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateResolvedUtc",
                table: "TechnicalInquiries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSubmittedUtc",
                table: "TechnicalInquiries",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsResolved",
                table: "TechnicalInquiries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNotes",
                table: "TechnicalInquiries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "TechnicalInquiries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateResolvedUtc",
                table: "TechnicalInquiries");

            migrationBuilder.DropColumn(
                name: "DateSubmittedUtc",
                table: "TechnicalInquiries");

            migrationBuilder.DropColumn(
                name: "IsResolved",
                table: "TechnicalInquiries");

            migrationBuilder.DropColumn(
                name: "ResolutionNotes",
                table: "TechnicalInquiries");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "TechnicalInquiries");

            migrationBuilder.AlterColumn<string>(
                name: "InquiryMessage",
                table: "TechnicalInquiries",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "TechnicalInquiries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
