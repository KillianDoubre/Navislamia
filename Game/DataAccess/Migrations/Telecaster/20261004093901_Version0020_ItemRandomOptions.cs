using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0020_ItemRandomOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "RandomOptionTypes",
                table: "Items",
                type: "integer[]",
                nullable: true);

            migrationBuilder.AddColumn<decimal[]>(
                name: "RandomOptionValues",
                table: "Items",
                type: "numeric[]",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "RandomOptionVars",
                table: "Items",
                type: "integer[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RandomOptionTypes",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RandomOptionValues",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RandomOptionVars",
                table: "Items");
        }
    }
}
