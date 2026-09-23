using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

/// <summary>
/// The pure decisions behind a player's resurrection, kept out of <see cref="ResurrectionService"/>
/// so the refusals and the restored vitals can be tested without a socket. The packet layout is in
/// <c>GameActionPackets.TryReadResurrection</c>; see docs/packet-specs/socle-mort-respawn.md.
/// </summary>
public static class ResurrectionRules
{
    /// <summary>
    /// Validates a <c>TM_CS_RESURRECTION</c> request against the connected character.
    /// </summary>
    /// <remarks>
    /// The town path (<see cref="ResurrectionType.UseNone"/>), the state path
    /// (<see cref="ResurrectionType.UseState"/>) and the item path (<see cref="ResurrectionType.UsePotion"/>,
    /// docs/packet-specs/socle-effets-resurrection.md) are implemented. The competition and the deathmatch
    /// are refused with <see cref="ResultCode.NotActable"/> and change nothing: duels and deathmatch
    /// instances do not exist. A session with no character in the world is refused the same way: it has
    /// no vitals and nothing to resurrect.
    /// <para>
    /// <b>The frame's <c>handle</c> is not checked.</b> NGemity's town path never reads it
    /// (<c>WorldSession::onRevive</c> revives <c>m_pPlayer</c>), and its state path reads it only to tell
    /// the player from one of its summons. No summon exists here, so whatever the client sends can only
    /// designate the connected character. The first cut refused any handle other than
    /// <c>CharacterHandle</c> with <c>NotOwn</c> — a rule of this repository, not of the reference — and the
    /// 7.3 client's town button was refused in game (result 3, 2026-09-23): it does not send that value.
    /// </para>
    /// </remarks>
    public static ResultCode CheckRequest(ResurrectionType type, uint characterHandle, int characterHp)
    {
        if (characterHandle == 0)
        {
            return ResultCode.NotActable;
        }

        if (characterHp > 0)
        {
            return ResultCode.NotActable;
        }

        if (type is not (ResurrectionType.UseNone or ResurrectionType.UseState or ResurrectionType.UsePotion))
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

    /// <summary>
    /// The resurrection state a dead character comes back with: among its active states, the one of
    /// highest level that <paramref name="resolve"/> knows as a resurrection state — NGemity keeps the
    /// highest level too (<c>Unit::ResurrectByState</c>). False when none is active, which the reference
    /// answers with <c>NotActable</c>.
    /// </summary>
    public static bool TrySelectState(IEnumerable<ActiveBuff> activeStates,
        TryResolveResurrection resolve, out ActiveBuff state, out ResurrectionStateValues values)
    {
        state = default;
        values = default;
        var found = false;
        foreach (var candidate in activeStates ?? Array.Empty<ActiveBuff>())
        {
            if (!resolve(candidate.StateId, out var candidateValues))
            {
                continue;
            }

            if (!found || candidate.StateLevel > state.StateLevel)
            {
                state = candidate;
                values = candidateValues;
                found = true;
            }
        }

        return found;
    }

    public delegate bool TryResolveResurrection(int stateId, out ResurrectionStateValues values);

    /// <summary>
    /// The vitals a resurrection state gives back, NGemity's formula: HP is
    /// <c>(value_0 + value_1 × level) × max HP</c> added to the 0 HP of the corpse, MP is
    /// <c>(value_2 + value_3 × level) × max MP</c> added to what the character kept. HP is floored at 1,
    /// a choice of this repository borrowed from the reference's own <c>Unit::Resurrect</c>
    /// (<c>AddHealth(std::max(nIncHP, 1))</c>): a resurrection that left 0 HP would leave the character
    /// dead. Both are capped at their maxima.
    /// </summary>
    /// <summary>
    /// The vitals a resurrection skill gives back, NGemity's formulas verbatim (<c>Skill.cpp</c>):
    /// <list type="bullet">
    /// <item><c>EF_RESURRECTION</c> (504, <c>SKILL_RESURRECTION</c>): HP <c>max HP × var0 × level</c>, MP
    /// <c>max MP × var1 × level</c>;</item>
    /// <item><c>EF_RESURRECTION_WITH_RECOVER</c> (30501): HP <c>max HP × (var0 + var1 × level)</c>, MP
    /// <c>max MP × (var2 + var3 × level)</c> — the enhancement terms (<c>var9</c>, <c>var10</c>) are zero,
    /// enhancement is not modelled.</item>
    /// </list>
    /// <c>var0</c> is <c>Values[0]</c>, the <c>var1</c> column. HP floored at 1 and MP added to what the
    /// character kept, capped at the maxima: the same bounds as <see cref="VitalsByState"/>.
    /// </summary>
    public static (int Hp, int Mp) VitalsBySkill(SkillEffectType effect, decimal[] vars, int skillLevel,
        float maxHp, float maxMp, int currentMp)
    {
        decimal Var(int index) => vars is not null && index < vars.Length ? vars[index] : 0m;

        var (hpRatio, mpRatio) = effect == SkillEffectType.ResurrectionWithRecover
            ? (Var(0) + Var(1) * skillLevel, Var(2) + Var(3) * skillLevel)
            : (Var(0) * skillLevel, Var(1) * skillLevel);

        var hp = (int)Math.Clamp(hpRatio * (decimal)maxHp, 1m, Math.Max(1m, (decimal)maxHp));
        var mp = (int)Math.Clamp(Math.Max(0, currentMp) + mpRatio * (decimal)maxMp, 0m,
            Math.Max(0m, (decimal)maxMp));
        return (hp, mp);
    }

    public static (int Hp, int Mp) VitalsByState(ResurrectionStateValues values, int stateLevel, float maxHp,
        float maxMp, int currentMp)
    {
        var hp = (int)Math.Clamp(values.HpRatio(stateLevel) * (decimal)maxHp, 1m, Math.Max(1m, (decimal)maxHp));
        var gainedMp = values.MpRatio(stateLevel) * (decimal)maxMp;
        var mp = (int)Math.Clamp(Math.Max(0, currentMp) + gainedMp, 0m, Math.Max(0m, (decimal)maxMp));
        return (hp, mp);
    }
}
