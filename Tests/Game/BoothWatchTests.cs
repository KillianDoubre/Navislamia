using System;
using System.Buffers.Binary;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The behaviour of the visibility socle of the player booth, driven through the real receive loop so
/// that the enum, the dispatch arms and the handlers are exercised together
/// (docs/packet-specs/socle-booths-visibilite.md §5.2 and §5.3): a <c>702</c> on an open booth answers a
/// <c>703</c> whose items come from the owner's inventory, a <c>702</c> that names no servable booth is
/// refused with the code the lot tranched, and a <c>704</c> is idempotent.
///
/// NGemity implements none of this family (<c>Chihiro/src/Network/Messages.cpp:573-577</c> are five
/// commented lines), so the only authority is the 7.3 client: the refusals, the skipped record and the
/// purge are Navislamia decisions, listed in the packet sheet's "A VERIFIER" section.
/// </summary>
[TestFixture]
public class BoothWatchTests
{
    private const uint OwnerHandle = 0x80000001u;
    private const uint WatcherHandle = 0x40000001u;
    private const uint DeclaredHandle = 0x0A000001u;
    private const string OwnerName = "Owner";
    private const string WatcherName = "Watcher";

    [Test]
    public void WatchBooth_OnAnOpenBooth_AnswersSevenHundredThreeWithTheResolvedInventoryItem()
    {
        var resolved = new ItemEntity
        {
            Id = DeclaredHandle,
            ItemResourceId = 240100,
            Amount = 5,
            Endurance = 120,
            Enhance = 4,
            Level = 5,
            SocketItemIds = new long[] { 11, 12 }
        };

        var (watcher, connection, _) = Watch(
            declared: new[] { new BoothOpenItem(DeclaredHandle, Count: 3, Gold: 1500) },
            resolved: new[] { resolved });

        connection.Sent.Should().ContainSingle("the 702 is answered by one 703 and nothing else");

        var packet = connection.Sent[0];

        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_WATCH_BOOTH);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(97);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(OwnerHandle,
            "target is the handle the client asked to watch");
        packet[11].Should().Be(1, "type is the one the owner declared in its 700, copied verbatim");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(1, "count at +12");

