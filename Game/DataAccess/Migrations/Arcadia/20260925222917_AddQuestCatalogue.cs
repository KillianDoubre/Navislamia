using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddQuestCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestLinkResources",
                columns: table => new
                {
                    NpcId = table.Column<int>(type: "integer", nullable: false),
                    QuestId = table.Column<int>(type: "integer", nullable: false),
                    FlagStart = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    FlagProgress = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    FlagEnd = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    TextIdStart = table.Column<int>(type: "integer", nullable: false),
                    TextIdInProgress = table.Column<int>(type: "integer", nullable: false),
                    TextIdEnd = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestLinkResources", x => new { x.NpcId, x.QuestId });
                });

            migrationBuilder.CreateTable(
                name: "QuestResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TextIdQuest = table.Column<int>(type: "integer", nullable: false),
                    TextIdSummary = table.Column<int>(type: "integer", nullable: false),
                    TextIdStatus = table.Column<int>(type: "integer", nullable: false),
                    LimitBeginTime = table.Column<int>(type: "integer", nullable: false),
                    LimitEndTime = table.Column<int>(type: "integer", nullable: false),
                    LimitLevel = table.Column<int>(type: "integer", nullable: false),
                    LimitJobLevel = table.Column<int>(type: "integer", nullable: false),
                    LimitMaxLevel = table.Column<int>(type: "integer", nullable: false),
                    LimitMaxJobLevel = table.Column<int>(type: "integer", nullable: false),
                    LimitDeva = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitAsura = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitGaia = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitFighter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitHunter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitMagician = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitSummoner = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    LimitJob = table.Column<int>(type: "integer", nullable: false),
                    LimitJobDepth = table.Column<short>(type: "smallint", nullable: false),
                    LimitFavorGroupId = table.Column<int>(type: "integer", nullable: false),
                    LimitFavor = table.Column<int>(type: "integer", nullable: false),
                    Repeatable = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    InvokeCondition = table.Column<int>(type: "integer", nullable: false),
                    InvokeValue = table.Column<int>(type: "integer", nullable: false),
                    TimeLimitType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TimeLimit = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value1 = table.Column<int>(type: "integer", nullable: false),
                    Value2 = table.Column<int>(type: "integer", nullable: false),
                    Value3 = table.Column<int>(type: "integer", nullable: false),
                    Value4 = table.Column<int>(type: "integer", nullable: false),
                    Value5 = table.Column<int>(type: "integer", nullable: false),
                    Value6 = table.Column<int>(type: "integer", nullable: false),
                    Value7 = table.Column<int>(type: "integer", nullable: false),
                    Value8 = table.Column<int>(type: "integer", nullable: false),
                    Value9 = table.Column<int>(type: "integer", nullable: false),
                    Value10 = table.Column<int>(type: "integer", nullable: false),
                    Value11 = table.Column<int>(type: "integer", nullable: false),
                    Value12 = table.Column<int>(type: "integer", nullable: false),
                    DropGroupId = table.Column<int>(type: "integer", nullable: false),
                    QuestDifficulty = table.Column<int>(type: "integer", nullable: false),
                    FavorGroupId = table.Column<int>(type: "integer", nullable: false),
                    HateGroupId = table.Column<int>(type: "integer", nullable: false),
                    Favor = table.Column<int>(type: "integer", nullable: false),
                    Exp = table.Column<long>(type: "bigint", nullable: false),
                    Jp = table.Column<int>(type: "integer", nullable: false),
                    HolicPoint = table.Column<int>(type: "integer", nullable: false),
                    Ld = table.Column<int>(type: "integer", nullable: false),
                    DefaultRewardId = table.Column<int>(type: "integer", nullable: false),
                    DefaultRewardLevel = table.Column<int>(type: "integer", nullable: false),
                    DefaultRewardQuantity = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId1 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel1 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity1 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId2 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel2 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity2 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId3 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel3 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity3 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId4 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel4 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity4 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId5 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel5 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity5 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardId6 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardLevel6 = table.Column<int>(type: "integer", nullable: false),
                    OptionalRewardQuantity6 = table.Column<int>(type: "integer", nullable: false),
                    ForeQuest1 = table.Column<int>(type: "integer", nullable: false),
                    ForeQuest2 = table.Column<int>(type: "integer", nullable: false),
                    ForeQuest3 = table.Column<int>(type: "integer", nullable: false),
                    OrFlag = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    IsAutoQuest = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    ScriptStartText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ScriptEndText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ScriptDropText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ShowTargetType = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    ShowTargetId = table.Column<int>(type: "integer", nullable: true),
                    MarkHide = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    CoolTime = table.Column<int>(type: "integer", nullable: false),
                    AcceptCoolTime = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestResources", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestLinkResources");

            migrationBuilder.DropTable(
                name: "QuestResources");
        }
    }
}
