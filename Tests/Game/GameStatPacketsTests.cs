using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class GameStatPacketsTests
{
    private const int AttributeOffset = 27;

    private static byte Checksum(byte[] p)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        return c;
    }

    private static short Attrib(byte[] packet, int index)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(AttributeOffset + index * 2, 2));
    }

    [Test]
    public void BuildStatInfo_HasCorrectFrameAndBaseStats()
    {
        var stats = new StatBlock
        {
            StatId = 7, Strength = 20, Vitality = 21, Dexterity = 22,
            Agility = 23, Intelligence = 24, Wisdom = 25, Luck = 26
        };

        var p = GameStatPackets.BuildStatInfo(handle: 0x11223344, stats, StatInfoType.Total);

        p.Length.Should().Be(96);
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(0, 4)).Should().Be(96);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)).Should().Be(1000);
        p[6].Should().Be(Checksum(p));
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(11, 2)).Should().Be(7);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(13, 2)).Should().Be(20);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(15, 2)).Should().Be(21);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(17, 2)).Should().Be(22);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(19, 2)).Should().Be(23);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(21, 2)).Should().Be(24);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(23, 2)).Should().Be(25);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(25, 2)).Should().Be(26);
        p[95].Should().Be(0);
    }

    [Test]
    public void BuildStatInfo_WritesTheEpic73AttributeOrder()
    {
        var stats = new StatBlock
        {
            Critical = 1, CriticalPower = 2, AttackPointRight = 3, AttackPointLeft = 4,
            Defence = 5, BlockDefence = 6, MagicPoint = 7, MagicDefence = 8,
            AccuracyRight = 9, AccuracyLeft = 10, MagicAccuracy = 11, Avoid = 12,
            MagicAvoid = 13, BlockChance = 14, MoveSpeed = 15, AttackSpeed = 16,
            AttackRange = 17, MaxWeight = 18, CastingSpeed = 19, CoolTimeSpeed = 20,
            ItemChance = 21, HpRegenPercentage = 22, HpRegenPoint = 23, MpRegenPercentage = 24,
            MpRegenPoint = 25, PerfectBlock = 26, MagicalDefIgnore = 27, MagicalDefIgnoreRatio = 28,
            PhysicalDefIgnore = 29, PhysicalDefIgnoreRatio = 30, MagicalPenetration = 31,
            MagicalPenetrationRatio = 32, PhysicalPenetration = 33, PhysicalPenetrationRatio = 34
        };

        var packet = GameStatPackets.BuildStatInfo(1, stats, StatInfoType.Total);

        for (var index = 0; index < 34; index++)
        {
            Attrib(packet, index).Should().Be((short)(index + 1),
                "attribute {0} must follow the Epic 7.3 order", index);
        }
    }

    [Test]
    public void BuildStatInfo_PlacesMaxWeightAfterAttackRange()
    {
        var packet = GameStatPackets.BuildStatInfo(1,
            new StatBlock { AttackRange = 50, MaxWeight = 700 }, StatInfoType.Total);

        Attrib(packet, 16).Should().Be(50);
        Attrib(packet, 17).Should().Be(700);
    }

    [Test]
    public void BuildStatInfo_TruncatesFloatsTowardZero()
    {
        var packet = GameStatPackets.BuildStatInfo(1, new StatBlock { AttackPointRight = 12.9f },
            StatInfoType.Total);

        Attrib(packet, 2).Should().Be(12);
    }

    [TestCase(StatInfoType.Total, (byte)0)]
    [TestCase(StatInfoType.ByItem, (byte)1)]
    public void BuildStatInfo_WritesTheType(StatInfoType type, byte expected)
    {
        GameStatPackets.BuildStatInfo(1, new StatBlock(), type)[95].Should().Be(expected);
    }

    [Test]
    public void BuildProperty_NumericLayoutIsCorrect()
    {
        var p = GameStatPackets.BuildProperty(handle: 0x0A0B0C0D, "level", 42);

        p.Length.Should().Be(37);
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(0, 4)).Should().Be(37);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)).Should().Be(507);
        p[6].Should().Be(Checksum(p));
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7, 4)).Should().Be(0x0A0B0C0D);
        p[11].Should().Be(1);
        Encoding.ASCII.GetString(p, 12, 5).Should().Be("level");
        p[17].Should().Be(0);
        BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(28, 8)).Should().Be(42);
        p[36].Should().Be(0);
    }

    [Test]
    public void StringProperty_RoundTripsTheEpic73ClientInfoLayout()
    {
        const string clientInfo = "KGM=02,1,3|AKA=2,513,3";
        var response = GameStatPackets.BuildStringProperty(0x11223344, "client_info", clientInfo);

        BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(4, 2)).Should().Be(507);
        BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(7, 4)).Should().Be(0x11223344);
        response[11].Should().Be(0);
        Encoding.ASCII.GetString(response, 12, 11).Should().Be("client_info");
        Encoding.ASCII.GetString(response, 36, clientInfo.Length).Should().Be(clientInfo);
        response[^1].Should().Be(0);
        response[6].Should().Be(Checksum(response));

        var request = new byte[7 + 16 + clientInfo.Length + 1];
        Encoding.ASCII.GetBytes("client_info").CopyTo(request, 7);
        Encoding.ASCII.GetBytes(clientInfo).CopyTo(request, 23);

        GameStatPackets.TryReadSetProperty(request, out var name, out var value).Should().BeTrue();
        name.Should().Be("client_info");
        value.Should().Be(clientInfo);
    }
}
