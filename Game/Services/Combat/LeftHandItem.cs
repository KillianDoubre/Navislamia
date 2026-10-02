using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Combat;

/// <summary>
/// The item worn in the shield slot that is not a shield: <see cref="WeaponType"/> set for a second weapon, unset for
/// arrows (group <c>Bullet</c>, worn in the same slot). <see cref="Amount"/> is what a bow has left to shoot.
/// </summary>
public sealed record LeftHandItem(uint Handle, int ResourceId, ItemType? WeaponType, long Amount,
    IReadOnlyList<StatEffect> Effects)
{
    public long Amount { get; set; } = Amount;
}
