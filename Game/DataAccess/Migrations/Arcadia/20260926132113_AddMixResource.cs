using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class AddMixResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MixResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MixType = table.Column<int>(type: "integer", nullable: false),
                    MixValue01 = table.Column<int>(type: "integer", nullable: false),
                    MixValue02 = table.Column<int>(type: "integer", nullable: false),
                    MixValue03 = table.Column<int>(type: "integer", nullable: false),
                    MixValue04 = table.Column<int>(type: "integer", nullable: false),
                    MixValue05 = table.Column<int>(type: "integer", nullable: false),
                    MixValue06 = table.Column<int>(type: "integer", nullable: false),
                    SubMaterialCount = table.Column<int>(type: "integer", nullable: false),
                    MainType01 = table.Column<int>(type: "integer", nullable: false),
                    MainValue01 = table.Column<int>(type: "integer", nullable: false),
                    MainType02 = table.Column<int>(type: "integer", nullable: false),
                    MainValue02 = table.Column<int>(type: "integer", nullable: false),
                    MainType03 = table.Column<int>(type: "integer", nullable: false),
                    MainValue03 = table.Column<int>(type: "integer", nullable: false),
                    MainType04 = table.Column<int>(type: "integer", nullable: false),
                    MainValue04 = table.Column<int>(type: "integer", nullable: false),
                    MainType05 = table.Column<int>(type: "integer", nullable: false),
                    MainValue05 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub01Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub02Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub03Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub04Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub05Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub06Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub07Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub08Value05 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Type01 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Value01 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Type02 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Value02 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Type03 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Value03 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Type04 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Value04 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Type05 = table.Column<int>(type: "integer", nullable: false),
                    Sub09Value05 = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MixResources", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MixResources");
        }
    }
}
