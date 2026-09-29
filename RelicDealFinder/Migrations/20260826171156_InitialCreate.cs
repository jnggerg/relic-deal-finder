using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelicDealFinder.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrimeParts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Vaulted = table.Column<bool>(type: "INTEGER", nullable: false),
                    GameRef = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrimeParts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Relics",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    CommonRewardSlugs = table.Column<string>(type: "TEXT", nullable: false),
                    UncommonRewardSlugs = table.Column<string>(type: "TEXT", nullable: false),
                    RareRewardSlug = table.Column<string>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relics", x => x.Slug);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrimeParts");

            migrationBuilder.DropTable(name: "Relics");
        }
    }
}
