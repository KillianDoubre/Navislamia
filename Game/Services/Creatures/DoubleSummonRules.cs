using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The double summon of the official server (docs/packet-specs/socle-duree-invocations.md): a second creature called
/// while one is out stays <c>m_nDoubleSummonTime</c> ticks, announced by <c>TS_SC_UNSUMMON_NOTICE</c> (306), then goes
/// back. Ported from <c>StructPlayer::Summon</c>, <c>SetMainAndSubSummon</c>, <c>OnUpdate</c> and the stat passes that
/// build <c>m_nDoubleSummonTime</c>, identical in the 2012-11 <c>CaptainHerlockServer.exe</c> and the 2015 source.
/// </summary>
public static class DoubleSummonRules
{
    /// <summary><c>GameRule::DEFAULT_DOUBLE_SUMMON_TIME</c>: 1000 ar_time ticks, 10 s (<c>0x3e8</c> in 2012).</summary>
    public const uint DefaultDoubleSummonTime = 1000;

    /// <summary><c>SKILL_TECHNICAL_CREATURE_CONTROL</c>: each level adds <c>var0</c> seconds.</summary>
    public const int TechnicalCreatureControlSkill = 1881;

    /// <summary><c>StructState::EF_CREATURE_PARAMETER_AMP</c>: each one adds <c>level × value_4</c> seconds.</summary>
    public const int CreatureParameterAmpEffect = 112;

    /// <summary><c>StructState::EF_INFINITE_SUMMON_TIME</c>: while it holds, the second summon does not go back.</summary>
    public const int InfiniteSummonTimeEffect = 3121;

    /// <summary>
    /// <c>m_nDoubleSummonTime</c>: the default, plus <c>level × var0 × 100</c> for Technical Creature Control
    /// (<c>applyPassiveSkillEffect</c>), plus <c>level × value_4 × 100</c> for each creature parameter state
    /// (<c>applyStatByState</c>).
    /// </summary>
    public static uint DoubleSummonTime(int technicalControlLevel, decimal technicalControlVar0,
        IEnumerable<(int Level, decimal Value4)> creatureAmpStates)
    {
        var ticks = (decimal)DefaultDoubleSummonTime + Math.Max(0, technicalControlLevel) * technicalControlVar0 * 100m;
        if (creatureAmpStates is not null)
        {
            foreach (var (level, value4) in creatureAmpStates)
            {
                if (level > 0)
                {
                    ticks += level * value4 * 100m;
                }
            }
        }

        return ticks <= 0 ? 0 : (uint)Math.Min(ticks, int.MaxValue);
    }

    /// <summary>The <c>unsummon_duration</c> a new second summon is announced with: 0 under an infinite state.</summary>
    public static uint NoticeDuration(bool infinite, uint doubleSummonTime) => infinite ? 0 : doubleSummonTime;

    /// <summary>
    /// <c>SetMainAndSubSummon</c>: of the two summons out, the one met first among the formation's first
    /// <c>min(Creature Control level, 6) − 1</c> slots is the main one, the other the second one. When neither is met
    /// the official code asserts and, released, would clear the main summon; the pair is then kept as it is.
    /// </summary>
    public static (long Main, long Sub) Order(IReadOnlyList<long> slots, long main, long sub, int creatureControlLevel)
    {
        if (main == 0 || sub == 0 || slots is null)
        {
            return (main, sub);
        }

        var searched = Math.Min(Math.Min(creatureControlLevel, 6) - 1, slots.Count);
        for (var i = 0; i < searched; i++)
        {
            if (slots[i] == main)
            {
                return (main, sub);
            }

            if (slots[i] == sub)
            {
                return (sub, main);
            }
        }

        return (main, sub);
    }

    /// <summary><c>OnUpdate</c>: <c>m_nNextUnSummonTime &lt; t</c>, the second summon's time is up.</summary>
    public static bool IsDue(uint now, uint deadline) => unchecked((int)(deadline - now)) < 0;

    /// <summary>
    /// <c>DB_UpdateCharacter</c>'s <c>IN_REMAIN_SUMMON_TIME</c>: what is left of the second summon's time, negative once
    /// it is up (an infinite state holds it past its deadline).
    /// </summary>
    public static int Remaining(uint now, uint deadline) => unchecked((int)(deadline - now));

    /// <summary><c>SendCharacterInfo</c>: the login notice carries what is left, 0 once nothing is.</summary>
    public static uint LoginNotice(int remaining) => remaining > 0 ? (uint)remaining : 0;
}
