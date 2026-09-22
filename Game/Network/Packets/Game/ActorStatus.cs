using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The single place that composes the whole <c>status</c> mask of an actor, by nature of actor,
/// for the two frames that carry it: the creature information of <c>TM_SC_ENTER</c> (3, offset 26)
/// and <c>TM_SC_STATUS_CHANGE</c> (500, offset 11).
///
/// The field is an instant-snapshot of every flag the actor carries, never a delta — NGemity
/// recomputes it in <c>Messages::GetStatusCode</c> on every broadcast
/// (<c>Chihiro/src/Network/Messages.cpp:557-620</c>) and each of NavisLamia's send sites used to
/// pass a bare bit or a literal <c>0</c>. Routing every site through here is what keeps a second
/// flag from silently clearing the first.
/// </summary>
public static class ActorStatus
{
    /// <summary>
    /// A player's mask. <paramref name="pkModeOn"/> is <c>TCS_FlagPkOn</c>
    /// (<see cref="CreatureStatus.PlayerPkOn"/>); <paramref name="sitting"/>, <paramref name="battleMode"/>
    /// and <paramref name="walking"/> are the three states the GM commands <c>/sitdown</c>, <c>/battle</c>
    /// and <c>/walk</c> toggle (docs/gm-commands.md). Booths, bloody and demoniac states have no
    /// established server-side rule yet. Every flag is passed on every send: the mask is a snapshot.
    /// </summary>
    public static uint ForPlayer(bool pkModeOn, bool sitting = false, bool battleMode = false,
        bool walking = false)
    {
        var status = 0u;
        if (pkModeOn)
        {
            status |= CreatureStatus.PlayerPkOn;
        }

        if (sitting)
        {
            status |= CreatureStatus.PlayerSitdown;
        }

        if (battleMode)
        {
            status |= CreatureStatus.BattleMode;
        }

        if (walking)
        {
            status |= CreatureStatus.PlayerWalking;
        }

        return status;
    }

    /// <summary>
    /// A monster's mask. <paramref name="dead"/> is the corpse flag; the corpse outlives the death
    /// and its <c>TM_SC_LEAVE</c> is deferred by the combat tick.
    /// </summary>
    public static uint ForMonster(bool dead = false) => dead ? CreatureStatus.MonsterDead : 0u;

    /// <summary>An NPC's mask: this Epic 7.3 client has no NPC flag in use.</summary>
    public static uint ForNpc() => 0u;
}
