using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class RemovePerformanceTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredPerformanceTier",
                table: "CuratedGames");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredPerformanceTier",
                table: "CuratedGames",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
