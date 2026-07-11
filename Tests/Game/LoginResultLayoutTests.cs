using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class LoginResultLayoutTests
{
    // The real Epic 7.3 client used with Navislamia carries one extra 4-byte field between hairId
    // and name in TS_SC_LOGIN_RESULT (FaceTextureId). Without it the self-player name renders
    // truncated ("Freezeraid" -> "zeraid"). This locks the name at the calibrated wire offset.
    private const int CalibratedNameOffset = 82;

    [Test]
    public void LoginResult_NameLandsAtCalibratedOffset()
    {
        var result = new TS_SC_LOGIN_RESULT { Name = "Freezeraid" };

        var data = new Packet<TS_SC_LOGIN_RESULT>((ushort)GamePackets.TM_SC_LOGIN_RESULT, result).Data;

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
