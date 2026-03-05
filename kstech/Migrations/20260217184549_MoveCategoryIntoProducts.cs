using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class MoveCategoryIntoProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "Products",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE p
                SET p.CategoryName = COALESCE(NULLIF(LTRIM(RTRIM(c.CategoryName)), N''), N'Uncategorized')
                FROM Products p
                LEFT JOIN Categories c ON c.CategoryID = p.CategoryID;
                """);

            migrationBuilder.Sql(
                """
                UPDATE Products
                SET CategoryName = N'Uncategorized'
                WHERE CategoryName IS NULL
                   OR LTRIM(RTRIM(CategoryName)) = N''
                   OR CategoryName IN (N'All', N'__OTHER__');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "CategoryName",
                table: "Products",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryID",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryID",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CategoryID",
                table: "Products");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerUserID_CategoryName",
                table: "Products",
                columns: new[] { "OwnerUserID", "CategoryName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerUserID_CategoryName",
                table: "Products");

            migrationBuilder.AddColumn<int?>(
                name: "CategoryID",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerUserID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_OwnerUserID",
                table: "Categories",
                column: "OwnerUserID");

            migrationBuilder.Sql(
                """
                INSERT INTO Categories (CategoryName, Description, OwnerUserID)
                SELECT DISTINCT
                    COALESCE(NULLIF(LTRIM(RTRIM(p.CategoryName)), N''), N'Uncategorized') AS CategoryName,
                    N'' AS Description,
                    p.OwnerUserID
                FROM Products p;
                """);

            migrationBuilder.Sql(
                """
                UPDATE p
                SET p.CategoryID = c.CategoryID
                FROM Products p
                INNER JOIN Categories c
                    ON c.CategoryName = COALESCE(NULLIF(LTRIM(RTRIM(p.CategoryName)), N''), N'Uncategorized')
                   AND (
                        (c.OwnerUserID IS NULL AND p.OwnerUserID IS NULL) OR
                        c.OwnerUserID = p.OwnerUserID
                   );
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM Products WHERE CategoryID IS NULL)
                BEGIN
                    INSERT INTO Categories (CategoryName, Description, OwnerUserID)
                    VALUES (N'Uncategorized', N'', NULL);

                    DECLARE @FallbackCategoryID INT = SCOPE_IDENTITY();

                    UPDATE Products
                    SET CategoryID = @FallbackCategoryID
                    WHERE CategoryID IS NULL;
                END
                """);

            migrationBuilder.AlterColumn<int>(
                name: "CategoryID",
                table: "Products",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryID",
                table: "Products",
                column: "CategoryID");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryID",
                table: "Products",
                column: "CategoryID",
                principalTable: "Categories",
                principalColumn: "CategoryID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "Products");
        }
    }
}
