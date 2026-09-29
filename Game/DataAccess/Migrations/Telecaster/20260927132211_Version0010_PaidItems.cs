using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0010_PaidItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaidItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: true),
                    CharacterName = table.Column<string>(type: "text", nullable: true),
                    ItemCode = table.Column<int>(type: "integer", nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    RestItemCount = table.Column<int>(type: "integer", nullable: false),
                    BoughtTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ServerName = table.Column<string>(type: "text", nullable: true),
                    TakenCharacterId = table.Column<long>(type: "bigint", nullable: true),
                    TakenCharacterName = table.Column<string>(type: "text", nullable: true),
                    TakenServerName = table.Column<string>(type: "text", nullable: true),
                    TakenTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TakenAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Confirmed = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCancel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaidItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaidItems_AccountId_CharacterId",
                table: "PaidItems",
                columns: new[] { "AccountId", "CharacterId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaidItems");
        }
    }
}
