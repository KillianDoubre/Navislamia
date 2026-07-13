using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Navislamia.Game.DataAccess.Migrations.Telecaster
{
    /// <inheritdoc />
    public partial class Version0006_ClientInfoKeyBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Characters\" ALTER COLUMN \"ClientInfo\" TYPE text " +
                "USING array_to_string(\"ClientInfo\", '|');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Characters\" ALTER COLUMN \"ClientInfo\" TYPE text[] " +
                "USING string_to_array(\"ClientInfo\", '|');");
        }
    }
}
