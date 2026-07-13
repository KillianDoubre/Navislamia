using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class LoginResultLayoutTests
{
    private const int CalibratedNameOffset = 82;

    [Test]
    public void LoginResult_NameLandsAtCalibratedOffset()
    {
        var result = new TS_SC_LOGIN_RESULT
        {
            FaceId = 0x11223344,
            SkinColor = 0xAABBCCDD,
            FaceTextureId = 0x55667788,
            HairId = 0x01020304,
            Name = "Freezeraid"
        };

        var data = new Packet<TS_SC_LOGIN_RESULT>((ushort)GamePackets.TM_SC_LOGIN_RESULT, result).Data;

        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(66, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(70, 4)).Should().Be(0xAABBCCDD);
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(74, 4)).Should().Be(0x01020304);
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(78, 4)).Should().Be(0x55667788);
        IndexOf(data, Encoding.ASCII.GetBytes("Freezeraid")).Should().Be(CalibratedNameOffset);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
