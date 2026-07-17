using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.DataAccess.Repositories;

public class SkillResourceRepository : ISkillResourceRepository
{
    private readonly ArcadiaContext _context;

    public SkillResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<SkillPassiveFields> GetStatPassives()
    {
        var supported = SkillPassiveCatalog.SupportedEffectTypes;

        return _context.SkillResources
            .AsNoTracking()
            .Where(skill => !skill.IsToggle
                            && skill.IsValid != SkillState.Invalid
                            && supported.Contains((int)skill.EffectType))
            .Select(skill => new PassiveRow
            {
                Id = (int)skill.Id,
                EffectType = (int)skill.EffectType,
                Vars = skill.Values,
                OneHandSword = skill.UseWithOneHandSword,
                TwoHandSword = skill.UseWithTwoHandSword,
                DoubleSword = skill.UseWithDoubleSword,
                Dagger = skill.UseWithDagger,
                DoubleDagger = skill.UseWithDoubleDagger,
                Spear = skill.UseWithSpear,
                Axe = skill.UseWithAxe,
                OneHandAxe = skill.UseWithOneHandAxe,
                DoubleAxe = skill.UseWithDoubleAxe,
                OneHandMace = skill.UseWithOneHandMace,
                TwoHandMace = skill.UseWithTwoHandMace,
                Lightbow = skill.UseWithLightbow,
                Heavybow = skill.UseWithHeavybow,
                Crossbow = skill.UseWithCrossbow,
                OneHandStaff = skill.UseWithOneHandStaff,
                TwoHandStaff = skill.UseWithTwoHandStaff,
                WeaponNotRequired = skill.UseWithWeaponNotRequired
            })
            .AsEnumerable()
            .Select(row => new SkillPassiveFields(row.Id, row.EffectType, row.Vars, WeaponFlagsOf(row),
                row.WeaponNotRequired))
            .ToList();
    }

    public IReadOnlyList<CastableSkillRow> GetCastableSkills()
    {
        var effectTypes = BuffCatalog.CastableEffectTypes;

        return _context.SkillResources
            .AsNoTracking()
            .Where(skill => effectTypes.Contains((int)skill.EffectType)
                            && skill.IsValid != SkillState.Invalid)
            .Select(skill => new CastableSkillRow(
                (int)skill.Id,
                (int)skill.EffectType,
                skill.IsHarmful,
                (int)skill.Target,
                (int?)skill.StateId,
                skill.ToggleGroup,
                skill.Values,
                skill.StateSecond,
                skill.StateSecondPerLevel,
                skill.StateLevelBase,
                skill.StateLevelPerSkill,
                skill.CostMp,
                skill.CostMpPerSkl,
                skill.DelayCast,
                skill.DelayCastPerSkl,
                skill.DelayCommon,
                skill.DelayCooltime,
                skill.DelayCooltimePerSkl,
                skill.RequiredLevel))
            .ToList();
    }

    private static SkillWeaponFlag WeaponFlagsOf(PassiveRow row)
    {
        var flags = SkillWeaponFlag.None;
        if (row.OneHandSword) { flags |= SkillWeaponFlag.OneHandSword; }
        if (row.TwoHandSword) { flags |= SkillWeaponFlag.TwoHandSword; }
        if (row.DoubleSword) { flags |= SkillWeaponFlag.DoubleSword; }
        if (row.Dagger) { flags |= SkillWeaponFlag.Dagger; }
        if (row.DoubleDagger) { flags |= SkillWeaponFlag.DoubleDagger; }
        if (row.Spear) { flags |= SkillWeaponFlag.Spear; }
        if (row.Axe) { flags |= SkillWeaponFlag.Axe; }
        if (row.OneHandAxe) { flags |= SkillWeaponFlag.OneHandAxe; }
        if (row.DoubleAxe) { flags |= SkillWeaponFlag.DoubleAxe; }
        if (row.OneHandMace) { flags |= SkillWeaponFlag.OneHandMace; }
        if (row.TwoHandMace) { flags |= SkillWeaponFlag.TwoHandMace; }
        if (row.Lightbow) { flags |= SkillWeaponFlag.Lightbow; }
        if (row.Heavybow) { flags |= SkillWeaponFlag.Heavybow; }
        if (row.Crossbow) { flags |= SkillWeaponFlag.Crossbow; }
        if (row.OneHandStaff) { flags |= SkillWeaponFlag.OneHandStaff; }
        if (row.TwoHandStaff) { flags |= SkillWeaponFlag.TwoHandStaff; }

        return flags;
    }

    private sealed class PassiveRow
    {
        public int Id { get; init; }
        public int EffectType { get; init; }
        public decimal[] Vars { get; init; }
        public bool OneHandSword { get; init; }
        public bool TwoHandSword { get; init; }
        public bool DoubleSword { get; init; }
        public bool Dagger { get; init; }
        public bool DoubleDagger { get; init; }
        public bool Spear { get; init; }
        public bool Axe { get; init; }
        public bool OneHandAxe { get; init; }
        public bool DoubleAxe { get; init; }
        public bool OneHandMace { get; init; }
        public bool TwoHandMace { get; init; }
        public bool Lightbow { get; init; }
        public bool Heavybow { get; init; }
        public bool Crossbow { get; init; }
        public bool OneHandStaff { get; init; }
        public bool TwoHandStaff { get; init; }
        public bool WeaponNotRequired { get; init; }
    }
}
