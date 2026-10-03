using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0017_GuildsAndSieges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Dungeons_Guilds_RaidGuildId",
                table: "Dungeons");

            migrationBuilder.DropForeignKey(
                name: "FK_Guilds_Alliances_AllianceId",
                table: "Guilds");

            migrationBuilder.DropForeignKey(
                name: "FK_Guilds_Dungeons_DungeonId",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_AllianceId",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_DungeonId",
                table: "Guilds");

            migrationBuilder.AlterColumn<long>(
                name: "DungeonId",
                table: "Guilds",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "AllianceId",
                table: "Guilds",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "LeaderId",
                table: "Guilds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Guilds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuildMemo",
                table: "Characters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "GuildPermission",
                table: "Characters",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Alliances",
                type: "text",
                nullable: true);

            // The previous model could store neither a leader nor member ranks. Recover an existing
            // guild deterministically from its oldest active member, keeping empty legacy guilds intact.
            migrationBuilder.Sql("""
                UPDATE "Guilds" SET "AllianceId" = NULL WHERE "AllianceId" = 0;
                UPDATE "Guilds" SET "DungeonId" = NULL WHERE "DungeonId" = 0;
                UPDATE "Guilds" SET "NormalizedName" = upper(trim("Name"));
                UPDATE "Alliances" SET "NormalizedName" = upper(trim("Name"));
                UPDATE "Guilds" g SET "LeaderId" = (
                    SELECT min(c."Id") FROM "Characters" c WHERE c."GuildId" = g."Id" AND c."DeletedOn" IS NULL);
                UPDATE "Characters" SET "GuildPermission" = 1 WHERE "GuildId" IS NOT NULL AND "DeletedOn" IS NULL;
                UPDATE "Characters" c SET "GuildPermission" = 7 WHERE EXISTS (
                    SELECT 1 FROM "Guilds" g WHERE g."LeaderId" = c."Id" AND g."DeletedOn" IS NULL);
                UPDATE "Characters" SET "GuildMemo" = '' WHERE "GuildMemo" IS NULL;
                UPDATE "Alliances" a SET "LeadGuildId" = (
                    SELECT min(g."Id") FROM "Guilds" g WHERE g."AllianceId" = a."Id")
                    WHERE NOT EXISTS (SELECT 1 FROM "Guilds" g WHERE g."Id" = a."LeadGuildId")
                      AND EXISTS (SELECT 1 FROM "Guilds" g WHERE g."AllianceId" = a."Id");
                """);

            migrationBuilder.CreateTable(
                name: "GuildRaids",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    DungeonId = table.Column<long>(type: "bigint", nullable: false),
                    Week = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BestTime = table.Column<int>(type: "integer", nullable: false),
                    Boss1Dead = table.Column<bool>(type: "boolean", nullable: false),
                    Boss2Dead = table.Column<bool>(type: "boolean", nullable: false),
                    WrappedUp = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildRaids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildRaids_Dungeons_DungeonId",
                        column: x => x.DungeonId,
                        principalTable: "Dungeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuildRaids_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuildSieges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DungeonId = table.Column<long>(type: "bigint", nullable: false),
                    Week = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefenderId = table.Column<long>(type: "bigint", nullable: true),
                    AttackerId = table.Column<long>(type: "bigint", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WinnerId = table.Column<long>(type: "bigint", nullable: true),
                    CoreDestroyed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildSieges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildSieges_Dungeons_DungeonId",
                        column: x => x.DungeonId,
                        principalTable: "Dungeons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuildSiegeParticipants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SiegeId = table.Column<long>(type: "bigint", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Attacker = table.Column<bool>(type: "boolean", nullable: false),
                    StartCredited = table.Column<bool>(type: "boolean", nullable: false),
                    EndCredited = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildSiegeParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildSiegeParticipants_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuildSiegeParticipants_GuildSieges_SiegeId",
                        column: x => x.SiegeId,
                        principalTable: "GuildSieges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_AllianceId",
                table: "Guilds",
                column: "AllianceId");

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_DungeonId",
                table: "Guilds",
                column: "DungeonId");

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_LeaderId",
                table: "Guilds",
                column: "LeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_NormalizedName",
                table: "Guilds",
                column: "NormalizedName",
                unique: true,
                filter: "\"DeletedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Dungeons_OwnerGuildId",
                table: "Dungeons",
                column: "OwnerGuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Alliances_LeadGuildId",
                table: "Alliances",
                column: "LeadGuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Alliances_NormalizedName",
                table: "Alliances",
                column: "NormalizedName",
                unique: true,
                filter: "\"DeletedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GuildRaids_DungeonId",
                table: "GuildRaids",
                column: "DungeonId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildRaids_GuildId_Week",
                table: "GuildRaids",
                columns: new[] { "GuildId", "Week" },
                unique: true,
                filter: "\"DeletedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GuildSiegeParticipants_CharacterId",
                table: "GuildSiegeParticipants",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildSiegeParticipants_SiegeId_CharacterId",
                table: "GuildSiegeParticipants",
                columns: new[] { "SiegeId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildSieges_DungeonId_Week",
                table: "GuildSieges",
                columns: new[] { "DungeonId", "Week" },
                unique: true,
                filter: "\"DeletedOn\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Alliances_Guilds_LeadGuildId",
                table: "Alliances",
                column: "LeadGuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Dungeons_Guilds_OwnerGuildId",
                table: "Dungeons",
                column: "OwnerGuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Dungeons_Guilds_RaidGuildId",
                table: "Dungeons",
                column: "RaidGuildId",
                principalTable: "Guilds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Guilds_Alliances_AllianceId",
                table: "Guilds",
                column: "AllianceId",
                principalTable: "Alliances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Guilds_Characters_LeaderId",
                table: "Guilds",
                column: "LeaderId",
                principalTable: "Characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Guilds_Dungeons_DungeonId",
                table: "Guilds",
                column: "DungeonId",
                principalTable: "Dungeons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alliances_Guilds_LeadGuildId",
                table: "Alliances");

            migrationBuilder.DropForeignKey(
                name: "FK_Dungeons_Guilds_OwnerGuildId",
                table: "Dungeons");

            migrationBuilder.DropForeignKey(
                name: "FK_Dungeons_Guilds_RaidGuildId",
                table: "Dungeons");

            migrationBuilder.DropForeignKey(
                name: "FK_Guilds_Alliances_AllianceId",
                table: "Guilds");

            migrationBuilder.DropForeignKey(
                name: "FK_Guilds_Characters_LeaderId",
                table: "Guilds");

            migrationBuilder.DropForeignKey(
                name: "FK_Guilds_Dungeons_DungeonId",
                table: "Guilds");

            migrationBuilder.DropTable(
                name: "GuildRaids");

            migrationBuilder.DropTable(
                name: "GuildSiegeParticipants");

            migrationBuilder.DropTable(
                name: "GuildSieges");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_AllianceId",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_DungeonId",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_LeaderId",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Guilds_NormalizedName",
                table: "Guilds");

            migrationBuilder.DropIndex(
                name: "IX_Dungeons_OwnerGuildId",
                table: "Dungeons");

            migrationBuilder.DropIndex(
                name: "IX_Alliances_LeadGuildId",
                table: "Alliances");

            migrationBuilder.DropIndex(
                name: "IX_Alliances_NormalizedName",
                table: "Alliances");

            migrationBuilder.DropColumn(
                name: "LeaderId",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Guilds");

            migrationBuilder.DropColumn(
                name: "GuildMemo",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "GuildPermission",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Alliances");

            migrationBuilder.AlterColumn<long>(
                name: "DungeonId",
                table: "Guilds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AllianceId",
                table: "Guilds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_AllianceId",
                table: "Guilds",
                column: "AllianceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_DungeonId",
                table: "Guilds",
                column: "DungeonId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Dungeons_Guilds_RaidGuildId",
                table: "Dungeons",
                column: "RaidGuildId",
                principalTable: "Guilds",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Guilds_Alliances_AllianceId",
                table: "Guilds",
                column: "AllianceId",
                principalTable: "Alliances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Guilds_Dungeons_DungeonId",
                table: "Guilds",
                column: "DungeonId",
                principalTable: "Dungeons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
