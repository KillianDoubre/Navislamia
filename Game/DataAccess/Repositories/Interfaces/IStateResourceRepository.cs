using System.Collections.Generic;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct StateEffectFields(int StateId, int EffectType, decimal[] Values);

public interface IStateResourceRepository
{
    IReadOnlyList<StateEffectFields> GetStatStates();
}
