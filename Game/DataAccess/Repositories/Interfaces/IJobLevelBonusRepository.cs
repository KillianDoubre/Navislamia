using System.Collections.Generic;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct JobLevelBonusFields(
    int Job,
    decimal[] Strength,
    decimal[] Vitality,
    decimal[] Dexterity,
    decimal[] Agility,
    decimal[] Intelligence,
    decimal[] Wisdom,
    decimal[] Luck,
    decimal DefaultStrength,
    decimal DefaultVitality,
    decimal DefaultDexterity,
    decimal DefaultAgility,
    decimal DefaultIntelligence,
    decimal DefaultWisdom,
    decimal DefaultLuck);

public interface IJobLevelBonusRepository
{
    IReadOnlyList<JobLevelBonusFields> GetAll();
}