        // The 75 bytes of the record are the inventory motif, not the declared triplet: the declared
        // frame carried only a handle, a count and a price — no code, no endurance and no socket.
        var expected = new byte[ItemFixedInfoWriter.Size];
        ItemFixedInfoWriter.Write(expected, ItemFixedInfo.FromItem(resolved));
        packet.AsSpan(14, ItemFixedInfoWriter.Size).ToArray().Should().Equal(expected,
            "the motif comes from ICharacterService.GetItemByHandleAsync");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(14 + 16, 8)).Should().Be(5,
            "the amount is the one the inventory holds, not the declared cnt of 3");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(14 + 75, 8)).Should().Be(1500,
            "the price is the declared one, verbatim — unit or total is not established (§7.7)");

        StorageTestHarness.Session(watcher).WatchedBoothHandle.Should().Be(OwnerHandle,
            "the observation is kept on the connection, under the booth lock regime");
    }

    [Test]
    public void WatchBooth_OnABoothWhoseDeclaredHandleNoLongerResolves_SkipsThatRecordOnly()
    {
        // §7.6: the object moved, was consumed or sold between the 700 and the 702. The lot skips the
        // record so count and length stay coherent, instead of failing the whole window.
        var kept = new ItemEntity { Id = DeclaredHandle, ItemResourceId = 240100, Amount = 1 };

        var (_, connection, _) = Watch(
            declared: new[]
            {
                new BoothOpenItem(0x0A00000Fu, Count: 1, Gold: 100),
                new BoothOpenItem(DeclaredHandle, Count: 1, Gold: 200)
            },
            resolved: new[] { kept });

        connection.Sent.Should().ContainSingle();

        var packet = connection.Sent[0];

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(97,
            "one record survived, so the frame is 14 + 83");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(1,
            "count drops with the skipped record");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(14 + 75, 8)).Should().Be(200,
            "the surviving record keeps its own declared price");
    }

    [Test]
    public void WatchBooth_OnAnEmptyBooth_AnswersAFourteenByteWindow()
    {
        var (_, connection, _) = Watch(declared: Array.Empty<BoothOpenItem>(), resolved: Array.Empty<ItemEntity>());

        connection.Sent.Should().ContainSingle();
        connection.Sent[0].Should().HaveCount(14, "a coherent but empty window, never a refusal");
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(12, 2)).Should().Be(0);
    }

    [Test]
    public void WatchBooth_OnAHandleNoOpenBoothServes_RefusesWithTheTranchedCode()
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, 0x80000099u));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;
        StorageTestHarness.Session(watcher).CharacterName = WatcherName;

        watcher.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();

        var result = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();

        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_WATCH_BOOTH);
        result.Result.Should().Be((ushort)ResultCode.NotExist,
            "the lot tranched NotExist (1) for a booth no session serves; no source names a code (§7.2)");
        result.Value.Should().Be(unchecked((int)0x80000099u), "the handle that did not resolve");
    }

    [Test]
    public void WatchBooth_OnAnEmptyBoothOfASessionWithoutABooth_IsRefusedTheSameWay()
    {
        // The session exists but declared no booth: its handle resolves to nothing servable.
        var characters = A.Fake<ICharacterService>();
        var owner = Owner(characters, declared: Array.Empty<BoothOpenItem>());
        StorageTestHarness.Session(owner).CloseBooth();

        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, OwnerHandle));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;
        StorageTestHarness.Network(watcher).AuthorizedGameClients.TryAdd("owner", owner);

        watcher.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();
        new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>().Result
            .Should().Be((ushort)ResultCode.NotExist, "a closed booth serves nothing");
    }

    [Test]
    public void WatchBooth_OnAFrameShorterThanElevenBytes_RefusesWithInvalidArgument()
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, OwnerHandle, length: 10));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;

        watcher.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();

        var result = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();

        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_WATCH_BOOTH);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void WatchBooth_OutsideTheWorld_IsDroppedWithoutAnAnswer()
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, OwnerHandle));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);

        watcher.OnDataReceived(connection.BytesAvailable);
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);

        connection.Sent.Should().BeEmpty("a session without a character has no booth to watch");
    }

    [Test]
    public void WatchBooth_WhileTheRequestersOwnBoothIsOpen_IsRefusedWithFiftyFive()
    {
        // smsg_booth_not_use_store: the client refuses "another store" while its own is open, and the
        // server gate now covers 702 as well (fiche §5.2 point 9).
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, OwnerHandle));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;
        StorageTestHarness.Session(watcher).OpenBooth(
            new StartBoothRequest(1, "Mine"u8.ToArray(), new[] { new BoothOpenItem(1, 1, 1) }));

        watcher.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();

        var result = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();

        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_WATCH_BOOTH);
        result.Result.Should().Be((ushort)ResultCode.NotActableWhileUsingBooth);
    }

    [Test]
    public void StopWatchBooth_IsIdempotentAndAnswersSuccessEveryTime()
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_STOP_WATCH_BOOTH, OwnerHandle));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;
        StorageTestHarness.Session(watcher).BeginWatchingBooth(OwnerHandle);

        watcher.OnDataReceived(connection.BytesAvailable);

        var first = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();

        first.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH);
        first.Result.Should().Be((ushort)ResultCode.Success);
        StorageTestHarness.Session(watcher).WatchedBoothHandle.Should().BeNull();

        // Same frame again: the answer is the same, the observation was already gone.
        var again = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_STOP_WATCH_BOOTH, OwnerHandle));
        var second = StorageTestHarness.NewGameClient(again, characterService: characters);
        StorageTestHarness.Session(second).CharacterHandle = WatcherHandle;

        second.OnDataReceived(again.BytesAvailable);

        again.Sent.Should().ContainSingle("a 704 without an observation is still answered Success");
        new Packet<TS_SC_RESULT>(again.Sent[0]).GetDataStruct<TS_SC_RESULT>().Result
            .Should().Be((ushort)ResultCode.Success);
    }

    [Test]
    public void StopWatchBooth_OnAFrameShorterThanElevenBytes_RefusesWithInvalidArgument()
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_STOP_WATCH_BOOTH, OwnerHandle, length: 10));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(watcher).BeginWatchingBooth(OwnerHandle);

        watcher.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();

        var result = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();

        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        StorageTestHarness.Session(watcher).WatchedBoothHandle.Should().Be(OwnerHandle,
            "a frame that cannot be read does not close the observation");
    }

    [Test]
    public void TryFindBoothOwner_MatchesTheSessionWhoseHandleAndOpenBoothAgree()
    {
        // The missing primitive of §5.2 point 7: the repository has no handle → connection index, so the
        // authorised clients are scanned. Pure: it only reads session state.
        var matching = new ConnectionInfo { CharacterHandle = OwnerHandle };
        matching.OpenBooth(new StartBoothRequest(1, "S"u8.ToArray(), new[] { new BoothOpenItem(1, 1, 1) }));

        var wrongHandle = new ConnectionInfo { CharacterHandle = 0x80000002u };
        wrongHandle.OpenBooth(new StartBoothRequest(1, "S"u8.ToArray(), new[] { new BoothOpenItem(1, 1, 1) }));

        var noBooth = new ConnectionInfo { CharacterHandle = OwnerHandle };

        var sessions = new[] { wrongHandle, noBooth, matching };

        BoothWatchService.TryFindBoothOwner(sessions, OwnerHandle, out var owner).Should().BeTrue();
        owner.Should().BeSameAs(matching, "both the handle and an open booth are required");

        BoothWatchService.TryFindBoothOwner(sessions, 0x80000002u, out owner).Should().BeTrue();
        owner.Should().BeSameAs(wrongHandle);

        BoothWatchService.TryFindBoothOwner(sessions, 0x80000003u, out _).Should().BeFalse();
        BoothWatchService.TryFindBoothOwner(sessions, 0, out _).Should().BeFalse("no booth carries handle 0");
        BoothWatchService.TryFindBoothOwner(Array.Empty<ConnectionInfo>(), OwnerHandle, out _).Should().BeFalse();
    }

    private static (GameClient Watcher, StorageTestHarness.FrameConnection Connection, GameClient Owner) Watch(
        BoothOpenItem[] declared, ItemEntity[] resolved)
    {
        var characters = A.Fake<ICharacterService>();

        // CharacterService returns null when the handle is not in the character's bag
        // (Services/CharacterService.cs:255-259). FakeItEasy would otherwise answer with a dummy
        // ItemEntity, which would silently hide the skipped record this socle has to handle.
        A.CallTo(() => characters.GetItemByHandleAsync(A<string>._, A<uint>._)).Returns((ItemEntity)null);

        foreach (var entity in resolved)
        {
            A.CallTo(() => characters.GetItemByHandleAsync(OwnerName, (uint)entity.Id)).Returns(entity);
        }

        var owner = Owner(characters, declared);

        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, OwnerHandle));
        var watcher = StorageTestHarness.NewGameClient(connection, characterService: characters);

        StorageTestHarness.Session(watcher).CharacterHandle = WatcherHandle;
        StorageTestHarness.Session(watcher).CharacterName = WatcherName;

        StorageTestHarness.Network(watcher).AuthorizedGameClients.TryAdd("owner", owner);

        watcher.OnDataReceived(connection.BytesAvailable);
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);

        return (watcher, connection, owner);
    }

    private static GameClient Owner(ICharacterService characters, BoothOpenItem[] declared)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var owner = StorageTestHarness.NewGameClient(connection, characterService: characters);

        StorageTestHarness.Session(owner).CharacterHandle = OwnerHandle;
        StorageTestHarness.Session(owner).CharacterName = OwnerName;
        StorageTestHarness.Session(owner).OpenBooth(
            new StartBoothRequest(1, "Owner's booth"u8.ToArray(), declared));

        return owner;
    }

    /// <summary>
    /// The frame the 7.3 client builds for 702 and 704: announced length, id, handle at +7 and the header
    /// checksum the receive loop verifies before dispatching anything.
    /// </summary>
    private static byte[] ClientFrame(GamePackets id, uint target, int length = 11)
    {
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);

        if (length >= 11)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), target);
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }
}
