using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class QuestLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterQuests_CharacterId_Code",
                table: "CharacterQuests");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "CharacterQuests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RemainingSeconds",
                table: "CharacterQuests",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "CharacterQuestCompletions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterQuestCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterQuestCompletions_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuests_CharacterId_Code",
                table: "CharacterQuests",
                columns: new[] { "CharacterId", "Code" },
                unique: true,
                filter: "\"DeletedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuestCompletions_CharacterId_Code",
                table: "CharacterQuestCompletions",
                columns: new[] { "CharacterId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterQuestCompletions");

            migrationBuilder.DropIndex(
                name: "IX_CharacterQuests_CharacterId_Code",
                table: "CharacterQuests");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "CharacterQuests");

            migrationBuilder.DropColumn(
                name: "RemainingSeconds",
                table: "CharacterQuests");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterQuests_CharacterId_Code",
                table: "CharacterQuests",
                columns: new[] { "CharacterId", "Code" },
                unique: true);
        }
    }
}
