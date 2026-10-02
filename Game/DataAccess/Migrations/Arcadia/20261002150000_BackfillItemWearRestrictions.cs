using System.IO;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Navislamia.Game.DataAccess.Contexts;

namespace Navislamia.Game.DataAccess.Migrations.Arcadia;

/// <summary>Fill existing whitelist columns from the Epic 7 limit_* fields, without changing the schema.</summary>
[DbContext(typeof(ArcadiaContext))]
[Migration("20261002150000_BackfillItemWearRestrictions")]
public class BackfillItemWearRestrictions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var source = typeof(BackfillItemWearRestrictions).Assembly
            .GetManifestResourceStream("Navislamia.ItemWearRestrictions.sql")
            ?? throw new InvalidDataException("Missing embedded item wear restrictions");
        using var reader = new StreamReader(source);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    // This corrects imported data; reverting cannot reconstruct the previous per-item values.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
