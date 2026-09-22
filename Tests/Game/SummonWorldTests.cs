using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The summon in the world — <c>TS_SC_ENTER</c> (3) for an invocation, the placement of its master, and
/// the way it leaves again — see <c>docs/packet-specs/socle-invocation-monde.md</c>. The tram is fixed at
/// <b>96 bytes</b>: the 26-byte creature prefix, the 38-byte shared creature block, <c>master_handle</c>
/// @64, the 8-byte randomized <c>summon_code</c> @68, the 19-byte <c>name</c> @76 and <c>enhance</c> @95.
/// Every field whose value the reference leaves open is asserted as the caller's, never as a default.
/// </summary>
[TestFixture]
public class SummonWorldTests
{
    private const int PacketLength = 96;
    private const uint MasterHandle = 0x40000123u;
    private const uint CardHandle = 0x40000456u;
    private const uint SummonCode = 1201u;
    private const string SummonName = "Lamia";

    // ---------------------------------------------------------------- TS_SC_ENTER (3), the entry tram

    [Test]
    public void BuildEnterSummon_LaysOutThe96ByteTramFieldByField()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 0x40000002u, x: 83950f, y: 115980f, z: 4.5f, layer: 3,
            hp: 120, maxHp: 900, mp: 30, maxMp: 450, level: 5, faceDir: 1.5f, isFirstEnter: true,
            masterHandle: MasterHandle, summonCode: SummonCode, name: SummonName, enhance: 0);

        var encoded = ScrambledInt.Encode(SummonCode);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(1, "ET_NPC (Type::Object) is what a summon declares as its main type");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x40000002u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(83950f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(115980f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(4.5f);
        packet[24].Should().Be(3, "layer is the master's");
        packet[25].Should().Be(4, "EOT_Summon");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(26, 4)).Should().Be(0, "no summon status flag exists");
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(30, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(120);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(900);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(42, 4)).Should().Be(30);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(450);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(5);
        packet[54].Should().Be(0, "UNIT_FIELD_RACE is never set for a summon");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(55, 4)).Should().Be(0);
        packet[59].Should().Be(1, "first entry into the world");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(60, 4)).Should().Be(0, "energy is a player-only field");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(64, 4)).Should().Be(MasterHandle);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(68, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2)).Should().Be((ushort)((encoded >> 16) & 0xFFFF));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(72, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(74, 2)).Should().Be((ushort)(encoded & 0xFFFF));
        Encoding.ASCII.GetString(packet, 76, SummonName.Length).Should().Be(SummonName);
        packet[95].Should().Be(0, "enhance is the last byte of the 96");
    }

    [Test]
    public void BuildEnterSummon_WritesTheSummonCodeAsARandomizedEncodedInt()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: false, masterHandle: MasterHandle, summonCode: SummonCode,
            name: SummonName, enhance: 0);

        ScrambledInt.Encode(SummonCode).Should().NotBe(SummonCode, "the wire value is scrambled, not the id");

        var high = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2));
        var low = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(74, 2));
        var onWire = ((uint)high << 16) | low;

        ScrambledInt.Decode(onWire).Should().Be(SummonCode);
    }

    [Test]
    public void BuildEnterSummon_TruncatesTheNameTo18BytesAndZeroFills()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: false, masterHandle: MasterHandle, summonCode: SummonCode,
            name: "0123456789ABCDEFGHIJKL", enhance: 0);

        Encoding.ASCII.GetString(packet, 76, 18).Should().Be("0123456789ABCDEFGH");
        packet[94].Should().Be(0, "the 19th byte of the slot stays the terminator");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
    }

    [Test]
    public void BuildEnterSummon_LeavesTheNameSlotZeroedWithoutAName()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: false, masterHandle: MasterHandle, summonCode: SummonCode,
            name: null, enhance: 0);

        packet.AsSpan(76, 19).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void BuildEnterSummon_WritesTheNameExactlyLikeTheCreatureWindow()
    {
        var entry = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: false, masterHandle: MasterHandle, summonCode: SummonCode,
            name: SummonName, enhance: 0);
        var info = GameSummonPackets.BuildAddSummonInfo(CardHandle, 1u, SummonName, (int)SummonCode, 1, 0);

        info.AsSpan(15, 19).ToArray().Should().Equal(entry.AsSpan(76, 19).ToArray(),
            "301 and 3 show the same creature name, so both 19-byte slots must carry the same bytes");
    }

    [Test]
    public void BuildEnterSummon_SendsZeroForAReEntry()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: false, masterHandle: MasterHandle, summonCode: SummonCode,
            name: SummonName, enhance: 0);

        packet[59].Should().Be(0);
        packet.Length.Should().Be(PacketLength);
    }

    [Test]
    public void BuildEnterSummon_WritesTheEnhanceByteAt95WithoutGrowingTheTram()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 1, maxHp: 1, mp: 1, maxMp: 1, level: 1,
            faceDir: 0f, isFirstEnter: true, masterHandle: MasterHandle, summonCode: SummonCode,
            name: SummonName, enhance: 7);

        packet[95].Should().Be(7, "the Epic >= 7.1 field is the last byte of the frame");
        packet.Length.Should().Be(PacketLength);
    }

    [Test]
    public void BuildEnterSummon_KeepsTheMaximaDistinctFromTheCurrentValues()
    {
        var packet = GameSpawnPackets.BuildEnterSummon(
            handle: 1u, x: 0f, y: 0f, z: 0f, layer: 1, hp: 120, maxHp: 900, mp: 30, maxMp: 450,
            level: 5, faceDir: 0f, isFirstEnter: true, masterHandle: MasterHandle,
            summonCode: SummonCode, name: SummonName, enhance: 0);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(900,
            "a summon's maximum is not its current health, unlike the NPC and monster callers");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(450);
    }

    [Test]
    public void BuildEnterSummon_DoesNotDisturbTheNpcAndMonsterTram()
    {
        var npc = GameSpawnPackets.BuildEnterNpc(0x40000002u, 83950f, 115980f, 1f, 3, 100, 7, 1, 42);
        var monster = GameSpawnPackets.BuildEnterMonster(0x40000003u, 83950f, 115980f, 1f, 3, 900, 5, 1,
            2101, 1.5f);

        npc.Length.Should().Be(72);
        BinaryPrimitives.ReadInt32LittleEndian(npc.AsSpan(38, 4)).Should().Be(100, "max_hp still mirrors hp");
        BinaryPrimitives.ReadInt32LittleEndian(npc.AsSpan(42, 4)).Should().Be(0);
        npc[59].Should().Be(0, "an NPC is never a first entry");
        monster.Length.Should().Be(73);
        BinaryPrimitives.ReadInt32LittleEndian(monster.AsSpan(38, 4)).Should().Be(900);
    }

    // ------------------------------------------------------------------- entering, placing, leaving

    [Test]
    public void Enter_AnnouncesTheCreatureWindowThenPutsTheObjectInTheWorld()
    {
        var connection = new RecordingConnection();
        var service = new SummonWorldService();

        var handle = service.Enter(Session(83950f, 115980f), "test", connection, Entry(noiseRange: 0));

        connection.Sent.Should().HaveCount(2, "301 fills the creature window, then 3 adds the object");
        var info = connection.Sent[0];
        var entry = connection.Sent[1];

        BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_ADD_SUMMON_INFO);
        info.Length.Should().Be(46);
        BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        entry.Length.Should().Be(PacketLength);

        handle.Should().BeGreaterThan(0x40000000u, "handles come from WorldObjectHandle");
        BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(8, 4)).Should().Be(handle);
        BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(11, 4)).Should().Be(handle,
            "the creature window and the object carry the same handle");
        BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(7, 4)).Should().Be(CardHandle);
        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(34, 4)).Should().Be((int)SummonCode);
    }

    [Test]
    public void Enter_PlacesTheSummonOnItsMasterWithinTheJitterPalier()
    {
        var connection = new RecordingConnection();
        var service = new SummonWorldService();
        const float masterX = 83950f;
        const float masterY = 115980f;
        const int range = SummonWorldService.SummonNoiseRange;

        for (var draw = 0; draw < 200; draw++)
        {
            connection.Sent.Clear();
            service.Enter(Session(masterX, masterY, layer: 3), "test", connection, Entry(range));

            var entry = connection.Sent[1];
            var offsetX = BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(12, 4)) - masterX;
            var offsetY = BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(16, 4)) - masterY;

            offsetX.Should().BeGreaterOrEqualTo(-range / 2f).And.BeLessThan(range / 2f);
            offsetY.Should().BeGreaterOrEqualTo(-range / 2f).And.BeLessThan(range / 2f);
            BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(20, 4)).Should().Be(4.5f,
                "NON ÉTABLI 6 leaves z to the caller, and z is never the master's");
            entry[24].Should().Be(3, "the layer is the master's");
            BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(64, 4)).Should().Be(MasterHandle);
        }
    }

    [Test]
    public void Enter_CarriesEveryCallerSuppliedField()
    {
        var connection = new RecordingConnection();
        var service = new SummonWorldService();

        service.Enter(Session(83950f, 115980f, layer: 3), "test", connection, Entry(noiseRange: 0));

        var entry = connection.Sent[1];
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(34, 4)).Should().Be(120);
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(38, 4)).Should().Be(900);
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(42, 4)).Should().Be(30);
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(46, 4)).Should().Be(450);
        BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(50, 4)).Should().Be(5);
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(30, 4)).Should().Be(1.5f);
        entry[59].Should().Be(1);
        entry[95].Should().Be(0);
        Encoding.ASCII.GetString(entry, 76, SummonName.Length).Should().Be(SummonName);
    }

    [Test]
    public void Enter_WithNoiseRangeZeroPlacesTheSummonExactlyOnItsMaster()
    {
        var connection = new RecordingConnection();
        var service = new SummonWorldService();

        service.Enter(Session(83950f, 115980f), "test", connection, Entry(noiseRange: 0));

        var entry = connection.Sent[1];
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(12, 4)).Should().Be(83950f);
        BinaryPrimitives.ReadSingleLittleEndian(entry.AsSpan(16, 4)).Should().Be(115980f);
    }

    [Test]
    public void Enter_WithoutASessionOrConnectionSendsNothingAndReturnsZero()
    {
        var service = new SummonWorldService();
        var connection = new RecordingConnection();

        service.Enter(null!, "test", connection, Entry(noiseRange: 0)).Should().Be(0);
        service.Enter(Session(0f, 0f), "test", null!, Entry(noiseRange: 0)).Should().Be(0);
        service.Enter(Session(0f, 0f), "test", connection, null!).Should().Be(0);
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Leave_SendsTheUnsummonThenTheLeaveForTheSameHandle()
    {
        var connection = new RecordingConnection();
        var service = new SummonWorldService();
        var handle = service.Enter(Session(83950f, 115980f), "test", connection, Entry(noiseRange: 0));
        connection.Sent.Clear();

        service.Leave(Session(83950f, 115980f), "test", connection, handle).Should().BeTrue();

        connection.Sent.Should().HaveCount(2, "the master sees the unsummon, then the object leave");
        var unsummon = connection.Sent[0];
        var leave = connection.Sent[1];

        BinaryPrimitives.ReadUInt16LittleEndian(unsummon.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_UNSUMMON);
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

        new SummonWorldService().Leave(Session(0f, 0f), "test", connection, 0u).Should().BeFalse();
        connection.Sent.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------- jitter and paliers

    [TestCase(70, 0u, -35f)]
    [TestCase(70, 34u, -1f)]
    [TestCase(70, 35u, 0f)]
    [TestCase(70, 69u, 34f)]
    [TestCase(70, 70u, -35f)]
    [TestCase(35, 0u, -17f)]
    [TestCase(50, 49u, 24f)]
    public void Jitter_MatchesTheReferenceIntegerFormula(int range, uint raw, float expected)
    {
        SummonWorldService.Jitter(range, raw).Should().Be(expected);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Jitter_WithoutAPalierKeepsTheMasterPosition(int range)
    {
        SummonWorldService.Jitter(range, 12345u).Should().Be(0f,
            "the reference would divide by zero, and a caller with no palier must not take the send down");
    }

    [Test]
    public void NoiseRanges_AreTheThreeReferencePaliers()
    {
        SummonWorldService.SummonNoiseRange.Should().Be(70);
        SummonWorldService.LoginNoiseRange.Should().Be(50);
        SummonWorldService.WarpNoiseRange.Should().Be(35);
    }

    // ---------------------------------------------------------------------------------- entry source

    [Test]
    public void FromEntity_ReadsTheSummonRowAndKeepsTheOpenFieldsFromTheCaller()
    {
        var summon = new SummonEntity
        {
            Id = 7,
            CardItemId = CardHandle,
            Name = SummonName,
            Lv = 5,
            Sp = 12,
            Hp = 120,
            Mp = 30,
        };

        var entry = SummonWorldEntry.FromEntity(summon, code: (int)SummonCode, maxHp: 900, maxMp: 450,
            faceDirection: 1.5f, z: 4.5f, noiseRange: SummonWorldService.LoginNoiseRange,
            isFirstEnter: true);

        entry.CardHandle.Should().Be(CardHandle);
        entry.Name.Should().Be(SummonName);
        entry.Level.Should().Be(5);
        entry.Sp.Should().Be(12);
        entry.Hp.Should().Be(120);
        entry.Mp.Should().Be(30);
        entry.Code.Should().Be((int)SummonCode);
        entry.MaxHp.Should().Be(900, "the maximum is the caller's, never the row's current health");
        entry.MaxMp.Should().Be(450);
        entry.Z.Should().Be(4.5f);
        entry.NoiseRange.Should().Be(SummonWorldService.LoginNoiseRange);
        entry.IsFirstEnter.Should().BeTrue();
        entry.Enhance.Should().Be(0, "nothing fills enhance yet (NON ÉTABLI 7)");
    }

    // --------------------------------------------------------------------------------------- helpers

    private static ConnectionInfo Session(float x, float y, byte layer = 1) =>
        new() { X = x, Y = y, Layer = layer, CharacterHandle = MasterHandle };

    private static SummonWorldEntry Entry(int noiseRange) =>
        new()
        {
            CardHandle = CardHandle,
            Code = (int)SummonCode,
            Name = SummonName,
            Level = 5,
            Sp = 12,
            Hp = 120,
            MaxHp = 900,
            Mp = 30,
            MaxMp = 450,
            FaceDirection = 1.5f,
            Z = 4.5f,
            Enhance = 0,
            IsFirstEnter = true,
            NoiseRange = noiseRange,
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
