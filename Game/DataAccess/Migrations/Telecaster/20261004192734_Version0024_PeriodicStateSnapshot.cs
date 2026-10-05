using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <summary>
    /// The periodic state snapshot (docs/packet-specs/socle-etats-periodiques-energie.md §6). Codex first shipped these two
    /// columns as Version0023_PeriodicStateSnapshot, which collided with Version0023_CharacterFriends and was regenerated
    /// here; a database that already ran the first one has the columns and its history row, so both steps tolerate it.
    /// </summary>
    public partial class Version0024_PeriodicStateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"CharacterStates\" ADD COLUMN IF NOT EXISTS \"PeriodicBaseDamage\" integer NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE \"CharacterStates\" ADD COLUMN IF NOT EXISTS \"RemainingFireTicks\" integer NULL;");
            migrationBuilder.Sql(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261004190536_Version0023_PeriodicStateSnapshot';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"CharacterStates\" DROP COLUMN IF EXISTS \"PeriodicBaseDamage\";");
            migrationBuilder.Sql("ALTER TABLE \"CharacterStates\" DROP COLUMN IF EXISTS \"RemainingFireTicks\";");
        }
    }
}
