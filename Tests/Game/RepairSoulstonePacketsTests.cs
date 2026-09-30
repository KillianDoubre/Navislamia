using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// TM_CS_REPAIR_SOULSTONE (262), the soul-stone recharge request of the crafting family, is a fixed 31-byte
/// frame in Epic 7.3: the 7-byte header, then six <c>uint32</c> little-endian handles at offsets 7, 11, 15,
/// 19, 23 and 27. Passing 31 bytes is the whole frame — no counter, no length field, so a frame with one
/// slot filled is exactly as long as a frame with six, and an empty slot is written zero.
/// <para>
/// The 7.3 client is the source of the layout: one sender lays the six handles out there
/// (SFrame.exe <c>0x48dad0</c>, <c>[edi+0x13]</c>..<c>[edi+0x27]</c>) and the interface message that feeds it
/// copies the six slots of the recharge window in that same order (<c>0x56b2ec-0x56b31c</c>), never sending a
/// frame whose six slots are all zero (predicate <c>0x56b270-0x56b2ae</c>).
/// </para>
/// <para>
/// The repository reads, bounds and resolves the frame, then refuses it: no reference describes a
/// recharging engine, a Lak cost, a formula or where a stone's Soul Power lives, and no frame of the family
/// carries a result (fiche §5.2, §7). The tests below therefore assert the wire layout, the refusals and the
/// fact that a zero slot is a sentinel and never an item — not a working recharge. See
/// docs/packet-specs/262-repair-soulstone.md and docs/packet-specs/socle-artisanat-objets.md.
/// </para>
/// </summary>
[TestFixture]
public class RepairSoulstonePacketsTests
{
    private const int PacketLength = 31;
    private const int HeaderSize = 7;
    private const int HandleCount = 6;
    private const int HandleSize = 4;
    private const ushort PacketId = 262;
    private const string Character = "Killian";
    private const uint CharacterHandle = 42;

    private static readonly int[] HandleOffsets = { 7, 11, 15, 19, 23, 27 };

    private static readonly uint[] SixHandles =
    {
        0x80000200u, 0x80000201u, 0x80000202u, 0x80000203u, 0x80000204u, 0x80000205u
    };

    /// <summary>
    /// Builds the frame the way the 7.3 sender lays it out: the 7-byte header, then four little-endian bytes
    /// per handle at 7, 11, 15, 19, 23 and 27. The bytes are written one by one on purpose — a test that
    /// built the frame with the reader's own helpers would only compare the reader with itself.
    /// </summary>
    private static byte[] ClientFrame(params uint[] handles)
    {
        var packet = new byte[PacketLength];

        packet[0] = PacketLength;
        packet[4] = (byte)(PacketId & 0xFF);
        packet[5] = (byte)(PacketId >> 8);

        for (var slot = 0; slot < HandleCount; slot++)
        {
            var handle = slot < handles.Length ? handles[slot] : 0u;
            for (var index = 0; index < HandleSize; index++)
            {
                packet[HandleOffsets[slot] + index] = (byte)(handle >> (8 * index));
            }
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    /// <summary>
    /// A frame of an arbitrary declared length, header and checksum valid: the receive loop reads a frame by
    /// the length its own header announces, so a non-conforming 262 arrives as a buffer of that size.
    /// </summary>
    private static byte[] FrameOfLength(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(SixHandles), frame, Math.Min(PacketLength, length));

        if (length >= HeaderSize)
        {
            frame[0] = (byte)length;
            frame[1] = (byte)(length >> 8);
            frame[6] = StorageTestHarness.Checksum(frame);
        }

        return frame;
    }

    private static ushort IdOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static TS_SC_RESULT RefusalOf(byte[] packet)
        => new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();

    // ------------------------------------------------------------------ identity and layout

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_REPAIR_SOULSTONE).Should().Be(PacketId);

