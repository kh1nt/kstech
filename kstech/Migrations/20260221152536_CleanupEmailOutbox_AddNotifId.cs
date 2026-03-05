using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class CleanupEmailOutbox_AddNotifId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                table: "EmailOutbox");

            migrationBuilder.AddColumn<int>(
                name: "NotifID",
                table: "EmailOutbox",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifID",
                table: "EmailOutbox");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "EmailOutbox",
                type: "datetime2",
                nullable: true);
        }
    }
}
