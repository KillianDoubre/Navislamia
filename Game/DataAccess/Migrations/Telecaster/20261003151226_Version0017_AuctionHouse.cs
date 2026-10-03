using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0017_AuctionHouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionKeepings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: true),
                    Gold = table.Column<long>(type: "bigint", nullable: false),
                    KeepingType = table.Column<int>(type: "integer", nullable: false),
                    RelatedAuctionId = table.Column<long>(type: "bigint", nullable: false),
                    RelatedItemCode = table.Column<int>(type: "integer", nullable: false),
                    RelatedItemEnhance = table.Column<int>(type: "integer", nullable: false),
                    RelatedItemLevel = table.Column<int>(type: "integer", nullable: false),
                    ExpireTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionKeepings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuctionListings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    SellerId = table.Column<long>(type: "bigint", nullable: false),
                    SellerName = table.Column<string>(type: "text", nullable: true),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartPrice = table.Column<long>(type: "bigint", nullable: false),
                    InstantPurchasePrice = table.Column<long>(type: "bigint", nullable: false),
                    RegistrationTax = table.Column<long>(type: "bigint", nullable: false),
                    HighestBiddingPrice = table.Column<long>(type: "bigint", nullable: false),
                    HighestBidderId = table.Column<long>(type: "bigint", nullable: false),
                    HighestBidderName = table.Column<string>(type: "text", nullable: true),
                    BidderIds = table.Column<long[]>(type: "bigint[]", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionListings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionKeepings_OwnerId",
                table: "AuctionKeepings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionListings_SellerId",
                table: "AuctionListings",
                column: "SellerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionKeepings");

            migrationBuilder.DropTable(
                name: "AuctionListings");
        }
    }
}
