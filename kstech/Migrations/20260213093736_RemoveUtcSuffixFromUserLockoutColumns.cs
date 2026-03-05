using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUtcSuffixFromUserLockoutColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LockoutEndUtc",
                table: "Users",
                newName: "LockoutEnd");

            migrationBuilder.RenameColumn(
                name: "LastFailedLoginUtc",
                table: "Users",
                newName: "LastFailedLogin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LockoutEnd",
                table: "Users",
                newName: "LockoutEndUtc");

            migrationBuilder.RenameColumn(
                name: "LastFailedLogin",
                table: "Users",
                newName: "LastFailedLoginUtc");
        }
    }
}
