using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0019_AutomaticAuctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoAuctionResourceId",
                table: "AuctionListings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecrouteOnly",
                table: "AuctionListings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AutoAuctionRegistrations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResourceId = table.Column<int>(type: "integer", nullable: false),
                    LastRegisteredTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoAuctionRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoAuctionRegistrations_ResourceId",
                table: "AutoAuctionRegistrations",
                column: "ResourceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoAuctionRegistrations");

            migrationBuilder.DropColumn(
                name: "AutoAuctionResourceId",
                table: "AuctionListings");

            migrationBuilder.DropColumn(
                name: "SecrouteOnly",
                table: "AuctionListings");
        }
    }
}
