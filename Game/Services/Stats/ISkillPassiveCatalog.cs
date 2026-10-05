using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

public interface ISkillPassiveCatalog
{
    IReadOnlyList<StatEffect> ResolveSummonSp(int skillId, int skillLevel) => System.Array.Empty<StatEffect>();
    IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon);

    /// <summary>
    /// <c>var0</c> of a passive read by a rule rather than as a stat: Technical Creature Control (1881), whose
    /// seconds lengthen the double summon. 0 for any other skill.
    /// </summary>
    decimal FirstVar(int skillId) => 0m;
}
