using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The familier (pet) on the wire — <c>TS_SC_UNSUMMON_PET</c> (350), <c>TS_SC_ADD_PET_INFO</c> (351),
/// <c>TS_SC_REMOVE_PET_INFO</c> (352), the entry tram <c>TS_SC_ENTER</c> (3) with <c>objType = EOT_Pet</c>,
/// and the way the service sequences them — see <c>docs/packet-specs/socle-familier-pet.md</c>. The entry
/// tram is fixed at <b>95 bytes</b> (the summon's 96 <b>minus</b> the <c>enhance</c> byte) and the 351 at
/// <b>42 bytes</b>, where both server references declare 38. Every field whose value the reference leaves
/// open is asserted as the caller's, never as a default.
/// </summary>
[TestFixture]
public class PetWorldTests
{
    private const int EnterPacketLength = 95;
    private const int AddPetInfoLength = 42;
    private const uint MasterHandle = 0x40000123u;
    private const uint CageHandle = 0x40000789u;
    private const uint PetCode = 3102u;
    private const string PetName = "Loulou";

    // ------------------------------------------------- TS_SC_ADD_PET_INFO (351), 42 bytes and the 5th dword

    /// <summary>
    /// The whole frame, field by field. The two references declare 38 bytes (they stop at <c>code</c>); the
    /// 7.3 client reads a fifth dword, so the frame below is 42 and the last four bytes belong to it.
    /// </summary>
    [Test]
    public void BuildAddPetInfo_LaysOutThe42ByteFrameFieldByField()
    {
        var packet = GamePetPackets.BuildAddPetInfo(CageHandle, 0x40000002u, PetName, code: 77, unknown: 5);

        packet.Length.Should().Be(AddPetInfoLength, "rzu and NGemity declare 38, the client reads 42");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(AddPetInfoLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_ADD_PET_INFO);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(CageHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000002u);
        Encoding.ASCII.GetString(packet, 15, PetName.Length).Should().Be(PetName);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(77);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(5,
            "the fifth dword is the client's, at packet+0x26 of its read window");
    }

    [Test]
    public void BuildAddPetInfo_TruncatesTheNameTo18BytesAndZeroFills()
    {
        var packet = GamePetPackets.BuildAddPetInfo(CageHandle, 1u, "0123456789ABCDEFGHIJKL", 0, 0);

        Encoding.ASCII.GetString(packet, 15, 18).Should().Be("0123456789ABCDEFGH");
        packet[33].Should().Be(0, "the 19th byte of the slot stays the terminator");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(0,
            "code starts right after the 19-byte name, not after the name's useful bytes");
        packet.Length.Should().Be(AddPetInfoLength);
    }

    [Test]
    public void BuildAddPetInfo_LeavesTheNameSlotZeroedWithoutAName()
    {
        var packet = GamePetPackets.BuildAddPetInfo(CageHandle, 1u, null, 0, 0);

        packet.AsSpan(15, 19).ToArray().Should().OnlyContain(b => b == 0);
    }

    /// <summary>
    /// The 19-byte name slot of the 351 is byte-for-byte the one the entry tram writes, so a name announced
    /// in the creature window reads back identically in the world.
    /// </summary>
    [Test]
    public void BuildAddPetInfo_WritesTheNameExactlyLikeTheEntryTram()
    {
        const string name = "0123456789ABCDEFGHIJKL";
        var info = GamePetPackets.BuildAddPetInfo(CageHandle, 1u, name, 0, 0);
        var entry = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, false,
            MasterHandle, PetCode, name);

