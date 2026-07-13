using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class AttackPacketsTests
{
    [Test]
    public void BuildAttackEvent_LaysOutHeaderEventAndSingleAttackInfo()
    {
        var packet = GameAttackPackets.BuildAttackEvent(
            attackerHandle: 0x40000001u, targetHandle: 0x40000002u,
            attackSpeed: 1200, attackDelay: 1200, action: GameAttackPackets.ActionAttack,
            damage: 55, targetHp: 45, attackerHp: 200);

        packet.Length.Should().Be(83);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(83);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ATTACK_EVENT);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000001u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000002u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(15, 2)).Should().Be(1200);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17, 2)).Should().Be(1200);
        packet[19].Should().Be(3);
        packet[20].Should().Be(0);
        packet[21].Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(22, 4)).Should().Be(55);
        packet[30].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(59, 4)).Should().Be(45);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(75, 4)).Should().Be(200);
    }

    [Test]
    public void BuildEndAttack_HasEndActionAndZeroAttackCount()
    {
        var packet = GameAttackPackets.BuildEndAttack(0x40000001u, 0x40000002u);

        packet.Length.Should().Be(22);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(22);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ATTACK_EVENT);
        packet[6].Should().Be(Checksum(packet));
        packet[19].Should().Be(GameAttackPackets.ActionEndAttack);
        packet[21].Should().Be(0);
    }

    [Test]
    public void ReadAttackTarget_ReturnsTargetHandle()
    {
        var packet = new byte[15];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x40000001u);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11, 4), 0x40000099u);

        GameAttackPackets.ReadAttackTarget(packet).Should().Be(0x40000099u);
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }
}