        // rzu remaps the request to 1262 from EPIC_9_6_3 (0x090603) on, above EPIC_7_3 = 0x070300: that id is
        // out of this repository's scope and must stay undeclared. The 7.3 client writes the id 0x106 itself
        // (0x48cf48), so the plain id is the one on the wire.
        Enum.IsDefined(typeof(GamePackets), (ushort)1262).Should().BeFalse(
            "1262 is the 9.6.3 remap of the recharge request and is above EPIC_7_3");
        Enum.IsDefined(typeof(GamePackets), PacketId).Should().BeTrue(
            "an id missing from GamePackets is dropped as undefined before any dispatch");
    }

    [Test]
    public void Layout_IsSixHandleSizeHandlesAfterTheHeaderAndNoCounter()
    {
        // Naming the parts apart means a change of the 7-byte header breaks this test even when the total
        // happens to stay at 31.
        Marshal.SizeOf<Header>().Should().Be(HeaderSize);
        HandleOffsets.Should().Equal(7, 11, 15, 19, 23, 27);
        HandleCount.Should().Be(6);
        HandleSize.Should().Be(sizeof(uint));
        (HeaderSize + HandleCount * HandleSize).Should().Be(PacketLength);
        (HandleOffsets[HandleCount - 1] + HandleSize).Should().Be(PacketLength,
            "the sixth handle ends the frame: nothing follows it, there is no counter and no padding");
    }

    [Test]
    public void ClientFrame_CarriesTheHeaderTheSpecificationPins()
    {
        var packet = ClientFrame(SixHandles);

        packet.Should().HaveCount(PacketLength);
        new Header(packet).Length.Should().Be(PacketLength);
        new Header(packet).ID.Should().Be(PacketId);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum is the sum of the first six header bytes");
    }

    // ------------------------------------------------------------------------------- the reader

    [Test]
    public void TryReadRepairSoulstone_ReadsEachSlotAtItsOwnOffset()
    {
        // Asymmetric values: a wrong stride, a two-byte field or a big-endian read changes at least one of
        // them, where six identical handles would hide all three.
        var handles = new uint[] { 0x01020304u, 0x0A0B0C0Du, 0x11223344u, 0x55667788u, 0x99AABBCCu, 0xDDEEFF00u };
        var packet = ClientFrame(handles);

        GameActionPackets.TryReadRepairSoulstone(packet, out var request).Should().BeTrue();

        request.ItemHandles.Should().HaveCount(HandleCount);
        for (var slot = 0; slot < HandleCount; slot++)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(HandleOffsets[slot], 4)).Should().Be(handles[slot],
                $"slot {slot} is written at offset {HandleOffsets[slot]}");
            request.ItemHandles[slot].Should().Be(handles[slot],
                $"the reader must read slot {slot} at offset {HandleOffsets[slot]}");
        }

        request.ItemHandles.Should().Equal(handles);
        request.ItemHandles[0].Should().NotBe(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(HandleOffsets[0], 4)),
            "the fields are little-endian");
    }

    [Test]
    public void TryReadRepairSoulstone_KeepsAnEmptySlotAsZero()
    {
        // Unlike 260, no slot of 262 is a target: the six are one and the same field, so an empty slot must
        // neither be dropped nor renumbered — dropping it would shift every handle behind it.
        var packet = ClientFrame(0u, 0x80000201u, 0u, 0u, 0x80000204u, 0u);

        GameActionPackets.TryReadRepairSoulstone(packet, out var request).Should().BeTrue();

        request.ItemHandles.Should().Equal(0u, 0x80000201u, 0u, 0u, 0x80000204u, 0u);
    }

    [TestCase(0, TestName = "TryReadRepairSoulstone_RefusesAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadRepairSoulstone_RefusesAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadRepairSoulstone_RefusesAFrameWithOneHandleOnly")]
    [TestCase(27, TestName = "TryReadRepairSoulstone_RefusesAFrameMissingItsLastHandle")]
    [TestCase(30, TestName = "TryReadRepairSoulstone_RefusesAFrameOneByteShort")]
    [TestCase(32, TestName = "TryReadRepairSoulstone_RefusesAPaddedFrame")]
    [TestCase(33, TestName = "TryReadRepairSoulstone_RefusesATwoByteOverFrame")]
    public void TryReadRepairSoulstone_AcceptsOnlyThirtyOneBytes(int length)
    {
        // The 7.3 client writes the length 0x1f in hard from its only frame builder (0x48cf53): another
        // length is a malformed frame, not a shorter or padded variant of the same request.
        var packet = FrameOfLength(length);

        GameActionPackets.TryReadRepairSoulstone(packet, out var request).Should().BeFalse();

        request.ItemHandles.Should().BeNull("a refused frame leaves no half-read value behind");
    }

    // ------------------------------------------------------------------ the refusal of the socle

    private sealed record Harness(CraftingSocleService Service, ICharacterService Characters, GameClient Client,
        StorageTestHarness.FrameConnection Connection, ConnectionInfo Session);

    /// <summary>
    /// A real <see cref="CraftingSocleService"/> behind a real receive loop, with the character store faked:
    /// the answer on the wire is what the production path produces, not what a stub was told to say.
    /// </summary>
    private static Harness Build(byte[] frame = null, bool inWorld = true)
    {
        var characters = A.Fake<ICharacterService>();
        var service = new CraftingSocleService(characters);
        var connection = new StorageTestHarness.FrameConnection(frame ?? Array.Empty<byte>());

        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "repair-soulstone-test-key" }),
            characters,
            A.Fake<IBannedWordsRepository>(),
            A.Fake<IStatService>(),
            Options.Create(new ServerOptions()),
            A.Fake<INpcSpawnService>(),
            A.Fake<INpcDialogService>(),
            A.Fake<IMonsterSpawnService>(),
            A.Fake<ICombatService>(),
            A.Fake<ILevelingService>(),
            A.Fake<ISkillService>(),
            A.Fake<IEquipmentService>(),
            A.Fake<IInventoryService>(),
            A.Fake<IGroundItemService>(),
            A.Fake<ISkillCastService>(),
            A.Fake<IFieldPropService>(),
            A.Fake<IItemUseService>(),
            A.Fake<IWorldLocationService>(),
            A.Fake<IResurrectionService>(),
            A.Fake<IEventAreaService>(),
            service,
            A.Fake<IStorageService>(),
            A.Fake<IQuestService>(),
            A.Fake<IGmCommandService>(),
            A.Fake<Navislamia.Game.Services.Pets.IPetSummonService>());

        var client = new GameClient(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp),
            networkService) { Connection = connection };

        var session = StorageTestHarness.Session(client);
        session.CharacterName = Character;
        session.CharacterHandle = inWorld ? CharacterHandle : 0;

        return new Harness(service, characters, client, connection, session);
    }

    /// <summary>Answers the named handles with an owned item and every other handle with null.</summary>
    private static void AnswersWithItems(Harness harness, params uint[] handles)
    {
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._))
            .Returns(Task.FromResult<ItemEntity>(null!));

        foreach (var handle in handles)
        {
            A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, handle))
                .Returns(Task.FromResult(new ItemEntity { Id = 1, ItemResourceId = 240100, Amount = 1, Idx = 0 }));
        }
    }

    [Test]
    public async Task HandleAsync_ResolvesEveryHandleThenRefusesWithInvalidArgument()
    {
        var harness = Build();
        AnswersWithItems(harness, SixHandles);

        await harness.Service.HandleAsync(harness.Client, PacketId, ClientFrame(SixHandles));

        foreach (var handle in SixHandles)
        {
            A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, handle)).MustHaveHappenedOnceExactly();
        }

        harness.Connection.Sent.Should().ContainSingle(
            "no reference describes a recharge, so a readable frame is refused and nothing else is sent");
        IdOf(harness.Connection.Sent[0]).Should().Be((ushort)GamePackets.TM_SC_RESULT);

        var refusal = RefusalOf(harness.Connection.Sent[0]);
        refusal.RequestMsgID.Should().Be(PacketId, "the answer echoes the request id");
        refusal.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        ((ushort)ResultCode.InvalidArgument).Should().Be(28,
            "the code travels as the socle's InvalidArgument, not as the 0 of TM_SC_RESULT's own id");
        refusal.Value.Should().Be(0, "the reference answer carries no value");
    }

    [Test]
    public async Task HandleAsync_NeverResolvesAZeroSlot()
    {
        // A slot the player left empty arrives as zero and is not an item: resolving it would answer
        // NotExist for a slot that was deliberately empty.
        var harness = Build();
        AnswersWithItems(harness, 0x80000201u, 0x80000204u);

        await harness.Service.HandleAsync(harness.Client, PacketId,
            ClientFrame(0u, 0x80000201u, 0u, 0u, 0x80000204u, 0u));

        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, 0u)).MustNotHaveHappened();
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustHaveHappenedTwiceExactly();
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, 0x80000201u)).MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, 0x80000204u)).MustHaveHappenedOnceExactly();

        harness.Connection.Sent.Should().ContainSingle();
        RefusalOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument)
            .And.NotBe((ushort)ResultCode.NotExist, "three empty slots are not three missing items");
    }

    [Test]
    public async Task HandleAsync_RefusesASixZeroFrameWithoutResolvingAnything()
    {
        // The 7.3 client never sends it (0x56b270-0x56b2ae returns 0 when the six slots are empty). The socle
        // reads it as a well-formed frame that names nothing and refuses it like any other readable frame —
        // the retained conduct, written down: the alternative (malformed) answers the very same code, and no
        // source distinguishes the two cases.
        var harness = Build();

        await harness.Service.HandleAsync(harness.Client, PacketId, ClientFrame(0u, 0u, 0u, 0u, 0u, 0u));

        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
        harness.Connection.Sent.Should().ContainSingle();
        var refusal = RefusalOf(harness.Connection.Sent[0]);
        refusal.RequestMsgID.Should().Be(PacketId);
        refusal.Result.Should().Be((ushort)ResultCode.InvalidArgument)
            .And.NotBe((ushort)ResultCode.NotExist);
    }

    [Test]
    public async Task HandleAsync_RefusesAnUnknownHandleWithNotExistAndItsHandle()
    {
        // Nothing belongs to this character, so the first handle of the frame is the one reported: the walk
        // stops there instead of resolving the five that follow.
        var harness = Build();
        AnswersWithItems(harness);

        await harness.Service.HandleAsync(harness.Client, PacketId, ClientFrame(SixHandles));

        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustHaveHappenedOnceExactly();
        harness.Connection.Sent.Should().ContainSingle();
        var refusal = RefusalOf(harness.Connection.Sent[0]);
        refusal.RequestMsgID.Should().Be(PacketId);
        refusal.Result.Should().Be((ushort)ResultCode.NotExist, "the convention of the 203 drop path");
        refusal.Value.Should().Be(unchecked((int)SixHandles[0]), "the refused handle travels in value");
    }

    [TestCase(7, TestName = "HandleAsync_RefusesAHeaderOnlyFrame")]
    [TestCase(30, TestName = "HandleAsync_RefusesAFrameOneByteShort")]
    [TestCase(32, TestName = "HandleAsync_RefusesAPaddedFrame")]
    public async Task HandleAsync_RefusesANonConformingLengthWithoutTouchingTheStore(int length)
    {
        var harness = Build();

        await harness.Service.HandleAsync(harness.Client, PacketId, FrameOfLength(length));

        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
        harness.Connection.Sent.Should().ContainSingle();
        RefusalOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public async Task HandleAsync_AnswersNothingOutsideTheWorld()
    {
        var harness = Build(inWorld: false);

        await harness.Service.HandleAsync(harness.Client, PacketId, ClientFrame(SixHandles));

        harness.Connection.Sent.Should().BeEmpty(
            "no character is in the world: there is no inventory to resolve against and no answer to send");
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task HandleAsync_RequiresNoContactStateAndWritesNone()
    {
        // NGemity arms SetLastContact("RepairSoulStone", 1) when it opens the window (Messages.cpp:939) and
        // its model reads that state for 260; the repository's equivalent is the dialog page's own trigger
        // list. The specification leaves 262 without any guard (it prescribes no contact state: fiche §5.4),
        // and this lot invents none — nothing is read and nothing is written, so the frame is refused the
        // same way whatever the session held before.
        var harness = Build();
        AnswersWithItems(harness, SixHandles);
        harness.Session.NpcDialogHandle = 0x40000001u;
        harness.Session.NpcDialogTriggers.Add("show_soulstone_repair_window");

        await harness.Service.HandleAsync(harness.Client, PacketId, ClientFrame(SixHandles));

        harness.Session.NpcDialogHandle.Should().Be(0x40000001u, "the refusal does not consume the dialog state");
        harness.Session.NpcDialogTriggers.Should().Equal("show_soulstone_repair_window");
        harness.Connection.Sent.Should().ContainSingle();
        RefusalOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    // ------------------------------------------------------------------- the receive loop

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutReachingTheThrowingSwitch()
    {
        // A member of GamePackets that no arm claims reaches the final switch and throws
        // "Unknown Packet Type" inside the receive loop, which kills the connection.
        var frame = ClientFrame(SixHandles);
        var harness = Build(frame);
        AnswersWithItems(harness, SixHandles);

        var receive = () => harness.Client.OnDataReceived(frame.Length);

        receive.Should().NotThrow("262 is defined and dispatched together");
        harness.Connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        StorageTestHarness.WaitFor(() => harness.Connection.Sent.Count > 0);
        harness.Connection.Sent.Should().ContainSingle();
        var refusal = RefusalOf(harness.Connection.Sent[0]);
        refusal.RequestMsgID.Should().Be(PacketId);
        refusal.Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAHeaderOnlyFrame")]
    [TestCase(30, TestName = "OnDataReceived_ConsumesAFrameOneByteShort")]
    [TestCase(32, TestName = "OnDataReceived_ConsumesAPaddedFrame")]
    public void OnDataReceived_ConsumesANonConformingLengthAndRefusesIt(int length)
    {
        var frame = FrameOfLength(length);
        var harness = Build(frame);

        var receive = () => harness.Client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        harness.Connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");

        StorageTestHarness.WaitFor(() => harness.Connection.Sent.Count > 0);
        RefusalOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopInStepOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[HeaderSize];
        keepalive[0] = HeaderSize;
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var stream = ClientFrame(SixHandles).Concat(keepalive).ToArray();
        var harness = Build(stream);
        AnswersWithItems(harness, SixHandles);

        var receive = () => harness.Client.OnDataReceived(stream.Length);

        receive.Should().NotThrow();
        harness.Connection.BytesAvailable.Should().Be(0, "the frame behind the request was consumed too");
    }
}
