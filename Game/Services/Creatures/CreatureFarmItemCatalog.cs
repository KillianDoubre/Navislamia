using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// What a deposition's stacks must be, read from the item resource table: the class that tells a ticket (403
/// <c>CLASS_FARM_PASS</c>) from a cracker (402 <c>CLASS_CREATURE_FOOD</c>), and — for a ticket — the duration
/// of the farm row it buys and whether it is premium. The values come from the item, never from the frame
/// (docs/packet-specs/6002-foster-creature.md §5.2 points 3-5, §5.4).
/// </summary>
public interface ICreatureFarmItemCatalog
{
    /// <summary>The class of the resource, false when the table has no such row.</summary>
    bool TryGetClass(int itemResourceId, out ItemType itemType);

    /// <summary>
    /// The duration in seconds a ticket of this resource buys (<c>OptVar1[0]</c>, the <c>fOptVar1[0]</c> of the
    /// reference) and whether it is premium (<c>OptVar2[0] == 1</c>). False when the resource is unknown or
    /// carries no such option slot.
    /// </summary>
    bool TryGetTicket(int itemResourceId, out int durationSeconds, out bool isCash);

    /// <summary>
    /// <c>StructItem::IsExpireItem</c> (<c>StructItem.cpp:778-784</c>): the resource's <c>decrease_type</c> is
    /// <c>DECREASE_ON_GAME</c> (1) or <c>DECREASE_ALWAYS</c> (2), an item with a limited duration.
    /// </summary>
    bool IsExpireItem(int itemResourceId) => false;
}

/// <summary>
/// The two columns the deposition reads, frozen at startup over the whole item resource table. A resource the
/// table does not hold is unknown rather than default: an unknown handle must not be able to pass for a ticket.
/// </summary>
public sealed class CreatureFarmItemCatalog : ICreatureFarmItemCatalog
{
    private readonly Dictionary<int, ItemFarmFields> _fields = new();

    public CreatureFarmItemCatalog(IItemResourceRepository repository)
    {
        var fields = repository?.GetFarmFields();
        if (fields is null)
        {
            return;
        }

        foreach (var field in fields)
        {
            _fields[field.Id] = field;
        }
    }

    public bool TryGetClass(int itemResourceId, out ItemType itemType)
    {
        if (_fields.TryGetValue(itemResourceId, out var field))
        {
            itemType = field.ItemType;
            return true;
        }

        itemType = default;
        return false;
    }

    public bool IsExpireItem(int itemResourceId) =>
        _fields.TryGetValue(itemResourceId, out var field)
        && field.DecreaseType is ItemDecreaseTimeType.DecreaseInGame or ItemDecreaseTimeType.DecreaseAlways;

    public bool TryGetTicket(int itemResourceId, out int durationSeconds, out bool isCash)
    {
        durationSeconds = 0;
        isCash = false;

        if (!_fields.TryGetValue(itemResourceId, out var field)
            || field.OptVar1 is not { Length: > 0 } || field.OptVar2 is not { Length: > 0 })
        {
            return false;
        }

        // Read verbatim, as seconds: the depot's opt_var1_0 holds the logical value the reference's duration
        // takes (5 days -> 432000), the ×10000 fixed point being the C++ ItemBase's own storage. The unit the
        // client's item dump uses is still open (docs/packet-specs/6002-foster-creature.md §7.1) and no
        // division could repair a wrong one.
        durationSeconds = (int)field.OptVar1[0];
        isCash = field.OptVar2[0] == 1m;
        return true;
    }
}
