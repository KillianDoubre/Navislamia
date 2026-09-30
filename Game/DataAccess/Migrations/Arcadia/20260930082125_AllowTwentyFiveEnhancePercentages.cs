using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AllowTwentyFiveEnhancePercentages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EnhanceResourceEntity_Percentage_MaxSize20",
                table: "EnhanceResources");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EnhanceResourceEntity_Percentage_MaxSize25",
                table: "EnhanceResources",
                sql: "cardinality(\"Percentage\") <= 25");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EnhanceResourceEntity_Percentage_MaxSize25",
                table: "EnhanceResources");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EnhanceResourceEntity_Percentage_MaxSize20",
                table: "EnhanceResources",
                sql: "cardinality(\"Percentage\") <= 20");
        }
    }
}
