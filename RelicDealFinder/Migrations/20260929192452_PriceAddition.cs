using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelicDealFinder.Migrations
{
    /// <inheritdoc />
    public partial class PriceAddition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Price",
                table: "PrimeParts",
                type: "REAL",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Price", table: "PrimeParts");
        }
    }
}
