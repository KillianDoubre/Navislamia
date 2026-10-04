using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.DataAccess.Repositories;

public class StateResourceRepository : IStateResourceRepository
{
    private readonly ArcadiaContext _context;

    public StateResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<Navislamia.Game.Services.Buffs.PeriodicStateRule> GetPeriodicStates() =>
        _context.StateResources.AsNoTracking().Where(s => s.BaseEffect != StateBaseEffect.None)
            .Select(s => new Navislamia.Game.Services.Buffs.PeriodicStateRule((int)s.Id, (int)s.BaseEffect,
                s.FireInterval, (int)s.ElementalType, s.AmplifyBase, s.AmplifyPerSkill,
                s.AddDamageBase, s.AddDamagePerSkl, s.Values)).ToList();

    public IReadOnlyList<StateEffectFields> GetStatStates()
    {
        var supported = StateCatalog.SupportedEffectTypes;

        return _context.StateResources
            .AsNoTracking()
            .Where(state => supported.Contains((int)state.EffectType))
            .Select(state => new StateEffectFields((int)state.Id, (int)state.EffectType, state.Values))
            .ToList();
    }

    public IReadOnlyList<StateRuleFields> GetStateRules()
    {
        return _context.StateResources
            .AsNoTracking()
            .Select(state => new StateRuleFields((int)state.Id, state.DuplicateGroup, state.ReiterationCount,
                state.StateTimeType, (int)state.EffectType, state.Values))
            .ToList();
    }

    public IReadOnlyList<int> GetEraseOnRequestStateIds()
    {
        const StateTimeType flag = StateTimeType.EraseOnRequest;

        // The flag test runs in memory: the table is small, and a bitwise predicate on a mapped enum is
        // not worth a translation risk. The projection still keeps the payload to two columns.
        return _context.StateResources
            .AsNoTracking()
            .Select(state => new StateFlagFields((int)state.Id, state.StateTimeType))
            .AsEnumerable()
            .Where(row => (row.StateTimeType & flag) == flag)
            .Select(row => row.StateId)
            .ToList();
    }

    public IReadOnlyList<StateEffectFields> GetStatesWithEffect(int effectType)
    {
        return _context.StateResources
            .AsNoTracking()
            .Where(state => (int)state.EffectType == effectType)
            .Select(state => new StateEffectFields((int)state.Id, (int)state.EffectType, state.Values))
            .ToList();
    }

    public IReadOnlyList<int> GetStateIds()
    {
        return _context.StateResources
            .AsNoTracking()
            .Select(state => (int)state.Id)
            .ToList();
    }
}
