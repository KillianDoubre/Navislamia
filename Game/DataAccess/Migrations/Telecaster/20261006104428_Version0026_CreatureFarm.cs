using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0026_CreatureFarm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreatureFarms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    CardItemId = table.Column<long>(type: "bigint", nullable: false),
                    MaxLevel = table.Column<int>(type: "integer", nullable: false),
                    IsUsingCracker = table.Column<bool>(type: "boolean", nullable: false),
                    IsCash = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Duration = table.Column<int>(type: "integer", nullable: false),
                    NursingTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureFarms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreatureFarms_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreatureFarms_CardItemId",
                table: "CreatureFarms",
                column: "CardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureFarms_CharacterId_Slot",
                table: "CreatureFarms",
                columns: new[] { "CharacterId", "Slot" },
                unique: true,
                filter: "\"DeletedOn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreatureFarms");
        }
    }
}
