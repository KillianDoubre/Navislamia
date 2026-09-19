using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The pure decisions behind a player's resurrection, kept out of <see cref="ResurrectionService"/>
/// so the refusals and the restored vitals can be tested without a socket. The packet layout is in
/// <c>GameActionPackets.TryReadResurrection</c>; see docs/packet-specs/socle-mort-respawn.md.
/// </summary>
public static class ResurrectionRules
{
    /// <summary>
    /// Validates a <c>TM_CS_RESURRECTION</c> request against the connected character, in the order of
    /// the specification's §5.3.
    /// </summary>
    /// <remarks>
    /// Only the town path (<see cref="ResurrectionType.UseNone"/>) is implemented: a state, item,
    /// competition or deathmatch request is refused with <see cref="ResultCode.NotActable"/> and
    /// changes nothing, those paths waiting for their own lot (§12.2). A session with no character in
    /// the world is refused the same way: it has no vitals and nothing to resurrect.
    /// </remarks>
    public static ResultCode CheckRequest(uint handle, ResurrectionType type, uint characterHandle,
        int characterHp)
    {
        if (characterHandle == 0)
        {
            return ResultCode.NotActable;
        }

        if (handle != characterHandle)
        {
            return ResultCode.NotOwn;
        }

        if (characterHp > 0)
        {
            return ResultCode.NotActable;
        }

        if (type != ResurrectionType.UseNone)
        {
            return ResultCode.NotActable;
        }

        return ResultCode.Success;
    }

    /// <summary>
    /// The vitals a town respawn restores: the full maxima. No local reference gives the real amount
    /// (the reference's <c>revive_in_town</c> is a Lua script absent from the clone), and this is the
    /// value the specification proposes by default in §16.3 — the same full restore a level-up
    /// applies in <see cref="LevelingService"/>.
    /// </summary>
    public static (int Hp, int Mp) RestoredVitals(float maxHp, float maxMp) => ((int)maxHp, (int)maxMp);
}
