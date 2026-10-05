using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

public interface ISkillPassiveCatalog
{
    IReadOnlyList<StatEffect> ResolveSummonSp(int skillId, int skillLevel) => System.Array.Empty<StatEffect>();

    /// <summary>
    /// A master's <c>EF_INCREASE_SUMMON_HP_MP_SP</c> (10031) or <c>EF_AMPLIFY_SUMMON_HP_MP_SP</c> (10032) as its summons
    /// get it (<c>m_ParameterForSummon</c>): max HP, MP and SP, HP and MP regeneration.
    /// </summary>
    IReadOnlyList<StatEffect> ResolveForSummon(int skillId, int skillLevel) => ResolveSummonSp(skillId, skillLevel);

    /// <summary>The <c>var</c> row of an <c>EF_HUNTING_TRAINING</c> skill (10013), null for any other skill.</summary>
    decimal[] HuntingTraining(int skillId) => null;
    IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon);

    /// <summary>The same, with the shield slot known: a shield-only passive needs a shield worn.</summary>
    IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon, bool wearsShield) =>
        Resolve(skillId, skillLevel, equippedWeapon);

    /// <summary>
    /// <c>var0</c> of a passive read by a rule rather than as a stat: Technical Creature Control (1881), whose
    /// seconds lengthen the double summon. 0 for any other skill.
    /// </summary>
    decimal FirstVar(int skillId) => 0m;
}
