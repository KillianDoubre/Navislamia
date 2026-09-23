using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Sublot A of the PK socle (docs/packet-specs/socle-mode-pk.md §8): the actor status mask and the
/// PK bit it carries. The two frames that publish the mask are pinned here: TM_SC_STATUS_CHANGE (500)
/// is 15 bytes with handle @7 and status @11, and the player variant of TM_SC_ENTER (3) is 118 bytes
/// with status @26.
/// </summary>
[TestFixture]
public class PkModeStatusTests
{
    private const int HeaderSize = 7;
    private const int StatusChangeSize = 15;
    private const int PlayerEnterSize = 118;
    private const int EnterStatusOffset = 26;

    [Test]
    public void PkOnBit_IsBitEleven_AndSharesNoBitWithTheMonsterDeadFlag()
    {
        CreatureStatus.PlayerPkOn.Should().Be(1u << 11);
        CreatureStatus.PlayerPkOn.Should().Be(0x800u);

        // 1 << 8 is "dead" for a monster and "sit down" for a player: a player mask must never
        // carry it (docs/packet-specs/socle-mode-pk.md §5.2).
        CreatureStatus.MonsterDead.Should().Be(1u << 8);
        CreatureStatus.PlayerSitdown.Should().Be(CreatureStatus.MonsterDead);
        (CreatureStatus.PlayerPkOn & CreatureStatus.MonsterDead).Should().Be(0);
    }

    [Test]
    public void ActorStatus_ComposesTheWholeMaskByActorNature()
    {
        ActorStatus.ForPlayer(pkModeOn: true).Should().Be(CreatureStatus.PlayerPkOn);
        ActorStatus.ForPlayer(pkModeOn: false).Should().Be(0u);

        ActorStatus.ForMonster(dead: true).Should().Be(CreatureStatus.MonsterDead);
        ActorStatus.ForMonster().Should().Be(0u);
        ActorStatus.ForMonster(dead: false).Should().Be(0u);

        ActorStatus.ForNpc().Should().Be(0u);

        // The nature decides the meaning of the bits: a monster mask never carries the PK bit, and
        // a player mask never carries the monster death bit.
        (ActorStatus.ForMonster(dead: true) & CreatureStatus.PlayerPkOn).Should().Be(0);
        (ActorStatus.ForPlayer(pkModeOn: true) & CreatureStatus.MonsterDead).Should().Be(0);
    }

    [Test]
    public void StatusChangeFrame_IsFifteenBytes_WithHandleAtSevenAndStatusAtEleven()
    {
        var packet = GameCharacterPackets.BuildStatusChange(0x40000001u, ActorStatus.ForPlayer(pkModeOn: true));

        packet.Should().HaveCount(StatusChangeSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(StatusChangeSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_STATUS_CHANGE);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000001u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x800u);
    }

    [Test]
    public void StatusChangeFrame_ClearsThePkBitWhenTheModeIsOff()
    {
        var off = GameCharacterPackets.BuildStatusChange(0x40000001u, ActorStatus.ForPlayer(pkModeOn: false));
        var deadMonster = GameCharacterPackets.BuildStatusChange(0x40000002u, ActorStatus.ForMonster(dead: true));

        BinaryPrimitives.ReadUInt32LittleEndian(off.AsSpan(11, 4)).Should().Be(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(deadMonster.AsSpan(11, 4)).Should().Be(0x100u);
    }

    [Test]
    public void PlayerEnterFrame_IsOneHundredAndEighteenBytes_WithStatusAtTwentySix()
    {
        var enter = new TS_SC_ENTER_PLAYER
        {
            Type = 0,
            Handle = 0x40000001u,
            X = 92044f,
            Y = 116950f,
            Z = 12.5f,
            Layer = 3,
            ObjType = 0,
            Status = ActorStatus.ForPlayer(pkModeOn: true),
            FaceDirection = 0f,
            Hp = 900,
            MaxHp = 900,
            Mp = 300,
            MaxMp = 300,
            Level = 7,
            Race = 2,
            SkinColor = 0xAABBCCDDu,
            IsFirstEnter = 1,
            Energy = 0,
            Name = "Freezeraid"
        };

        var packet = new Packet<TS_SC_ENTER_PLAYER>((ushort)GamePackets.TM_SC_ENTER, enter).Data;

        packet.Should().HaveCount(PlayerEnterSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PlayerEnterSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        packet[6].Should().Be(Checksum(packet));
        packet[HeaderSize].Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x40000001u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(92044f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(116950f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(12.5f);
        packet[24].Should().Be(3);
        packet[25].Should().Be(0);

        // The one field the socle starts filling in this frame.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(EnterStatusOffset, 4)).Should().Be(0x800u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(30, 4)).Should().Be(0f);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(900);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(7);
        packet[54].Should().Be(2);
    }

    [Test]
    public void BothSendSites_PublishTheSameMask()
    {
        var enter = new Packet<TS_SC_ENTER_PLAYER>((ushort)GamePackets.TM_SC_ENTER,
            new TS_SC_ENTER_PLAYER { Status = ActorStatus.ForPlayer(pkModeOn: true) }).Data;
        var statusChange = GameCharacterPackets.BuildStatusChange(0x40000001u, ActorStatus.ForPlayer(pkModeOn: true));

        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(EnterStatusOffset, 4))
            .Should().Be(BinaryPrimitives.ReadUInt32LittleEndian(statusChange.AsSpan(11, 4)));
    }

    [Test]
    public void CreatureEnterFrames_LeaveTheStatusAtOffsetTwentySixClear()
    {
        var npc = GameSpawnPackets.BuildEnterNpc(
            handle: 0x40000001u, x: 92044f, y: 116950f, z: 12.5f,
            layer: 3, hp: 250, level: 7, race: 2, npcId: 200015);
        var monster = GameSpawnPackets.BuildEnterMonster(
            handle: 0x40000002u, x: 83950f, y: 115980f, z: 4f,
            layer: 3, hp: 900, level: 5, race: 1, monsterId: 2101, faceDir: 1.5f);

        npc.Should().HaveCount(72);
        monster.Should().HaveCount(73);
        BinaryPrimitives.ReadUInt32LittleEndian(npc.AsSpan(EnterStatusOffset, 4)).Should().Be(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(monster.AsSpan(EnterStatusOffset, 4)).Should().Be(0u);
    }

    [Test]
    public void ConnectionInfo_PkMode_StartsOffAndIsDroppedWithTheSession()
    {
        var info = new ConnectionInfo();

        info.PkMode.Should().BeFalse();

        info.PkMode = true;
        info.CharacterHandle = 0x40000001u;
        info.PkMode.Should().BeTrue();

        info.ClearCharacterSession();

        info.PkMode.Should().BeFalse();
        info.CharacterHandle.Should().Be(0u);
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++)
        {
            sum += packet[i];
        }

        return sum;
    }
}
