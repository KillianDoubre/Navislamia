using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0013_CharacterTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MainTitleId",
                table: "Characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CharacterTitleStates",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    OpenedTitleIds = table.Column<int[]>(type: "integer[]", nullable: true),
                    OwnedTitleIds = table.Column<int[]>(type: "integer[]", nullable: true),
                    ConditionIds = table.Column<int[]>(type: "integer[]", nullable: true),
                    ConditionCounts = table.Column<long[]>(type: "bigint[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterTitleStates", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_CharacterTitleStates_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterTitleStates");

            migrationBuilder.DropColumn(
                name: "MainTitleId",
                table: "Characters");
        }
    }
}
