using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddAuctionCateryResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionCateryResources",
                columns: table => new
                {
                    CateryId = table.Column<int>(type: "integer", nullable: false),
                    SubCateryId = table.Column<int>(type: "integer", nullable: false),
                    NameId = table.Column<int>(type: "integer", nullable: false),
                    LocalFlag = table.Column<int>(type: "integer", nullable: false),
                    ItemGroup = table.Column<int>(type: "integer", nullable: false),
                    ItemClass = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionCateryResources", x => new { x.CateryId, x.SubCateryId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionCateryResources");
        }
    }
}
