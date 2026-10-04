using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <summary>
    /// A marker: the items made before the ethereal wear existed hold 0, which now reads as exhausted. Their maxima live in
    /// Arcadia, which a Telecaster migration cannot read, so <see cref="Navislamia.Game.Services.EtherealDurabilityBackfill"/>
    /// fills them right after this migration is applied, once.
    /// </summary>
    public partial class Version0021_EtherealDurabilityBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
