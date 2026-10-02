using System;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Creatures;

/// <summary>What a summon's stats depend on besides its resource and level.</summary>
/// <param name="MasterLevel">The master's level (<c>m_fBattleLevel</c>, the accuracy correction, the stat penalty).</param>
/// <param name="CreatureMasteryLevel">The master's Creature Mastery (1811) level: +0.03 FCM each.</param>
/// <param name="StatAmplify"><c>CreatureEnhance.stat_amplify</c> of the card's enhance level.</param>
public readonly record struct SummonStatContext(int MasterLevel, int CreatureMasteryLevel = 0, float StatAmplify = 0);

/// <summary>A summon's progression after an exp change: its level, max reached level and JP.</summary>
public readonly record struct SummonLevelChange(int Level, int MaxReachedLevel, int JpGained)
{
    public bool LevelChanged(int previous) => Level != previous;
}

/// <summary>
/// The pure rules of a summon's stats and growth, from the official server
/// (docs/packet-specs/socle-invocations-progression.md): <c>StructSummon::AddExp</c>/<c>onExpChange</c>/
/// <c>GetDeadEXPPenalty</c>, <c>StructSummon::onBeforeCalculateStat</c>/<c>applyJobLevelBonus</c>/
/// <c>onAfterApplyStat</c>/<c>calcAttribute</c>, <c>StructCreature::calcAttribute</c> and <c>GameRule.h</c>.
/// </summary>
public static class SummonProgression
{
    public const int NormalMaxLevel = 60;
    public const int GrowthMaxLevel = 115;
    public const int EvolveMaxLevel = 170;
    public const int NormalEvolveLevel = 50;
    public const int GrowthEvolveLevel = 100;
    public const int MaxSummonLevel = 170;

    /// <summary><c>GameRule::VISIBLE_RANGE</c>: a summon farther than this from its master gets no hunting exp.</summary>
    public const float VisibleRange = 525;

    /// <summary><c>SKILL_CREATURE_MASTERY</c>, the master's passive that raises the creature coefficient.</summary>
    public const int CreatureMasterySkill = 1811;

    /// <summary><c>m_fCM.set(7000)</c>: a summon's creature coefficient is 0.7, +0.03 per Creature Mastery level.</summary>
    public static float CreatureCoefficient(int creatureMasteryLevel) => 0.7f + 0.03f * Math.Max(0, creatureMasteryLevel);

    public static int MaxLevel(int form) => form switch { 2 => GrowthMaxLevel, 3 => EvolveMaxLevel, _ => NormalMaxLevel };

    /// <summary>The level past which a level gives a second JP (overbreed); an evolved summon has none.</summary>
    public static int EvolvableLevel(int form) => form switch { 2 => GrowthEvolveLevel, 3 => EvolveMaxLevel, _ => NormalEvolveLevel };

    /// <summary><c>GameRule::GetSummonEXPLimit</c>: the most a summon of a level takes from one gain.</summary>
    public static long ExpLimit(int level) => (long)(Math.Pow(level, 2.0) * 200);

    /// <summary><c>GameRule::GetPlayerEXPLimit</c>: what a player gets from one gain while a summon is above them.</summary>
    public static long PlayerExpLimit(int level) => (long)(Math.Pow(level, 1.8) * 30.0) + 240;

    /// <summary><c>GameRule::GetSummonLevelPenalty</c>: added to the master's level for the battle level.</summary>
    public static float LevelPenalty(int masterLevel, int summonLevel)
    {
        var gap = summonLevel - masterLevel;
        if (gap <= 0)
        {
            return 0;
        }

        return gap >= 30 ? 10f : (int)(gap * 0.25f * (2.34f - gap / 30.0f) * 10.0f) / 10.0f;
    }

    /// <summary><c>GameRule::GetSummonStatPenalty</c>: the seven stats of a summon above its master shrink.</summary>
    public static float StatPenalty(int masterLevel, int summonLevel)
    {
        var gap = summonLevel - masterLevel;
        if (gap <= 0)
        {
            return 1f;
        }

        return gap >= 50 ? 0.7f : ((int)((50.0f - gap) * 0.6f) + 70.0f) / 100.0f;
    }

    /// <summary>
    /// <c>m_fBattleLevel</c>: the master's level plus the penalty of a summon above it — the level every attribute
    /// formula reads, not the summon's own.
    /// </summary>
    public static float BattleLevel(int masterLevel, int summonLevel) =>
        masterLevel + LevelPenalty(masterLevel, summonLevel);

    /// <summary>
    /// <c>GameContent::GetSummonLevelBonus</c>: <c>bonus × level</c> for a first form, <c>bonus × (level + 1 −
    /// (form − 1) × 50)</c> beyond, each stat truncated.
    /// </summary>
    public static float LevelBonus(float perLevel, int form, int level) => form <= 1
        ? (int)(perLevel * level)
        : (int)(perLevel * (level + 1 - (form - 1) * 50));

