using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Navislamia.Configuration.Options;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The shipped creature-farm ticket-cost table (tools/export_creature_farm_costs.py, from the 7.3 client's own
/// db_creaturefarm.rdb). The table is not on the wire: it is the line (rate, form, enhance_level) of the card
/// being deposited, and ValidateFarmTicket requires the offered tickets to equal it exactly, so a key the table
/// does not carry answers 0 and the deposition is refused
/// (docs/packet-specs/socle-cout-tickets-ferme.md §4, §5.4).
/// <para>
/// The source file is 420 bytes: a 128-byte header, a uint32 count of 72 at 0x80 and 72 four-byte records from
/// 0x84 — rate @0, form @1, enhance_level @2, ticket_count @3, the column order of the DDL and of the official
/// GameContent::GetCreatureFarmTicketCount. The rows below are read back from the shipped catalogue, which is
/// what the server loads; the .rdb itself is only ever read offline by the exporter.
/// </para>
/// </summary>
[TestFixture]
public class CreatureFarmTicketCostCatalogTests
{
    private const int Records = 72;
    private const int FieldsPerRecord = 4;

    /// <summary>The 72 rows of the 7.3 client table, in the order its file stores them.</summary>
    private static readonly (int Rate, int Form, int EnhanceLevel, int Tickets)[] ClientTable =
    {
        (0, 1, 0, 1), (0, 1, 1, 2), (0, 1, 2, 2), (0, 1, 3, 3), (0, 1, 4, 3), (0, 1, 5, 4),
        (0, 2, 0, 3), (0, 2, 1, 6), (0, 2, 2, 6), (0, 2, 3, 9), (0, 2, 4, 9), (0, 2, 5, 12),
        (1, 1, 0, 1), (1, 1, 1, 2), (1, 1, 2, 2), (1, 1, 3, 3), (1, 1, 4, 3), (1, 1, 5, 4),
        (1, 2, 0, 3), (1, 2, 1, 6), (1, 2, 2, 6), (1, 2, 3, 9), (1, 2, 4, 9), (1, 2, 5, 12),
        (2, 1, 0, 1), (2, 1, 1, 2), (2, 1, 2, 2), (2, 1, 3, 3), (2, 1, 4, 3), (2, 1, 5, 4),
        (2, 2, 0, 3), (2, 2, 1, 6), (2, 2, 2, 6), (2, 2, 3, 9), (2, 2, 4, 9), (2, 2, 5, 12),
        (3, 1, 0, 2), (3, 1, 1, 3), (3, 1, 2, 3), (3, 1, 3, 4), (3, 1, 4, 4), (3, 1, 5, 5),
        (3, 2, 0, 6), (3, 2, 1, 9), (3, 2, 2, 9), (3, 2, 3, 12), (3, 2, 4, 12), (3, 2, 5, 15),
        (4, 1, 0, 2), (4, 1, 1, 3), (4, 1, 2, 3), (4, 1, 3, 4), (4, 1, 4, 4), (4, 1, 5, 5),
        (4, 2, 0, 6), (4, 2, 1, 9), (4, 2, 2, 9), (4, 2, 3, 12), (4, 2, 4, 12), (4, 2, 5, 15),
        (5, 1, 0, 3), (5, 1, 1, 4), (5, 1, 2, 4), (5, 1, 3, 5), (5, 1, 4, 5), (5, 1, 5, 6),
        (5, 2, 0, 9), (5, 2, 1, 12), (5, 2, 2, 12), (5, 2, 3, 15), (5, 2, 4, 15), (5, 2, 5, 18),
    };

    /// <summary>Rows with four pairwise different values, so each field's position is proven on its own.</summary>
    private const int RateThreeFormOneEnhanceZero = 36;
    private const int RateThreeFormTwoEnhanceFive = 47;

    private static JsonElement ShippedRows()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "creature-farm-costs.73.json")));

        return document.RootElement.GetProperty("CreatureFarmTicketCosts").GetProperty("Rows").Clone();
    }

    /// <summary>The rows the server loads: read from the shipped file the way Program.ConfigureCreatureFarmTicketCosts reads it.</summary>
    private static List<CreatureFarmTicketCostRowOptions> Shipped()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "creature-farm-costs.73.json")));

        return document.RootElement.GetProperty("CreatureFarmTicketCosts")
            .Deserialize<CreatureFarmTicketCostOptions>()!.Rows;
    }

    /// <summary>The seam's contract: the tickets that key costs, 0 when the table carries no such key.</summary>
    private static int Lookup(IReadOnlyList<CreatureFarmTicketCostRowOptions> rows, int rate, int form, int enhanceLevel)
    {
        var row = rows.SingleOrDefault(row => row.Rate == rate && row.Form == form && row.EnhanceLevel == enhanceLevel);
        return row is null ? 0 : row.TicketCount;
    }

    [Test]
    public void TheShippedCatalogueIsTheSeventyTwoFourFieldRowsOfTheClientTable()
    {
        var rows = Shipped();

        rows.Should().HaveCount(Records, "the 7.3 client table holds 6 rates x 2 forms x 6 enhance levels");
        (rows.Count * FieldsPerRecord).Should().Be(288, "its body is 72 records of 4 fields (420 = 128 + 4 + 288 bytes)");
        rows.Should().OnlyContain(row => row.TicketCount >= 1,
            "0 is the answer reserved for a key the table does not carry");
        rows.Min(row => row.TicketCount).Should().Be(1);
        rows.Max(row => row.TicketCount).Should().Be(18);
    }

    [Test]
    public void EveryFieldOfARowSitsAtItsDocumentedPosition()
    {
        var rows = ShippedRows();

        foreach (var row in rows.EnumerateArray())
        {
            row.EnumerateObject().Select(field => field.Name).Should().Equal(
                new[] { "Rate", "Form", "EnhanceLevel", "TicketCount" },
                "the client writes rate, form, enhance_level then the ticket_count that key returns");
        }

        rows.GetArrayLength().Should().Be(Records);
        rows[RateThreeFormOneEnhanceZero].EnumerateObject().Select(field => field.Value.GetInt32())
            .Should().Equal(new[] { 3, 1, 0, 2 },
                "rate 3, form 1, enhance_level 0 cost 2 tickets: four pairwise different values");
        rows[RateThreeFormTwoEnhanceFive].EnumerateObject().Select(field => field.Value.GetInt32())
            .Should().Equal(new[] { 3, 2, 5, 15 }, "rate 3, form 2, enhance_level 5 cost 15 tickets");
    }

    [Test]
    public void TheKeysAreTheFullProductOfRateFormAndEnhanceLevelInStorageOrder()
    {
        var keys = Shipped().Select(row => (row.Rate, row.Form, row.EnhanceLevel)).ToList();

        keys.Should().OnlyHaveUniqueItems("the client table has no duplicated key");
        keys.Should().Equal(
            Enumerable.Range(0, 6).SelectMany(rate => Enumerable.Range(1, 2).SelectMany(form =>
                Enumerable.Range(0, 6).Select(enhanceLevel => (rate, form, enhanceLevel)))),
            "the 72 keys are 0-5 x 1-2 x 0-5, in the increasing order the client file stores them");
    }

    [Test]
    public void EveryKeyCostsWhatThe7_3ClientTableSays()
    {
        Shipped().Select(row => (row.Rate, row.Form, row.EnhanceLevel, row.TicketCount))
            .Should().Equal(ClientTable);
    }

    [Test]
    public void AKeyTheClientTableDoesNotCarryAnswersZero()
    {
        var rows = Shipped();

        rows.Should().NotContain(row => row.Form == 3,
            "neither copy of the 7.3 table prices a form-3 creature, so its 35 client invocations are refused");

        foreach (var enhanceLevel in Enumerable.Range(0, 6))
        {
            Lookup(rows, 0, 3, enhanceLevel).Should().Be(0,
                "a key with no row costs 0, which ValidateFarmTicket can never match: the deposition is refused");
        }

        Lookup(rows, 5, 2, 5).Should().Be(18, "a priced key answers the row it has");
    }

    [Test]
    public void AnAbsentCatalogueLeavesNoRowAndFailsNothing()
    {
        new CreatureFarmTicketCostOptions().Rows.Should().NotBeNull().And.BeEmpty(
            "the startup branch configures the options with their default value when the file is absent");

        Lookup(new CreatureFarmTicketCostOptions().Rows, 0, 1, 0).Should().Be(0,
            "with no file every key is unpriced, so every deposition is refused rather than accepted unpriced");
    }
}
