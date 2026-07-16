using System.Collections.Generic;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct SkillPassiveFields(
    int SkillId,
    int EffectType,
    decimal[] Vars,
    SkillWeaponFlag Weapons,
    bool WeaponNotRequired);

public interface ISkillResourceRepository
{
    IReadOnlyList<SkillPassiveFields> GetStatPassives();
}
