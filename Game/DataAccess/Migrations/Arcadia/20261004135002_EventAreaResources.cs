using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Arcadia
{
    /// <inheritdoc />
    public partial class EventAreaResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventAreaResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BeginTime = table.Column<int>(type: "integer", nullable: false),
                    EndTime = table.Column<int>(type: "integer", nullable: false),
                    MinLevel = table.Column<int>(type: "integer", nullable: false),
                    MaxLevel = table.Column<int>(type: "integer", nullable: false),
                    RaceJobLimit = table.Column<long>(type: "bigint", nullable: false),
                    Conditions = table.Column<int[]>(type: "integer[]", nullable: true),
                    Values = table.Column<int[]>(type: "integer[]", nullable: true),
                    CountLimit = table.Column<int>(type: "integer", nullable: false),
                    EnterHandler = table.Column<string>(type: "text", nullable: true),
                    LeaveHandler = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAreaResources", x => x.Id);
                });
            using var seed = typeof(EventAreaResources).Assembly.GetManifestResourceStream("Navislamia.EventAreaResources.sql");
            using var reader = new System.IO.StreamReader(seed);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventAreaResources");
        }
    }
}
