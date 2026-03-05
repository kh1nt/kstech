using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kstech.Migrations
{
    /// <inheritdoc />
    public partial class AddPcRequirementsToCuratedGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PcRequirementsMinHtml",
                table: "CuratedGames",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PcRequirementsRecHtml",
                table: "CuratedGames",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PcRequirementsMinHtml",
                table: "CuratedGames");

            migrationBuilder.DropColumn(
                name: "PcRequirementsRecHtml",
                table: "CuratedGames");
        }
    }
}
