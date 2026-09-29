using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelicDealFinder.Migrations
{
    /// <inheritdoc />
    public partial class RelicValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "IntPotentialPlat",
                table: "Relics",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RadPotentialPlat",
                table: "Relics",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntPotentialPlat",
                table: "Relics");

            migrationBuilder.DropColumn(
                name: "RadPotentialPlat",
                table: "Relics");
        }
    }
}
