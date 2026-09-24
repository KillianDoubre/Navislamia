using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;
using ItemGroup = Navislamia.Game.DataAccess.Entities.Enums.ItemGroup;
using ItemType = Navislamia.Game.DataAccess.Entities.Enums.ItemType;

namespace Navislamia.Game.Services;

/// <summary>
/// The profile two soul stones are told apart by, exactly as NGemity compares them
/// (<c>WorldSession.cpp:1537-1544</c>): <c>base_type[0..3]</c>, <c>base_var[k][0]</c>,
/// <c>opt_type[0..3]</c> and <c>opt_var[k][0]</c>. The reference reads the <em>first</em> value column
/// of each slot and nothing else; the repository carries a second one (<c>BaseVar2</c>/<c>OptVar2</c>)
/// and it is deliberately unused here, so that the comparison stays the one the reference makes.
///
/// A slot the resource does not fill reads as zero, which is what the reference's zero-initialised
/// arrays hold.
/// </summary>
public readonly record struct SoulstoneProfile(
    short BaseType0,
    short BaseType1,
    short BaseType2,
    short BaseType3,
    decimal BaseVar0,
    decimal BaseVar1,
    decimal BaseVar2,
    decimal BaseVar3,
    short OptType0,
    short OptType1,
    short OptType2,
    short OptType3,
    decimal OptVar0,
    decimal OptVar1,
    decimal OptVar2,
    decimal OptVar3)
{
    public static SoulstoneProfile From(in ItemSoulstoneCraftFields resource)
    {
        return new SoulstoneProfile(
            Code(resource.BaseTypes, 0), Code(resource.BaseTypes, 1),
            Code(resource.BaseTypes, 2), Code(resource.BaseTypes, 3),
            Value(resource.BaseVar1, 0), Value(resource.BaseVar1, 1),
            Value(resource.BaseVar1, 2), Value(resource.BaseVar1, 3),
            Code(resource.OptTypes, 0), Code(resource.OptTypes, 1),
            Code(resource.OptTypes, 2), Code(resource.OptTypes, 3),
            Value(resource.OptVar1, 0), Value(resource.OptVar1, 1),
            Value(resource.OptVar1, 2), Value(resource.OptVar1, 3));
    }

    private static short Code(short[] values, int slot)
    {
        return values is not null && slot < values.Length ? values[slot] : (short)0;
    }

    private static decimal Value(decimal[] values, int slot)
    {
        return values is not null && slot < values.Length ? values[slot] : 0m;
    }
}

/// <summary>
/// One item resource as <c>TM_CS_SOULSTONE_CRAFT</c> (260) reads it.
/// <paramref name="IsSoulstone"/> is the three-axis test of NGemity <c>WorldSession.cpp:1527-1528</c>:
/// base type 7, group 93 and class 401 (the repository maps <c>type</c>, <c>group</c> and <c>Class</c>).
/// </summary>
public readonly record struct SoulstoneCraftResource(
    bool IsSoulstone,
    int SocketCount,
    int Price,
    SoulstoneProfile Profile);

/// <summary>
/// The item resources 260 judges a request against. One lookup, so the service reads the crafted item's
/// chassis count, a stone's identity, its price and its profile without touching the database again.
/// </summary>
public interface ISoulstoneCraftCatalog
{
    /// <summary>False when the resource table carries no such item.</summary>
    bool TryGetResource(int itemResourceId, out SoulstoneCraftResource resource);
}

public class SoulstoneCraftCatalog : ISoulstoneCraftCatalog
{
    private const ItemBaseType SoulstoneBaseType = ItemBaseType.Soulstone; // 7, NGemity TYPE_SOULSTONE
    private const ItemGroup SoulstoneGroup = ItemGroup.Soulstone; // 93, NGemity GROUP_SOULSTONE
    private const ItemType SoulstoneClass = ItemType.Soulstone; // 401, NGemity CLASS_SOULSTONE

    private readonly ILogger _logger = Log.ForContext<SoulstoneCraftCatalog>();
    private readonly FrozenDictionary<int, SoulstoneCraftResource> _resources;

    public SoulstoneCraftCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetSoulstoneCraftFields();
        var resources = new Dictionary<int, SoulstoneCraftResource>(fields.Count);
        var stones = 0;

        foreach (var field in fields)
        {
            var isSoulstone = field.ItemBaseType == SoulstoneBaseType && field.Group == SoulstoneGroup &&
                              field.ItemType == SoulstoneClass;
            if (isSoulstone)
            {
                stones++;
            }

            resources[field.Id] = new SoulstoneCraftResource(isSoulstone, field.SocketCount, field.Price,
                SoulstoneProfile.From(field));
        }

        _resources = resources.ToFrozenDictionary();
        _logger.Debug("Loaded socket counts for {count} item resources, {stones} of them soul stones",
            _resources.Count, stones);
    }

    public bool TryGetResource(int itemResourceId, out SoulstoneCraftResource resource)
    {
        return _resources.TryGetValue(itemResourceId, out resource);
    }
}
