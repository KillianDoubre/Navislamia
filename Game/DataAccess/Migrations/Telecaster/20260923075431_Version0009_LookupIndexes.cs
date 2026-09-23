using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0009_LookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Items_AccountId",
                table: "Items",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_AccountId",
                table: "Characters",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_AccountName",
                table: "Characters",
                column: "AccountName");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_CharacterName",
                table: "Characters",
                column: "CharacterName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_AccountId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Characters_AccountId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_AccountName",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Characters_CharacterName",
                table: "Characters");
        }
    }
}
