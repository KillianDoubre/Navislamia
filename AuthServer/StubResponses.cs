using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Auth;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Upload;

namespace Navislamia.AuthServer;

public static class StubResponses
{
    private const int ChecksumOffset = 6;

    public static byte[] BuildAuthLoginResult()
    {
        var packet = new Packet<TS_AG_LOGIN_RESULT>(
            (ushort)AuthPackets.TS_AG_LOGIN_RESULT,
            new TS_AG_LOGIN_RESULT { Result = 0 });

        return PatchChecksum(packet.Data, packet.HeaderStruct);
    }

    public static byte[] BuildUploadLoginResult()
    {
        var packet = new Packet<TS_US_LOGIN_RESULT>(
            (ushort)UploadPackets.TS_US_LOGIN_RESULT,
            new TS_US_LOGIN_RESULT { Result = 0 });

        return PatchChecksum(packet.Data, packet.HeaderStruct);
    }

    private static byte[] PatchChecksum(byte[] data, Header header)
    {
        data[ChecksumOffset] = header.CalculateChecksum();
        return data;
    }
}
