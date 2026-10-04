namespace Navislamia.Game.Network.Packets.Enums;

public enum ChatType : byte
{
    Normal = 0x00,
    Yell = 0x01,
    Whisper = 0x03,
    Global = 0x04,
    Party = 0x0A,
    Guild = 0x0B,
    AttackTeam = 12,

    /// <summary><c>CHAT_FRIEND</c> (13): the friend window's printed lines, sender <c>@FRIEND</c>.</summary>
    Friend = 13,

    /// <summary><c>CHAT_FRIEND_SYSTEM</c> (140): the lines the friend window parses (<c>FLIST</c>, <c>DLIST</c>, <c>FSTATUS</c>).</summary>
    FriendSystem = 140,
    GuildSystem = 110,
    RaidSystem = 130,
    AllianceSystem = 150,

    /// <summary><c>CHAT_NOTICE</c>, the server-wide announcement line (<c>/notice</c>).</summary>
    Notice = 0x14,

    /// <summary>
    /// 100: the system line of party events (<c>@PARTY</c>), which the
    /// client parses instead of printing — the official server's <c>SendChatMessage(…, 100, "@PARTY", …)</c>.
    /// </summary>
    PartySystem = 0x64,

    /// <summary>
    /// <c>CHAT_EXP</c> (0x1E) in rzu and NGemity: the system line NGemity answers its commands on,
    /// sender <c>@SYSTEM</c> (<c>AllowedCommandInfo.cpp</c>, <c>onCheatPosition</c>).
    /// </summary>
    System = 0x1E,

    /// <summary><c>CHAT_ITEM</c> (32): the item line, sender <c>@SYSTEM</c> (the official server's item messages).</summary>
    Item = 0x20,
}
