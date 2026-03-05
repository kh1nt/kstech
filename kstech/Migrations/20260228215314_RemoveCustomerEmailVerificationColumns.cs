using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomerEmailVerificationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE u
                SET
                    u.IsEmailVerified = c.IsEmailVerified,
                    u.EmailVerificationToken = c.EmailVerificationToken
                FROM [Users] AS u
                INNER JOIN [Customers] AS c ON c.[UserID] = u.[UserID];
                """);

            migrationBuilder.DropColumn(
                name: "EmailVerificationToken",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsEmailVerified",
                table: "Customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationToken",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailVerified",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE c
                SET
                    c.IsEmailVerified = u.IsEmailVerified,
                    c.EmailVerificationToken = u.EmailVerificationToken
                FROM [Customers] AS c
                INNER JOIN [Users] AS u ON u.[UserID] = c.[UserID];
                """);
        }
    }
}
