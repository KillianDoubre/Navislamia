using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.AuthServer;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Auth;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Upload;
using NUnit.Framework;

namespace Tests.AuthServer;

[TestFixture]
public class StubResponsesTests
{
    [Test]
    public void BuildAuthLoginResult_has_valid_header_checksum_and_result_zero()
    {
        var data = StubResponses.BuildAuthLoginResult();

        var expectedLength = 7 + Marshal.SizeOf<TS_AG_LOGIN_RESULT>();
        data.Length.Should().Be(expectedLength);

        var header = new Header(data);
        header.ID.Should().Be((ushort)AuthPackets.TS_AG_LOGIN_RESULT);
        header.Length.Should().Be((uint)expectedLength);
        data[6].Should().Be(header.CalculateChecksum());

        var packet = new Packet<TS_AG_LOGIN_RESULT>(data);
        packet.GetDataStruct<TS_AG_LOGIN_RESULT>().Result.Should().Be(0);
    }

    [Test]
    public void BuildUploadLoginResult_has_valid_header_checksum_and_result_zero()
    {
        var data = StubResponses.BuildUploadLoginResult();

        var expectedLength = 7 + Marshal.SizeOf<TS_US_LOGIN_RESULT>();
        data.Length.Should().Be(expectedLength);

        var header = new Header(data);
        header.ID.Should().Be((ushort)UploadPackets.TS_US_LOGIN_RESULT);
        header.Length.Should().Be((uint)expectedLength);
        data[6].Should().Be(header.CalculateChecksum());

        var packet = new Packet<TS_US_LOGIN_RESULT>(data);
        packet.GetDataStruct<TS_US_LOGIN_RESULT>().Result.Should().Be(0);
    }
}
