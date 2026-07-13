using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddMonsterResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonsterResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonsterGroup = table.Column<int>(type: "integer", nullable: false),
                    NameId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    MotionFileId = table.Column<int>(type: "integer", nullable: false),
                    TransformLevel = table.Column<int>(type: "integer", nullable: false),
                    WalkType = table.Column<int>(type: "integer", nullable: false),
                    SlantType = table.Column<int>(type: "integer", nullable: false),
                    Size = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Scale = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TargetFxSize = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CameraX = table.Column<int>(type: "integer", nullable: false),
                    CameraY = table.Column<int>(type: "integer", nullable: false),
                    CameraZ = table.Column<int>(type: "integer", nullable: false),
                    TargetX = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TargetY = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TargetZ = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Grp = table.Column<int>(type: "integer", nullable: false),
                    Affiliation = table.Column<int>(type: "integer", nullable: false),
                    SpeciesId = table.Column<int>(type: "integer", nullable: false),
                    MagicType = table.Column<int>(type: "integer", nullable: false),
                    Race = table.Column<int>(type: "integer", nullable: false),
                    VisibleRange = table.Column<int>(type: "integer", nullable: false),
                    ChaseRange = table.Column<int>(type: "integer", nullable: false),
                    FirstAttack = table.Column<int>(type: "integer", nullable: false),
                    GroupFirstAttack = table.Column<int>(type: "integer", nullable: false),
                    ResponseCasting = table.Column<int>(type: "integer", nullable: false),
                    ResponseRace = table.Column<int>(type: "integer", nullable: false),
                    ResponseBattle = table.Column<int>(type: "integer", nullable: false),
                    MonsterType = table.Column<int>(type: "integer", nullable: false),
                    MonsterGradeIcon = table.Column<int>(type: "integer", nullable: false),
                    StatId = table.Column<int>(type: "integer", nullable: false),
                    FightType = table.Column<int>(type: "integer", nullable: false),
                    MonsterSkillLinkId = table.Column<int>(type: "integer", nullable: false),
                    Material = table.Column<int>(type: "integer", nullable: false),
                    WeaponType = table.Column<int>(type: "integer", nullable: false),
                    AttackMotionSpeed = table.Column<int>(type: "integer", nullable: false),
                    Ability = table.Column<int>(type: "integer", nullable: false),
                    StandardWalkSpeed = table.Column<int>(type: "integer", nullable: false),
                    StandardRunSpeed = table.Column<int>(type: "integer", nullable: false),
                    WalkSpeed = table.Column<int>(type: "integer", nullable: false),
                    RunSpeed = table.Column<int>(type: "integer", nullable: false),
                    AttackRange = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    HidesenseRange = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Hp = table.Column<int>(type: "integer", nullable: false),
                    Mp = table.Column<int>(type: "integer", nullable: false),
                    AttackPoint = table.Column<int>(type: "integer", nullable: false),
                    MagicPoint = table.Column<int>(type: "integer", nullable: false),
                    Defence = table.Column<int>(type: "integer", nullable: false),
                    MagicDefence = table.Column<int>(type: "integer", nullable: false),
                    AttackSpeed = table.Column<int>(type: "integer", nullable: false),
                    MagicSpeed = table.Column<int>(type: "integer", nullable: false),
                    Accuracy = table.Column<int>(type: "integer", nullable: false),
                    Avoid = table.Column<int>(type: "integer", nullable: false),
                    MagicAccuracy = table.Column<int>(type: "integer", nullable: false),
                    MagicAvoid = table.Column<int>(type: "integer", nullable: false),
                    TamingId = table.Column<int>(type: "integer", nullable: false),
                    CreatureTamingCode = table.Column<int>(type: "integer", nullable: false),
                    TamingPercentage = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    TamingExpMod = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Exp = table.Column<int>(type: "integer", nullable: false),
                    Jp = table.Column<int>(type: "integer", nullable: false),
                    GoldDropPercentage = table.Column<int>(type: "integer", nullable: false),
                    GoldMin = table.Column<int>(type: "integer", nullable: false),
                    GoldMax = table.Column<int>(type: "integer", nullable: false),
                    ChaosDropPercentage = table.Column<int>(type: "integer", nullable: false),
                    ChaosMin = table.Column<int>(type: "integer", nullable: false),
                    ChaosMax = table.Column<int>(type: "integer", nullable: false),
                    Exp2 = table.Column<int>(type: "integer", nullable: false),
                    Jp2 = table.Column<int>(type: "integer", nullable: false),
                    GoldMin2 = table.Column<int>(type: "integer", nullable: false),
                    GoldMax2 = table.Column<int>(type: "integer", nullable: false),
                    ChaosMin2 = table.Column<int>(type: "integer", nullable: false),
                    ChaosMax2 = table.Column<int>(type: "integer", nullable: false),
                    DropTableLinkId = table.Column<int>(type: "integer", nullable: false),
                    TextureGroup = table.Column<int>(type: "integer", nullable: false),
                    LocalFlag = table.Column<int>(type: "integer", nullable: false),
                    ScriptOnDead = table.Column<string>(type: "text", nullable: true),
                    RespawnGroup = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterResources", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonsterResources");
        }
    }
}
