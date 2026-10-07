using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// TM_CS_REQUEST_FARM_MARKET (6008), official onRequestFarmMarket (GameMessage.cpp:11943-11952): the farm window's
/// shop button answers with the TM_SC_MARKET (250) of the creature_farm market, npc_handle 0, and that market becomes
/// the one a 251 buys from (SendMarketInfo's SetLastContactMarket).
/// </summary>
[TestFixture]
public class CreatureFarmMarketTests
{
    [Test]
    public void OpenWithoutNpc_SendsTheMarketWithAZeroNpcHandle()
    {
        var catalog = new MarketCatalog(new MarketCatalogOptions
        {
            Markets = new List<MarketResourceRow>
            {
                new() { Name = "creature_farm", SortId = 1, Code = 710005, Price = 10_000 },
                new() { Name = "creature_farm", SortId = 2, Code = 2902161, Price = 500 },
            }
        });
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);

        new MarketService(catalog).OpenWithoutNpc(client, "creature_farm").Should().BeTrue();

        var frame = connection.Sent.Should().ContainSingle().Subject;
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_MARKET);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(0u, "no NPC sells the farm's market");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11, 2)).Should().Be(2);
        frame.Length.Should().Be(13 + 16 * 2);
    }

    [Test]
    public void OpenWithoutNpc_SendsNothingForAnUnknownMarket()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);

        new MarketService(new MarketCatalog(new MarketCatalogOptions())).OpenWithoutNpc(client, "creature_farm")
            .Should().BeFalse();
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void TheShopButton_OpensTheCreatureFarmMarketAndRemembersIt()
    {
        var markets = A.Fake<IMarketService>();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Farmer";
        A.CallTo(() => markets.OpenWithoutNpc(client, "creature_farm")).Returns(true);

        new CreatureFarmService(A.Fake<ICreatureFarmStore>(), markets: markets).OpenMarket(client).Should().BeTrue();

        info.OpenMarketName.Should().Be("creature_farm", "a 251 buys from the market SendMarketInfo announced");
    }

    [Test]
    public void TheShopButton_RemembersNothingWhenTheMarketIsUnknown()
    {
        var markets = A.Fake<IMarketService>();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Farmer";
        A.CallTo(() => markets.OpenWithoutNpc(client, "creature_farm")).Returns(false);

        new CreatureFarmService(A.Fake<ICreatureFarmStore>(), markets: markets).OpenMarket(client).Should().BeFalse();

        info.OpenMarketName.Should().BeEmpty();
    }
}