        info.AsSpan(15, 19).ToArray().Should().Equal(entry.AsSpan(76, 19).ToArray());
    }

    // ------------------------------------------------------- 350 and 352, the two 11-byte handle trames

    [Test]
    public void BuildUnsummonPet_LaysOutTheHandleTram()
    {
        var packet = GamePetPackets.BuildUnsummonPet(0x4000000Au);

        packet.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_UNSUMMON_PET);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x4000000Au);
    }

    [Test]
    public void BuildRemovePetInfo_LaysOutTheHandleTram()
    {
        var packet = GamePetPackets.BuildRemovePetInfo(0x4000000Au);

        packet.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_REMOVE_PET_INFO);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x4000000Au);
    }

    /// <summary>
    /// 350 and 352 are <b>different</b> frames with the same shape: 350 removes the actor from the world,
    /// 352 closes its creature window. Emitting one for the other would remove a pet the client still lists,
    /// or list one it has already dropped.
    /// </summary>
    [Test]
    public void TheThreeIdsAreTheSevenThreeOnesAndTwoHandleTramesStayApart()
    {
        ((ushort)GamePackets.TM_SC_UNSUMMON_PET).Should().Be(350, "the 9.6.3 remap to 1350 does not apply");
        ((ushort)GamePackets.TM_SC_ADD_PET_INFO).Should().Be(351);
        ((ushort)GamePackets.TM_SC_REMOVE_PET_INFO).Should().Be(352);

        var unsummon = GamePetPackets.BuildUnsummonPet(7u);
        var remove = GamePetPackets.BuildRemovePetInfo(7u);

        unsummon.AsSpan(4, 2).ToArray().Should().NotEqual(remove.AsSpan(4, 2).ToArray());
        unsummon.AsSpan(0, 4).ToArray().Should().Equal(remove.AsSpan(0, 4).ToArray(),
            "both are 11 bytes long");
    }

    // ----------------------------------------------------------- TS_SC_ENTER (3) for a pet, 95 bytes

    [Test]
    public void BuildEnterPet_LaysOutThe95ByteTramFieldByField()
    {
        var packet = GameSpawnPackets.BuildEnterPet(
            handle: 0x40000002u, x: 83950f, y: 115980f, z: 4.5f, layer: 3,
            hp: 120, maxHp: 900, mp: 30, maxMp: 450, level: 5, race: 9, faceDir: 1.5f,
            isFirstEnter: true, masterHandle: MasterHandle, petCode: PetCode, name: PetName);

        packet.Length.Should().Be(EnterPacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(EnterPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(1, "ET_NPC (Type::Object) is what a pet declares as its main type");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x40000002u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(83950f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(115980f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(4.5f);
        packet[24].Should().Be(3);
        packet[25].Should().Be(7, "EOT_Pet is the only case the 7.3 client executes for a pet");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(26, 4)).Should().Be(0, "no pet status flag exists");
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(30, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(120);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(900);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(42, 4)).Should().Be(30);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(450);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(5);
        packet[54].Should().Be(9, "race is part of the 38-byte creature block in 7.3");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(55, 4)).Should().Be(0, "skin_color, caller-silent");
        packet[59].Should().Be(1, "first entry into the world");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(60, 4)).Should().Be(0, "energy, caller-silent");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(64, 4)).Should().Be(MasterHandle);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(68, 2)).Should().Be(0, "a randomized word");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2)).Should().Be((ushort)(PetCode >> 16));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(72, 2)).Should().Be(0, "a randomized word");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(74, 2)).Should().Be((ushort)(PetCode & 0xFFFF));
        Encoding.ASCII.GetString(packet, 76, PetName.Length).Should().Be(PetName);
        packet[94].Should().Be(0, "the 19th byte of the name slot is the terminateur");
    }

    /// <summary>
    /// <c>enhance</c> is the summon's last byte (<c>&gt;= EPIC_7_1</c>) and
    /// <c>TS_SC_ENTER__PET_INFO</c> does <b>not</b> declare it: the pet tram is one byte shorter and offset
    /// 95 is outside it. Copying the summon's layout here would put a stray byte on the wire.
    /// </summary>
    [Test]
    public void BuildEnterPet_IsExactlyOneByteShorterThanTheSummonTram()
    {
        var pet = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, false,
            MasterHandle, PetCode, PetName);
        var summon = GameSpawnPackets.BuildEnterSummon(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0f, false,
            MasterHandle, PetCode, PetName, 0);

        pet.Length.Should().Be(95);
        summon.Length.Should().Be(96, "the summon keeps its enhance byte");
        pet.AsSpan(0, 95).ToArray().Should().NotEqual(summon.AsSpan(0, 95).ToArray(),
            "objType, and only objType, differs in the prefix");
        pet[25].Should().Be(7);
        summon[25].Should().Be(4);
        pet.AsSpan(64, 31).ToArray().Should().Equal(summon.AsSpan(64, 31).ToArray(),
            "master_handle, pet_code and name share their offsets with the summon");
    }

    [Test]
    public void BuildEnterPet_WritesThePetCodeAsARandomizedEncodedIntWithoutScrambling()
    {
        var packet = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, true,
            MasterHandle, PetCode, PetName);

        var word0 = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(68, 2));
        var word1 = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2));
        var word2 = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(72, 2));
        var word3 = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(74, 2));

        word0.Should().Be(0);
        word2.Should().Be(0);

        // The reference's own deserializer, transcribed from EncodingRandomized.h:34-40.
        var high = (ushort)(word1 - 2 * (word2 - word0));
        var low = (ushort)(word3 + 2 * (word2 + word0));
        (((uint)high << 16) | low).Should().Be(PetCode, "the client reads the id straight out of the field");

        var encoded = ScrambledInt.Encode(PetCode);
        (((uint)word1 << 16) | word3).Should().NotBe(encoded,
            "pet_code is an EncodedInt<EncodingRandomized>, not the scrambled monster_id encoding");
    }

    [Test]
    public void BuildEnterPet_TruncatesTheNameTo18BytesAndZeroFillsWithoutGrowingTheTram()
    {
        var packet = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, false,
            MasterHandle, PetCode, "0123456789ABCDEFGHIJKL");

        Encoding.ASCII.GetString(packet, 76, 18).Should().Be("0123456789ABCDEFGH");
        packet[94].Should().Be(0);
        packet.Length.Should().Be(EnterPacketLength);
    }

    [Test]
    public void BuildEnterPet_LeavesTheNameSlotZeroedWithoutAName()
    {
        var packet = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, false,
            MasterHandle, PetCode, null);

        packet.AsSpan(76, 19).ToArray().Should().OnlyContain(b => b == 0);
        packet.Length.Should().Be(EnterPacketLength);
    }

    [Test]
    public void BuildEnterPet_KeepsTheMaximaDistinctFromTheCurrentValues()
    {
        var packet = GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 120, 900, 30, 450, 5, 0, 0f, true,
            MasterHandle, PetCode, PetName);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(900,
            "no table carries a pet's maximum: it is the caller's, never its current health");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(450);
    }

    [Test]
    public void BuildEnterPet_DoesNotDisturbTheSummonNpcAndMonsterTrams()
    {
        var npc = GameSpawnPackets.BuildEnterNpc(0x40000002u, 83950f, 115980f, 1f, 3, 100, 7, 1, 42);
        var monster = GameSpawnPackets.BuildEnterMonster(0x40000003u, 83950f, 115980f, 1f, 3, 900, 5, 1,
            2101, 1.5f);

        npc.Length.Should().Be(72);
        monster.Length.Should().Be(73);
        GameSpawnPackets.BuildEnterPet(1u, 0f, 0f, 0f, 1, 1, 1, 1, 1, 1, 0, 0f, false, MasterHandle,
            PetCode, PetName).Length.Should().Be(EnterPacketLength);
    }

    // ------------------------------------------------------------------- entering, then leaving

    [Test]
    public void Enter_AnnouncesTheCreatureWindowThenPutsTheObjectInTheWorld()
    {
        var connection = new RecordingConnection();
        var service = new PetWorldService();

        var handle = service.Enter(Session(), "test", connection, Entry());

        connection.Sent.Should().HaveCount(2, "351 fills the creature window, then 3 adds the object");
        var info = connection.Sent[0];
        var entry = connection.Sent[1];

        BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_ADD_PET_INFO);
        info.Length.Should().Be(AddPetInfoLength);
        BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        entry.Length.Should().Be(EnterPacketLength);

        handle.Should().BeGreaterThan(0x40000000u, "handles come from WorldObjectHandle");
        BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(8, 4)).Should().Be(handle);
        BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(11, 4)).Should().Be(handle,
            "the creature window and the object carry the same handle");
        BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(7, 4)).Should().Be(CageHandle);
    }

    [Test]
    public void Enter_CarriesEveryCallerSuppliedFieldOfBothTrames()
    {
        var connection = new RecordingConnection();

        new PetWorldService().Enter(Session(), "test", connection, Entry());

        var info = connection.Sent[0];
        var entry = connection.Sent[1];

        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(34, 4)).Should().Be(77,
            "the 4th int32 of the 351 is the caller's: no reference names its source");
        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(38, 4)).Should().Be(5,
            "the 5th int32 is the caller's: no source names it at all");
        Encoding.ASCII.GetString(info, 15, PetName.Length).Should().Be(PetName);

        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(12, 4)).Should().Be(83950f,
            "no reference places a pet, so the caller's x is written as it is — never jittered");
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(16, 4)).Should().Be(115980f);
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(20, 4)).Should().Be(4.5f);
        entry[24].Should().Be(3);
        entry[54].Should().Be(9);
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(30, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(50, 4)).Should().Be(5);
        entry[59].Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(64, 4)).Should().Be(MasterHandle,
            "the master handle is the session's character handle");
        Encoding.ASCII.GetString(entry, 76, PetName.Length).Should().Be(PetName);
    }

    [Test]
    public void Enter_WithoutASessionOrConnectionSendsNothingAndReturnsZero()
    {
        var service = new PetWorldService();
        var connection = new RecordingConnection();

        service.Enter(null!, "test", connection, Entry()).Should().Be(0);
        service.Enter(Session(), "test", null!, Entry()).Should().Be(0);
        service.Enter(Session(), "test", connection, null!).Should().Be(0);
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Leave_SendsTheUnsummonThenTheLeaveForTheSameHandle()
    {
        var connection = new RecordingConnection();
        var service = new PetWorldService();
        var handle = service.Enter(Session(), "test", connection, Entry());
        connection.Sent.Clear();

        service.Leave(Session(), "test", connection, handle).Should().BeTrue();

        connection.Sent.Should().HaveCount(2, "the master sees the unsummon, then the object leave");
        var unsummon = connection.Sent[0];
        var leave = connection.Sent[1];

        BinaryPrimitives.ReadUInt16LittleEndian(unsummon.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_UNSUMMON_PET);
        unsummon.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(unsummon.AsSpan(7, 4)).Should().Be(handle);
        BinaryPrimitives.ReadUInt16LittleEndian(leave.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_LEAVE);
        leave.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(leave.AsSpan(7, 4)).Should().Be(handle);
    }

    [Test]
    public void Leave_RefusesAnEmptyHandleWithoutSendingAnything()
    {
        var connection = new RecordingConnection();

        new PetWorldService().Leave(Session(), "test", connection, 0u).Should().BeFalse();
        connection.Sent.Should().BeEmpty();
    }

    // ----------------------------------------------------------------------------------- receive guard

    /// <summary>
    /// The three ids are server to client only: the 7.3 client builds none of them, so a client that sends
    /// one is not making a request. The receive loop must log it and move on — an id declared in
    /// <see cref="GamePackets"/> but missing from the dispatch chain reaches the throwing switch at the end
    /// of <c>GameClient.OnDataReceived</c> and breaks the loop.
    /// </summary>
    [Test]
    public void AClientSentPetFrame_IsDroppedWithoutAnEcho()
    {
        var ids = new[]
        {
            ((ushort)GamePackets.TM_SC_UNSUMMON_PET, 11), ((ushort)GamePackets.TM_SC_ADD_PET_INFO, 42),
            ((ushort)GamePackets.TM_SC_REMOVE_PET_INFO, 11),
        };

        foreach (var (id, length) in ids)
        {
            var frame = Frame(id, length);
            var connection = new StorageTestHarness.FrameConnection(frame);
            var client = StorageTestHarness.NewGameClient(connection);

            var receive = () => client.OnDataReceived(connection.BytesAvailable);

            receive.Should().NotThrow("no member of GamePackets may reach the throwing switch");
            connection.Sent.Should().BeEmpty();
        }
    }

    /// <summary>A well-formed plaintext frame of the requested length, checksum included.</summary>
    private static byte[] Frame(ushort id, int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), id);
        frame[6] = Checksum(frame);
        return frame;
    }

    // --------------------------------------------------------------------------------------- helpers

    private static ConnectionInfo Session(byte layer = 1) =>
        new() { X = 0f, Y = 0f, Layer = layer, CharacterHandle = MasterHandle };

    private static PetWorldEntry Entry() =>
        new()
        {
            CageHandle = CageHandle,
            PetCode = PetCode,
            Code = 77,
            Unknown = 5,
            Name = PetName,
            Level = 5,
            Hp = 120,
            MaxHp = 900,
            Mp = 30,
            MaxMp = 450,
            Race = 9,
            FaceDirection = 1.5f,
            X = 83950f,
            Y = 115980f,
            Z = 4.5f,
            Layer = 3,
            IsFirstEnter = true,
        };

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        return checksum;
    }

    /// <summary>In-memory connection: everything the service sends is captured, nothing reaches a socket.</summary>
    private sealed class RecordingConnection : Connection
    {
        public RecordingConnection()
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
        }

        public List<byte[]> Sent { get; } = new();

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }
}
