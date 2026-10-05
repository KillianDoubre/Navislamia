using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0025_PartyPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Parties_LeadPartyId",
                table: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_Parties_LeaderId",
                table: "Parties");

            migrationBuilder.AlterColumn<long>(
                name: "LeadPartyId",
                table: "Parties",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_LeaderId",
                table: "Parties",
                column: "LeaderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Parties_LeadPartyId",
                table: "Parties",
                column: "LeadPartyId",
                principalTable: "Parties",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Parties_LeadPartyId",
                table: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_Parties_LeaderId",
                table: "Parties");

            migrationBuilder.AlterColumn<long>(
                name: "LeadPartyId",
                table: "Parties",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_LeaderId",
                table: "Parties",
                column: "LeaderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Parties_LeadPartyId",
                table: "Parties",
                column: "LeadPartyId",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
