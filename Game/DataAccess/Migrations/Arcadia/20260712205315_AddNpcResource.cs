using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddNpcResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NpcResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TextId = table.Column<int>(type: "integer", nullable: false),
                    NameId = table.Column<int>(type: "integer", nullable: false),
                    RaceId = table.Column<int>(type: "integer", nullable: false),
                    SexualId = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    Z = table.Column<int>(type: "integer", nullable: false),
                    Face = table.Column<int>(type: "integer", nullable: false),
                    LocalFlag = table.Column<int>(type: "integer", nullable: false),
                    IsPeriodic = table.Column<bool>(type: "boolean", nullable: false),
                    BeginOfPeriod = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndOfPeriod = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FaceX = table.Column<int>(type: "integer", nullable: false),
                    FaceY = table.Column<int>(type: "integer", nullable: false),
                    FaceZ = table.Column<int>(type: "integer", nullable: false),
                    ModelFile = table.Column<string>(type: "text", nullable: true),
                    HairId = table.Column<int>(type: "integer", nullable: false),
                    FaceId = table.Column<int>(type: "integer", nullable: false),
                    BodyId = table.Column<int>(type: "integer", nullable: false),
                    WeaponItemId = table.Column<int>(type: "integer", nullable: false),
                    ShieldItemId = table.Column<int>(type: "integer", nullable: false),
                    ClothesItemId = table.Column<int>(type: "integer", nullable: false),
                    HelmItemId = table.Column<int>(type: "integer", nullable: false),
                    GlovesItemId = table.Column<int>(type: "integer", nullable: false),
                    BootsItemId = table.Column<int>(type: "integer", nullable: false),
                    BeltItemId = table.Column<int>(type: "integer", nullable: false),
                    MantleItemId = table.Column<int>(type: "integer", nullable: false),
                    NecklaceItemId = table.Column<int>(type: "integer", nullable: false),
                    EarringItemId = table.Column<int>(type: "integer", nullable: false),
                    Ring1ItemId = table.Column<int>(type: "integer", nullable: false),
                    Ring2ItemId = table.Column<int>(type: "integer", nullable: false),
                    MotionId = table.Column<int>(type: "integer", nullable: false),
                    IsRoam = table.Column<int>(type: "integer", nullable: false),
                    RoamingId = table.Column<int>(type: "integer", nullable: false),
                    StandardWalkSpeed = table.Column<int>(type: "integer", nullable: false),
                    StandardRunSpeed = table.Column<int>(type: "integer", nullable: false),
                    WalkSpeed = table.Column<int>(type: "integer", nullable: false),
                    RunSpeed = table.Column<int>(type: "integer", nullable: false),
                    Attackable = table.Column<int>(type: "integer", nullable: false),
                    OffensiveType = table.Column<int>(type: "integer", nullable: false),
                    SpawnType = table.Column<int>(type: "integer", nullable: false),
                    ChaseRange = table.Column<int>(type: "integer", nullable: false),
                    RegenTime = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    StatId = table.Column<int>(type: "integer", nullable: false),
                    AttackRange = table.Column<int>(type: "integer", nullable: false),
                    AttackSpeedType = table.Column<int>(type: "integer", nullable: false),
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
                    AiScript = table.Column<string>(type: "text", nullable: true),
                    ContactScript = table.Column<string>(type: "text", nullable: true),
                    TextureGroup = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpcResources", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NpcResources");
        }
    }
}
