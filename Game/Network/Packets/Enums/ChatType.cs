namespace Navislamia.Game.Network.Packets.Enums;

public enum ChatType : byte
{
    Normal = 0x00,
    Yell = 0x01,
    Whisper = 0x03,
    Global = 0x04,
    Party = 0x0A,
    Guild = 0x0B,

    /// <summary><c>CHAT_NOTICE</c>, the server-wide announcement line (<c>/notice</c>).</summary>
    Notice = 0x14,

    /// <summary>
    /// <c>CHAT_EXP</c> (0x1E) in rzu and NGemity: the system line NGemity answers its commands on,
    /// sender <c>@SYSTEM</c> (<c>AllowedCommandInfo.cpp</c>, <c>onCheatPosition</c>).
    /// </summary>
    System = 0x1E,
}