    /// <summary>
    /// The official <c>CalculateStat</c> of a summon, without items, passives or states (those are folded in after,
    /// <see cref="SummonBuffStats"/>): base stats (<c>stat_id</c>), the card's enhance amplifier, the level bonus,
    /// the stat penalty, then every attribute from the creature coefficient and the battle level
    /// (<c>StructCreature::calcAttribute</c>, <c>GameRule::GetMaxHP</c>…) and the accuracy correction of
    /// <c>StructSummon::calcAttribute</c>.
    /// </summary>
    public static StatBlock Stats(SummonResourceInfo summon, int level, SummonStatContext context)
    {
        level = Math.Max(1, level);
        var masterLevel = context.MasterLevel > 0 ? context.MasterLevel : level;
        var block = new StatBlock();
        if (summon.BaseStats is { } stats)
        {
            StatCalculator.ApplyBaseStats(stats, block);
        }

        // onBeforeCalculateStat: the enhance amplifier on the base stats.
        var amp = 1f + context.StatAmplify;
        Scale(block, amp);

        // applyJobLevelBonus.
        if (summon.LevelBonus is { Length: 7 } bonus)
        {
            var form = Math.Max(1, summon.Form);
            block.Strength += LevelBonus(bonus[0], form, level);
            block.Vitality += LevelBonus(bonus[1], form, level);
            block.Dexterity += LevelBonus(bonus[2], form, level);
            block.Agility += LevelBonus(bonus[3], form, level);
            block.Intelligence += LevelBonus(bonus[4], form, level);
            block.Wisdom += LevelBonus(bonus[5], form, level);
            block.Luck += LevelBonus(bonus[6], form, level);
        }

        // onAfterApplyStat.
        Scale(block, StatPenalty(masterLevel, level));

        var fcm = CreatureCoefficient(context.CreatureMasteryLevel);
        var battle = BattleLevel(masterLevel, level);
        var correction = (2 * masterLevel - level) / level;

        block.CriticalPower = 80;
        block.Critical = block.Luck * 0.2f + 3;
        block.AttackPointRight = block.Strength * 2.8f * fcm + battle;
        block.MagicPoint = block.Intelligence * 2 * fcm + battle;
        block.ItemChance = block.Luck * 0.2f;
        block.Defence = block.Vitality * 1.6f * fcm + battle;
        block.MagicDefence = block.Wisdom * 2 * fcm + battle;
        block.AccuracyRight = block.Dexterity * 0.5f * fcm + battle + correction;
        block.AccuracyLeft = block.AccuracyRight;
        block.MagicAccuracy = (block.Wisdom * 0.4f + block.Dexterity * 0.1f) * fcm + battle + correction;
        block.Avoid = block.Agility * 0.5f * fcm + battle;
        block.MagicAvoid = block.Wisdom * 0.5f * fcm + battle;
        block.AttackSpeed = block.Agility * 0.1f + 100;
        block.CastingSpeed = 100;
        block.CoolTimeSpeed = 100;
        block.HpRegenPercentage = (int)(fcm * 5);
        block.HpRegenPoint = (int)(battle * 2 + 48);
        block.MpRegenPercentage = (int)(fcm * 5);
        block.MpRegenPoint = (int)(battle * 2 + 48 + block.Wisdom * 4.1f);
        block.PerfectBlock = 20;
        block.MaxHp = (int)(block.Vitality * fcm * 33 + battle * 20);
        block.MaxMp = (int)(block.Intelligence * fcm * 30 + battle * 20);

        // onBeforeCalculateStat: a summon walks at its own run_speed (120 + run_speed − 120).
        block.MoveSpeed = Math.Max(10f, summon.RunSpeed > 0 ? summon.RunSpeed : 120);
        block.AttackRange = summon.AttackRange;
        return block;
    }

    private static void Scale(StatBlock block, float factor)
    {
        block.Strength *= factor;
        block.Vitality *= factor;
        block.Dexterity *= factor;
        block.Agility *= factor;
        block.Intelligence *= factor;
        block.Wisdom *= factor;
        block.Luck *= factor;
    }

    /// <summary>
    /// <c>StructSummon::AddExp</c>: the hunting gain of a summon, capped by its level's limit (when it has a master
    /// and the gain is not forced) and by the exp of its form's last level.
    /// </summary>
    public static long CapGain(long currentExp, long gain, int level, int form, Func<int, long> needExp,
        bool force = false)
    {
        if (gain <= 0)
        {
            return 0;
        }

        if (!force)
        {
            gain = Math.Min(gain, ExpLimit(level));
        }

        var maxLevel = MaxLevel(form);
        var limit = needExp(maxLevel) - 1;
        if (limit <= currentExp)
        {
            return 0;
        }

        if (needExp(maxLevel - 1) < currentExp)
        {
            gain = Math.Min(gain, limit - currentExp);
        }

        return gain;
    }

    /// <summary><c>StructSummon::IsEXPLimitReached</c>.</summary>
    public static bool IsExpLimitReached(long exp, Func<int, long> needExp) => needExp(MaxSummonLevel) - 1 <= exp;

    /// <summary>
    /// <c>StructSummon::onExpChange</c>: the level the exp reaches, from level 1 up to the form's maximum, and one JP
    /// for each level never reached before, two past the evolvable level.
    /// </summary>
    public static SummonLevelChange ResolveLevel(long exp, int form, int maxReachedLevel, int currentLevel,
        Func<int, long> needExp)
    {
        var maxLevel = MaxLevel(form);
        var evolvable = EvolvableLevel(form);
        var reached = Math.Max(maxReachedLevel, currentLevel);
        var level = 1;
        var jp = 0;
        while (level < maxLevel)
        {
            var need = needExp(level);
            if (need == 0 || need > exp)
            {
                break;
            }

            level++;
            if (reached < level)
            {
                jp++;
                if (level > evolvable)
                {
                    jp++;
                }
            }
        }

        // The JP only counts on a level gain (onExpChange: "if( nLevel > m_nLevel )").
        return level > currentLevel
            ? new SummonLevelChange(level, Math.Max(reached, level), jp)
            : new SummonLevelChange(level, reached, 0);
    }

    /// <summary><c>StructSummon::GetDeadEXPPenalty</c>, applied above level 5 to a death by a monster.</summary>
    public static long DeathPenalty(int level, Func<int, long> needExp) =>
        level <= 5 ? 0 : (long)(needExp(level) * (0.15 / (level - 1) + 0.0005));
}
