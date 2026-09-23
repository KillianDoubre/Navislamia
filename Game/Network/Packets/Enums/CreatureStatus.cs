namespace Navislamia.Game.Network.Packets.Enums;

/// <summary>
/// Bits of the <c>status</c> field the server publishes in the creature information of
/// <c>TS_SC_ENTER</c> (3, offset 26) and in <c>TS_SC_STATUS_CHANGE</c> (500, offset 11).
///
/// Sources: rzu <c>librzu/src/packets/GameClient/TS_SC_STATUS_CHANGE.h:8-38</c>
/// (<c>TS_CREATURE_STATUS</c>), cross-checked against NGemity <c>shared/Server/TS_MESSAGE.h:30-47</c>
/// (<c>TS_PLAYER_FLAG</c>) and the Epic 7.3 client's own bit tests
/// (<c>SFrame.exe+0x506211-0x5062a0</c>). See <c>docs/packet-specs/socle-mode-pk.md</c> §5.1.
///
/// The mask is a **complete snapshot of the actor, never a delta**: sending one isolated bit clears
/// every other bit the actor carries. Never send a bare bit — compose the mask with
/// <see cref="Game.ActorStatus"/>.
/// </summary>
public static class CreatureStatus
{
    /// <summary>Flag battle mode, every actor nature (rzu <c>TCS_FlagBattleMode</c>).</summary>
    public const uint BattleMode = 1 << 0;

    /// <summary>Invisible, every actor nature (rzu <c>TCS_FlagInvisible</c>).</summary>
    public const uint Invisible = 1 << 1;

    /// <summary>
    /// <c>1 &lt;&lt; 8</c>: "dead" for a monster or an NPC (rzu <c>TCS_FlagDead</c>). The same bit
    /// means "sit down" on a player handle (see <see cref="PlayerSitdown"/>), so a dead mask must
    /// never be published on a player.
    /// </summary>
    public const uint MonsterDead = 1 << 8;

    /// <summary>Player sitting down — the player meaning of the same bit as <see cref="MonsterDead"/>.</summary>
    public const uint PlayerSitdown = 1 << 8;

    /// <summary>Player personal shop, buy side (rzu <c>TCS_FlagBuyBooth</c>).</summary>
    public const uint PlayerBuyBooth = 1 << 9;

    /// <summary>Player personal shop, sell side (rzu <c>TCS_FlagSellBooth</c>).</summary>
    public const uint PlayerSellBooth = 1 << 10;

    /// <summary>
    /// The PK mode — the only PK flag of the protocol (rzu <c>TCS_FlagPkOn</c>, NGemity
    /// <c>FLAG_PK_ON</c>). There is no server packet of its own: this bit is how the state reaches
    /// the client, which tests it at <c>SFrame.exe+0x68b006</c> to choose between
    /// <c>TM_CS_TURN_ON_PK_MODE</c> (800) and <c>TM_CS_TURN_OFF_PK_MODE</c> (801), and colours the
    /// name and the icon of the bearer from it.
    /// </summary>
    public const uint PlayerPkOn = 1 << 11;

    /// <summary>Player criminal state (rzu <c>TCS_FlagBloody</c>). No threshold is established.</summary>
    public const uint PlayerBloody = 1 << 12;

    /// <summary>Player demoniac state (rzu <c>TCS_FlagDemoniac</c>). No threshold is established.</summary>
    public const uint PlayerDemoniac = 1 << 13;

    /// <summary>Game master (rzu <c>TCS_FlagGm</c>).</summary>
    public const uint PlayerGm = 1 << 14;

    /// <summary>Walking, as opposed to running (rzu <c>TCS_FlagWalking</c>).</summary>
    public const uint PlayerWalking = 1 << 16;

    /// <summary>Competing in a battle arena (rzu <c>TCS_FlagCompeting</c>).</summary>
    public const uint PlayerCompeting = 1 << 21;

    // Deliberately absent: the dungeon owner and siege bits (1 << 15, 1 << 17) and the arena team
    // bits (1 << 22, 1 << 23). rzu lists them under "Last tested: EPIC_9_8_1" and their presence in
    // the Epic 7.3 client was not established — see docs/packet-specs/socle-mode-pk.md §12.5.
}
