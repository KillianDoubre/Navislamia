using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Rates;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class GroundItemVisibilityTests
{
    [Test]
    public async Task Drop_IsShownToNearbyPlayerAndRemovedFromEveryViewWhenTaken()
    {
        var characters = A.Fake<ICharacterService>();
        var players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        var ground = new GroundItemService(A.Fake<IMonsterDropCatalog>(), characters,
            A.Fake<IItemGroupCatalog>(), rates, players);
        var owner = NewPlayer(1, "owner", 1000);
        var peer = NewPlayer(2, "peer", 1100);
        players.Registry.Register(1, owner);
        players.Registry.Register(2, peer);
        A.CallTo(() => characters.RemoveItemAsync("owner", 7, A<Func<ItemEntity, long>>._))
            .Returns(new ItemRemoval(new ItemEntity { Id = 7, ItemResourceId = 603002, Amount = 1 }, 1));

        await ground.DropFromInventoryAsync(owner, 7, 1);

        var ownerEnter = Sent(owner).Single(frame => IsItemEnter(frame));
        var peerEnter = Sent(peer).Single(frame => IsItemEnter(frame));
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(ownerEnter.AsSpan(8, 4));
        BinaryPrimitives.ReadUInt32LittleEndian(peerEnter.AsSpan(8, 4)).Should().Be(handle);

        Sent(peer).Clear();
        StorageTestHarness.Session(peer).X = 5000;
        ground.Sync(peer);
        Sent(peer).Should().ContainSingle(frame => IsLeave(frame, handle));

        Sent(peer).Clear();
        StorageTestHarness.Session(peer).X = 1100;
        ground.Sync(peer);
        Sent(peer).Should().ContainSingle(frame => IsItemEnter(frame));

        Sent(owner).Clear();
        Sent(peer).Clear();
        A.CallTo(() => characters.AddItemAsync("owner", 603002, 1))
            .Returns(new ItemEntity { Id = 8, ItemResourceId = 603002, Amount = 1 });
        await ground.TakeAsync(owner, handle);
        Sent(owner).Should().Contain(frame => IsLeave(frame, handle));
        Sent(peer).Should().ContainSingle(frame => IsLeave(frame, handle));
    }

    private static GameClient NewPlayer(uint handle, string name, float x)
    {
        var client = StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.X = x;
        info.Y = 1000;
        return client;
    }

    private static System.Collections.Generic.List<byte[]> Sent(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static bool IsItemEnter(byte[] frame) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_ENTER
        && frame[7] == 2 && frame[25] == 2;

    private static bool IsLeave(byte[] frame, uint handle) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_LEAVE
        && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)) == handle;
}
