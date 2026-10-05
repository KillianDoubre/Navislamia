using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

public interface ISkillPassiveCatalog
{
    IReadOnlyList<StatEffect> ResolveSummonSp(int skillId, int skillLevel) => System.Array.Empty<StatEffect>();
    IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon);
}
