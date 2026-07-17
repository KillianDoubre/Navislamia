using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
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

    public IReadOnlyList<StateEffectFields> GetStatStates()
    {
        var supported = StateCatalog.SupportedEffectTypes;

        return _context.StateResources
            .AsNoTracking()
            .Where(state => supported.Contains((int)state.EffectType))
            .Select(state => new StateEffectFields((int)state.Id, (int)state.EffectType, state.Values))
            .ToList();
    }
}
