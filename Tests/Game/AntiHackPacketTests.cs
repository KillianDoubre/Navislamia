using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class AntiHackPacketTests
{
    private const int HeaderSize = 7;

    [Test]
    public void AntiHack_IsDeclaredWithTheEpic73Identifier()
    {
        ((ushort)GamePackets.TM_CS_ANTI_HACK).Should().Be(54);
        Enum.IsDefined(typeof(GamePackets), (ushort)54).Should().BeTrue();
    }

    [Test]
    public void AntiHack_SizesAreTheFixedRzuFrame()
    {
        GameAntiHackPackets.AntiHackPacketSize.Should().Be(409);
        GameAntiHackPackets.AntiHackPacketSize.Should().Be(7 + sizeof(ushort) + 400);
        GameAntiHackPackets.AntiHackPayloadSize.Should().Be(402);
    }

    [Test]
    public void AntiHack_FrameLaysOutHeaderThenDeclaredLengthThenTheFixedBuffer()
    {
        var frame = BuildFrame(0x1234);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(409);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(54);
        frame[6].Should().Be(Checksum(frame));

        var header = new Header(frame);
        header.Length.Should().Be(409);
        header.ID.Should().Be(54);

        GameAntiHackPackets.TryReadAntiHack(frame, out var declaredLength).Should().BeTrue();
        declaredLength.Should().Be(0x1234);

        // nLength occupies offsets 7-8 and byBuffer offsets 9-408, so the fixed buffer is 400 bytes.
        frame[9..].Length.Should().Be(400);
        (HeaderSize + sizeof(ushort)).Should().Be(9);
    }

    [Test]
    public void AntiHack_ReaderRefusesAShortDatagramAndAcceptsAnExactOne()
    {
        GameAntiHackPackets.TryReadAntiHack(new byte[408], out var refused).Should().BeFalse();
        refused.Should().Be(0);

        GameAntiHackPackets.TryReadAntiHack(new byte[409], out var accepted).Should().BeTrue();
        accepted.Should().Be(0);

        // A declared length of 0 is a value, not an error: nothing here interprets it.
        GameAntiHackPackets.TryReadAntiHack(BuildFrame(0x0000), out var zero).Should().BeTrue();
        zero.Should().Be(0);

        var longer = new byte[410];
        BinaryPrimitives.WriteUInt16LittleEndian(longer.AsSpan(7, 2), 0x1234);
        GameAntiHackPackets.TryReadAntiHack(longer, out var fromLongerDatagram).Should().BeTrue();
        fromLongerDatagram.Should().Be(0x1234);
    }

    [Test]
    public void AntiHack_IsConsumedBeforeTheFinalDispatchSwitch()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Game", "Network", "Clients",
            "GameClient.cs"));

        var armIndex = source.IndexOf("(ushort)GamePackets.TM_CS_ANTI_HACK", StringComparison.Ordinal);
        var switchIndex = source.IndexOf("IPacket msg = header.ID switch", StringComparison.Ordinal);

        armIndex.Should().BeGreaterThan(-1, "GameClient must declare the anti-cheat arm");
        switchIndex.Should().BeGreaterThan(-1, "the final dispatch switch must still be the last resort");
        armIndex.Should().BeLessThan(switchIndex,
            "otherwise the datagram would reach the final switch and throw on an unknown packet type");
    }

    [Test]
    public void AntiHack_ArmOnlyConsumesTheDatagramAndStaysOutOfTheNoReplyGroup()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Game", "Network", "Clients",
            "GameClient.cs"));

        var armIndex = source.IndexOf("(ushort)GamePackets.TM_CS_ANTI_HACK", StringComparison.Ordinal);
        armIndex.Should().BeGreaterThan(-1);
        var arm = source[armIndex..source.IndexOf("continue;", armIndex, StringComparison.Ordinal)];

        arm.Should().NotContain("SendResult");
        arm.Should().NotContain("SendMessage");
        arm.Should().NotContain("Disconnect");

        var groupIndex = source.IndexOf("GamePackets.TM_CS_UPDATE or", StringComparison.Ordinal);
        groupIndex.Should().BeGreaterThan(-1);
        var noReplyGroup =
            source[groupIndex..source.IndexOf("continue;", groupIndex, StringComparison.Ordinal)];

        noReplyGroup.Should().NotContain("TM_CS_ANTI_HACK",
            "the settled \"valid, no reply expected\" disposition does not apply to the anti-cheat arm");
    }

    private static byte[] BuildFrame(ushort declaredLength)
    {
        var frame = new byte[GameAntiHackPackets.AntiHackPacketSize];

        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_ANTI_HACK);
        frame[6] = Checksum(frame);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7, 2), declaredLength);

        return frame;
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        return checksum;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root (Navislamia.sln) must be reachable from the test output");

        return directory!.FullName;
    }
}
