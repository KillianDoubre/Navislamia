using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

public interface IItemStatCatalog
{
    IReadOnlyList<StatEffect> GetEffects(int itemResourceId);

    ItemType? GetWeaponType(int itemResourceId);

    /// <summary>The <c>AttackRange</c> a weapon gives: its <c>range</c> × 100; 0 when not a weapon or unknown.</summary>
    float GetAttackRange(int itemResourceId) => 0f;
}
