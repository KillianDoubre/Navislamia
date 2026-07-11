using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.AuthServer.Protocol;
using Navislamia.AuthServer.Protocol.Packets;
using NUnit.Framework;

namespace Tests.AuthServer;

[TestFixture]
public class AuthProtocolTests
{
    [Test]
    public void Build_writes_7byte_header_with_size_id_and_checksum()
    {
        var payload = new byte[] { 1, 2, 3, 4 };

        var packet = AuthMessage.Build(10000, payload);

        packet.Length.Should().Be(7 + 4);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)10000);

        byte expectedChecksum = 0;
        for (var i = 0; i < 6; i++)
        {
            expectedChecksum += packet[i];
        }
        packet[6].Should().Be(expectedChecksum);
        packet.AsSpan(7).ToArray().Should().Equal(payload);
    }

    [Test]
    public void FromStruct_and_ToStruct_roundtrip_TS_AC_RESULT()
    {
        var packet = AuthMessage.FromStruct(
            (ushort)AuthClientPackets.TS_AC_RESULT,
            new TS_AC_RESULT(10010, 0, 5));

        var result = AuthMessage.ToStruct<TS_AC_RESULT>(packet);

        result.RequestMsgId.Should().Be((ushort)10010);
        result.Result.Should().Be((ushort)0);
        result.LoginFlag.Should().Be(5);
    }

    [Test]
    public void TS_AC_SERVER_LIST_serializes_count_and_length()
    {
        var list = new TS_AC_SERVER_LIST { LastLoginServerIdx = 1 };
        list.Servers.Add(new TS_SERVER_INFO
        {
            ServerIdx = 1,
            ServerName = "Navislamia",
            ServerIp = "127.0.0.1",
            ServerPort = 4515,
            UserRatio = 0,
            IsAdultServer = false
        });

        var packet = list.ToPacket();

        packet.Length.Should().Be(7 + 2 + 2 + TS_AC_SERVER_LIST.ServerInfoSize);
        var payload = AuthMessage.Payload(packet);
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2)).Should().Be((ushort)1);
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)).Should().Be((ushort)1);
    }

    [Test]
    public void TS_CA_RSA_PUBLIC_KEY_parses_size_and_key()
    {
        var key = new byte[] { 10, 20, 30 };
        var payload = new byte[4 + key.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), key.Length);
        key.CopyTo(payload.AsSpan(4));

        var parsed = TS_CA_RSA_PUBLIC_KEY.Parse(payload);

        parsed.KeySize.Should().Be(3);
        parsed.Key.Should().Equal(key);
    }
}
