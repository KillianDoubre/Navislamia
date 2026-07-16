using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

public interface IItemStatCatalog
{
    IReadOnlyList<StatEffect> GetEffects(int itemResourceId);

    ItemType? GetWeaponType(int itemResourceId);
}
