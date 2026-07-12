namespace Navislamia.Game.Network.Packets.Enums;

public enum ChatType : byte
{
    Normal = 0x00,
    Yell = 0x01,
    Whisper = 0x03,
    Global = 0x04,
    Party = 0x0A,
    Guild = 0x0B,
}
