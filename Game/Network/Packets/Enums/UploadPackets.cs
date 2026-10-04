namespace Navislamia.Game.Network.Packets.Enums;

public enum UploadPackets : ushort
{
    TS_SU_LOGIN = 50001,
    TS_US_LOGIN_RESULT = 50002,
    TM_SU_REQUEST_UPLOAD = 50003,
    TM_US_REQUEST_UPLOAD = 50004,
    TM_US_UPLOAD = 50009
}
