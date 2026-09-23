using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// The merchant catalogue and the refusal policy that guards the trade window: a market this server
/// cannot resolve is <b>not</b> opened with an empty window, it is logged and nothing is sent.
/// </summary>
[TestFixture]
public class MarketCatalogTests
{
    private static MarketResourceRow Row(string name, int sortId, int code, long price,
        int huntaholicPoint = 0) =>
        new()
        {
            Name = name,
            SortId = sortId,
            Code = code,
            Price = price,
            HuntaholicPoint = huntaholicPoint
        };

    private static MarketCatalogOptions Options(params MarketResourceRow[] rows) =>
        new() { Markets = new List<MarketResourceRow>(rows) };

    [Test]
    public void TheCatalogGroupsTheRowsByMarketName()
    {
        var catalog = new MarketCatalog(Options(
            Row("deva_weapon", 1, 101, 5_000L),
            Row("deva_armor", 1, 201, 9_000L),
            Row("deva_weapon", 2, 102, 6_000L)));

        catalog.MarketCount.Should().Be(2);
        catalog.TryGetMarket("deva_weapon", out var weapon).Should().BeTrue();
        weapon.Should().HaveCount(2);
        weapon[0].Should().Be(new MarketLine(101, 5_000L, 0));
        weapon[1].Should().Be(new MarketLine(102, 6_000L, 0));

        catalog.TryGetMarket("deva_armor", out var armor).Should().BeTrue();
        armor.Should().ContainSingle().Which.Should().Be(new MarketLine(201, 9_000L, 0));
    }

    [Test]
    public void TheCatalogOrdersTheLinesBySortId()
    {
        var catalog = new MarketCatalog(Options(
            Row("deva_weapon", 30, 103, 7_000L),
            Row("deva_weapon", 10, 101, 5_000L),
            Row("deva_weapon", 20, 102, 6_000L)));

        catalog.TryGetMarket("deva_weapon", out var lines).Should().BeTrue();
        lines.Select(line => line.Code).Should().ContainInOrder(101, 102, 103);
    }

    [Test]
    public void TheCatalogKeepsTheFileOrderOfEqualSortIds()
    {
        var catalog = new MarketCatalog(Options(
            Row("deva_weapon", 0, 101, 5_000L),
            Row("deva_weapon", 0, 102, 6_000L)));

        catalog.TryGetMarket("deva_weapon", out var lines).Should().BeTrue();
        lines.Select(line => line.Code).Should().ContainInOrder(101, 102);
    }

    [Test]
    public void TheCatalogDropsRowsWithoutAnItemCodeOrAName()
    {
        var catalog = new MarketCatalog(Options(
            Row("deva_weapon", 1, 0, 5_000L),
            Row("", 1, 101, 5_000L),
            Row("deva_weapon", 2, 102, 6_000L)));

        catalog.MarketCount.Should().Be(1);
        catalog.TryGetMarket("deva_weapon", out var lines).Should().BeTrue();
        lines.Should().ContainSingle().Which.Code.Should().Be(102);
        catalog.TryGetMarket("", out _).Should().BeFalse();
    }

    [Test]
    public void AnUnknownOrEmptyMarketIsNotResolved()
    {
        var catalog = new MarketCatalog(Options(Row("deva_weapon", 1, 101, 5_000L)));

        catalog.TryGetMarket("deva_armor", out var lines).Should().BeFalse();
        lines.Should().BeEmpty();
        catalog.TryGetMarket("", out _).Should().BeFalse();
        catalog.TryGetMarket("DEVA_WEAPON", out _).Should().BeFalse("the reference compares names as stored");
    }

    [Test]
    public void AnEmptyCatalogResolvesNothing()
    {
        var catalog = new MarketCatalog(Options());

        catalog.MarketCount.Should().Be(0);
        catalog.TryGetMarket("deva_weapon", out _).Should().BeFalse();
    }

    [Test]
    public void TheShippedCatalogHoldsNoMarket()
    {
        // The file is delivered with zero rows: the reference's MarketResource table has no export here,
        // and the NPC to market link is not established either (reserve "A VERIFIER PAR KILLIAN").
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "market-catalog.73.json")));
        var options = document.RootElement.GetProperty("MarketCatalog")
            .Deserialize<MarketCatalogOptions>();

        options.Should().NotBeNull();
        options!.Markets.Should().BeEmpty();
        new MarketCatalog(options).MarketCount.Should().Be(0);
    }

    [Test]
    public void Open_SendsTheMarketOfTheSelectedNpc()
    {
        var (client, connection) = Client();
        var catalog = new MarketCatalog(Options(
            Row("deva_weapon", 1, 101, 5_000L),
            Row("deva_weapon", 2, 102, 6_000L, 7)));
        var service = new MarketService(catalog);

        service.Open(client, 0x80000123u, "deva_weapon");

        connection.Sent.Should().ContainSingle();
        var packet = connection.Sent[0];
        packet.Length.Should().Be(45);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_MARKET);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(13, 4)).Should().Be(101);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(17, 8)).Should().Be(5_000L);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(29, 4)).Should().Be(102);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(41, 4)).Should().Be(7);
    }

    [Test]
    public void Open_RefusesATruncatedMarketNameWithoutSendingAnything()
    {
        // Every merchant entry of npc-dialogs.73.json is the truncated "open_market(": the name was
        // concatenated in the client's Lua and is gone, so the catalogue cannot be chosen.
        var (client, connection) = Client();
        var service = new MarketService(new MarketCatalog(Options(Row("deva_weapon", 1, 101, 1L))));

        service.Open(client, 0x80000123u, string.Empty);

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Open_RefusesAnUnknownMarketWithoutSendingAnything()
    {
        var (client, connection) = Client();
        var service = new MarketService(new MarketCatalog(Options(Row("deva_weapon", 1, 101, 1L))));

        service.Open(client, 0x80000123u, "deva_armor");

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Open_RefusesAHandlelessDialogWithoutSendingAnything()
    {
        var (client, connection) = Client();
        var service = new MarketService(new MarketCatalog(Options(Row("deva_weapon", 1, 101, 1L))));

        service.Open(client, 0u, "deva_weapon");

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Open_RefusesAZeroLineMarketWithoutSendingAnything()
    {
        // A size-13 TM_SC_MARKET (n = 0) has no known producer: the client is never sent an empty
        // window, not even when a catalogue claims the market with no line.
        var (client, connection) = Client();
        var service = new MarketService(new EmptyMarketCatalog());

        service.Open(client, 0x80000123u, "deva_weapon");

        connection.Sent.Should().BeEmpty();
    }

    /// <summary>
    /// A game client whose only usable part is its connection: the aggro state machine tests use the
    /// same <see cref="RuntimeHelpers.GetUninitializedObject"/> trick to avoid the socket constructor.
    /// </summary>
    private static (GameClient Client, RecordingConnection Connection) Client()
    {
        var client = (GameClient)RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
        var connection = new RecordingConnection();
        client.Connection = connection;
        return (client, connection);
    }

    /// <summary>Records what the server would have written to the socket.</summary>
    private sealed class RecordingConnection : Connection
    {
        public List<byte[]> Sent { get; } = new();

        public RecordingConnection()
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
        }

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }

    /// <summary>A catalogue that claims every market and holds no line at all.</summary>
    private sealed class EmptyMarketCatalog : IMarketCatalog
    {
        public int MarketCount => 1;

        public bool TryGetMarket(string name, out IReadOnlyList<MarketLine> lines)
        {
            lines = Array.Empty<MarketLine>();
            return true;
        }
    }
}
