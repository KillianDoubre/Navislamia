using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The pure rules of taming and summoning, from the official server source
/// (docs/packet-specs/socle-apprivoisement-invocation.md §15): <c>GameProc.cpp</c> (<c>SetTamer</c>,
/// <c>ClearTamer</c>, <c>ProcTame</c>, <c>AllocNewSummon</c>), <c>StructPlayer::EquipSummon</c>,
/// <c>StructSkill</c>'s taming checks, <c>GameRule.h</c>.
/// </summary>
public static class CreatureRules
{
    /// <summary><c>Skill 1801</c>, Creature Control: its level is the number of formation slots, at most 6.</summary>
    public const int CreatureControlSkill = 1801;

    public const int MaxSlots = 6;

    /// <summary>
    /// <c>GameRule::TAMING_INTERVAL = 30000</c> ar_time ticks — 5 minutes, refreshed by every hit of the tamer
    /// (<c>StructMonster::onDamage</c>); past it the tamer is cleared (<c>MonsterAI.cpp:968</c>).
    /// </summary>
    public const uint TamingIntervalTicks = 30000;

    /// <summary><c>RANGE_LIMIT = 500</c> (<c>Extern.h</c>): a tamer farther than this from the corpse fails.</summary>
    public const float TamingRange = 500;

    /// <summary>The summon name field: 18 usable characters (<c>GameSummonPackets.NameSize - 1</c>).</summary>
    public const int MaxNameLength = 18;

    public static uint SummonCardMask => TamingRules.SummonCardMask;

    /// <summary>
    /// <c>ITEM_CODE_MIRROR_OF_TAMING_CARD_ON_TEST</c>, <c>…_CARD</c> and <c>…_TRADABLE</c>, in the order
    /// <c>ProcTame</c> looks for them: the first one found is broken by the draw and protects the card from a failure.
    /// </summary>
    public static readonly int[] MirrorOfTamingCards = { 9000111, 960019, 960021 };

    public static ItemFlag WithSummonFlag(ItemFlag flag) =>
        (ItemFlag)unchecked((int)(Raw(flag) | TamingRules.SummonCardMask));

    public static bool IsBound(ItemFlag flag) => TamingRules.IsBoundSummonCard(flag);

    private static uint Raw(ItemFlag flag) => flag == ItemFlag.None ? 0u : unchecked((uint)flag);

    /// <summary>The slots a Creature Control level opens.</summary>
    public static int SlotCount(int creatureControlLevel) => Math.Clamp(creatureControlLevel, 0, MaxSlots);

    /// <summary>
    /// <c>ProcTame</c>: <c>c_fixed10 p = taming_percentage × (var0 × level + var1 × enhance + 1) × 100</c>, a
    /// fixed-point value of factor 10 000, against <c>XRandom(1, 1 000 000)</c> — it fails when
    /// <c>p.get() &lt; roll</c>. So the chance is <c>taming_percentage × (var0 × level + var1 × enhance + 1)</c>.
    /// </summary>
    public static bool TamingSucceeds(decimal tamingPercentage, decimal var0, decimal var1, int skillLevel,
        int enhance, int roll)
    {
        var fixedValue = (long)decimal.Floor(tamingPercentage * (var0 * skillLevel + var1 * enhance + 1) * 100 * 10000);
        return fixedValue >= roll;
    }

    /// <summary>
    /// A summon's stats at a level (<see cref="SummonProgression.Stats"/>); without its master's context, the
    /// summon is taken as its master's level, with no Creature Mastery and no card enhance.
    /// </summary>
    public static StatBlock SummonStats(SummonResourceInfo summon, int level) =>
        SummonProgression.Stats(summon, level, new SummonStatContext(Math.Max(1, level)));

    public static StatBlock SummonStats(SummonResourceInfo summon, int level, SummonStatContext context) =>
        SummonProgression.Stats(summon, level, context);

    /// <summary>
    /// The reach between a summon and a monster: <c>12 × attack_range</c> plus both body radii
    /// (<c>Unit::GetRealAttackRange</c>, <c>Object::GetUnitSize</c>), the rule <see cref="CombatRange"/> uses.
    /// </summary>
    public static float SummonReach(SummonResourceInfo summon, float monsterSize, float monsterScale) =>
        12f * summon.AttackRange + (CombatRange.UnitSize(summon.Size, summon.Scale)
                                    + CombatRange.UnitSize(monsterSize, monsterScale)) * 0.5f;

    public static string TrimName(string name) =>
        string.IsNullOrEmpty(name) ? "Creature" : name.Length <= MaxNameLength ? name : name[..MaxNameLength];

    /// <summary>
    /// <c>StructPlayer::EquipSummon</c>. A bound card the request no longer lists is unbound, unless its summon is
    /// in the world, which fails the whole formation (null: the client gets its formation back unchanged). A
    /// newly listed card is bound only when <paramref name="canBind"/> accepts it (owned, in the bag, a summon
    /// card carrying the summon flag); a card already bound keeps its place wherever it moves. A card listed
    /// twice is bound once, and slots past the Creature Control level stay empty.
    /// </summary>
    /// <summary>
    /// Why the cards asked for and left out of <paramref name="resolved"/> were left out, in the order of
    /// <c>StructPlayer::EquipSummon</c>'s checks: a slot beyond the Creature Control level, a card that is not tamed
    /// (no <c>ITEM_FLAG_SUMMON</c>: an empty card), or a summon that could not be allocated.
    /// </summary>
    public static IReadOnlyList<(uint Card, string Reason)> FormationRefusals(IReadOnlyList<uint> requested,
        IReadOnlyList<long> resolved, int slotCount, Func<long, bool> isBound)
    {
        var refusals = new List<(uint, string)>();
        for (var i = 0; i < requested.Count; i++)
        {
            var card = requested[i];
            if (card == 0 || System.Linq.Enumerable.Contains(resolved, (long)card)) continue;
            refusals.Add((card, i >= slotCount
                ? $"slot {i + 1} is beyond the {slotCount} slot(s) of Creature Control"
                : isBound(card) ? "its summon could not be allocated" : "the card is not tamed (no ITEM_FLAG_SUMMON)"));
        }

        return refusals;
    }

    public static long[] ResolveFormation(IReadOnlyList<long> current, IReadOnlyList<uint> requested, int slotCount,
        Func<long, bool> canBind, Func<long, bool> isInWorld)
    {
        var bound = new HashSet<long>();
        for (var i = 0; i < MaxSlots; i++)
        {
            var card = i < current.Count ? current[i] : 0;
            if (card != 0)
            {
                bound.Add(card);
            }
        }

        var wanted = new HashSet<long>();
        for (var i = 0; i < Math.Min(slotCount, requested.Count); i++)
        {
            if (requested[i] != 0)
            {
                wanted.Add(requested[i]);
            }
        }

        foreach (var card in bound)
        {
            if (!wanted.Contains(card) && isInWorld(card))
            {
                return null;
            }
        }

        var result = new long[MaxSlots];
        var placed = new HashSet<long>();
        for (var i = 0; i < slotCount && i < requested.Count; i++)
        {
            long card = requested[i];
            if (card == 0 || !placed.Add(card))
            {
                continue;
            }

            if (bound.Contains(card) || canBind(card))
            {
                result[i] = card;
            }
            else
            {
                placed.Remove(card);
            }
        }

        return result;
    }
}
