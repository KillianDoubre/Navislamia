using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0008_CharacterQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterQuests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    StartId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<int[]>(type: "integer[]", maxLength: 6, nullable: true),
                    Status = table.Column<int[]>(type: "integer[]", maxLength: 6, nullable: true),
                    Progress = table.Column<byte>(type: "smallint", nullable: false),
                    TimeLimit = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterQuests_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuests_CharacterId_Code",
                table: "CharacterQuests",
                columns: new[] { "CharacterId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterQuests");
        }
    }
}
