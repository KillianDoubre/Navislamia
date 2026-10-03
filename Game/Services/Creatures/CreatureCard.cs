using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// A creature card of the bag as the session knows it: the item, its flag, and the summon it holds — the
/// official <c>StructItem::GetSummonStruct</c>. <see cref="SummonHandle"/> is allocated once per session, when the
/// summon is first announced (301), and kept for every entry into the world, like the official summon object.
/// </summary>
public sealed class CreatureCard
{
    public long ItemId { get; init; }
    public int Code { get; init; }
    public long Amount { get; set; }
    public ItemFlag Flag { get; set; }
    public int Enhance { get; init; }
    public long SummonId { get; set; }
    public int SummonCode { get; set; }
    public string SummonName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public uint SummonHandle { get; set; }

    /// <summary>The summon's JP, gained on its level-ups (<c>m_nJobPoint</c>).</summary>
    public int Jp { get; set; }

    /// <summary><c>m_nMaxReachedLevel</c>, kept in <c>Summons.MaxLevel</c>: a level regained after a death penalty gives no JP.</summary>
    public int MaxReachedLevel { get; set; } = 1;

    public long LastDecreasedExp { get; set; }

    /// <summary>The forms left behind by evolution (<c>m_nPrevJobId</c>/<c>m_nPrevJobLevel</c>), two at most.</summary>
    public long[] PreviousSummonIds { get; set; } = new long[2];

    public int[] PreviousLevels { get; set; } = new int[2];

    /// <summary>A summon at 0 HP: dead, sent back by its master or by the 60-second hold, and kept dead until revived.</summary>
    public bool IsDead => Hp <= 0 && HpKnown;

    /// <summary>Whether <see cref="Hp"/> is a real value: a fresh summon row (0 HP) starts full, not dead.</summary>
    public bool HpKnown { get; set; }

    /// <summary>The skills the summon learned, by id (<c>SummonSkills</c>), guarded by the session's summon lock.</summary>
    public System.Collections.Generic.Dictionary<int, byte> Skills { get; } = new();

    public SummonProgress Progress() => new(SummonId, SummonCode, Level, Exp, Jp, MaxReachedLevel, Hp, Mp,
        LastDecreasedExp, PreviousSummonIds, PreviousLevels, SummonName);

    /// <summary>Whether 301 has been sent this session (login for a slotted card, formation otherwise).</summary>
    public bool InfoSent { get; set; }

    public uint Handle => (uint)ItemId;
    public bool IsBound => CreatureRules.IsBound(Flag);
    public bool HasSummon => SummonId != 0 && SummonCode != 0;
}
