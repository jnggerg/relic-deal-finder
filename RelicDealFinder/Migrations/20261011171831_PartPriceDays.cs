using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelicDealFinder.Migrations
{
    /// <inheritdoc />
    public partial class PartPriceDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PrimeParts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PartPriceDays",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WaPrice = table.Column<double>(type: "REAL", nullable: true),
                    Volume = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartPriceDays", x => new { x.Slug, x.Date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartPriceDays");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "PrimeParts");
        }
    }
}
