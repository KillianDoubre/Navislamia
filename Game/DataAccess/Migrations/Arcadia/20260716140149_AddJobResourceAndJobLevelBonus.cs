using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddJobResourceAndJobLevelBonus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobLevelBonuses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Strength = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Vitality = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Dexterity = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Agility = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Intelligence = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Wisdom = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    Luck = table.Column<decimal[]>(type: "numeric[]", nullable: true),
                    DefaultStrength = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultVitality = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultDexterity = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultAgility = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultIntelligence = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultWisdom = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultLuck = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLevelBonuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StatId = table.Column<int>(type: "integer", nullable: false),
                    JobClass = table.Column<int>(type: "integer", nullable: false),
                    JobDepth = table.Column<short>(type: "smallint", nullable: false),
                    UpLv = table.Column<short>(type: "smallint", nullable: false),
                    UpJlv = table.Column<short>(type: "smallint", nullable: false),
                    AvailableJobs = table.Column<short[]>(type: "smallint[]", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobResources", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobLevelBonuses");

            migrationBuilder.DropTable(
                name: "JobResources");
        }
    }
}
