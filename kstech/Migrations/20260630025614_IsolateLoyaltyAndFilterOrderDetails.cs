using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class IsolateLoyaltyAndFilterOrderDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerTenantLoyalties",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<int>(type: "int", nullable: false),
                    TenantOwnerUserID = table.Column<int>(type: "int", nullable: false),
                    LoyaltyPoints = table.Column<int>(type: "int", nullable: false),
                    LifetimePointsEarned = table.Column<int>(type: "int", nullable: false),
                    LifetimePointsRedeemed = table.Column<int>(type: "int", nullable: false),
                    LastLoyaltyActivityUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTenantLoyalties", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CustomerTenantLoyalties_Customers_CustomerID",
                        column: x => x.CustomerID,
                        principalTable: "Customers",
                        principalColumn: "CustomerID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTenantLoyalties_CustomerID_TenantOwnerUserID",
                table: "CustomerTenantLoyalties",
                columns: new[] { "CustomerID", "TenantOwnerUserID" },
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO CustomerTenantLoyalties (CustomerID, TenantOwnerUserID, LoyaltyPoints, LifetimePointsEarned, LifetimePointsRedeemed, LastLoyaltyActivityUtc)
                SELECT c.CustomerID, COALESCE(u.OwnerUserID, (SELECT TOP 1 UserID FROM Users WHERE Role = 'Owner' ORDER BY UserID)), c.LoyaltyPoints, c.LifetimePointsEarned, c.LifetimePointsRedeemed, c.LastLoyaltyActivityUtc
                FROM Customers c
                INNER JOIN Users u ON c.UserID = u.UserID
                WHERE c.LoyaltyPoints > 0 OR c.LifetimePointsEarned > 0 OR c.LifetimePointsRedeemed > 0;
            ");

            migrationBuilder.DropColumn(
                name: "LastLoyaltyActivityUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LifetimePointsEarned",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LifetimePointsRedeemed",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LoyaltyPoints",
                table: "Customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerTenantLoyalties");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoyaltyActivityUtc",
                table: "Customers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifetimePointsEarned",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LifetimePointsRedeemed",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPoints",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
