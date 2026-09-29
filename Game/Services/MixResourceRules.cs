using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

/// <summary>
/// One <c>MaterialInfo</c> of a recipe: five <c>(type, value)</c> pairs, read in the order of the row
/// (<c>type[0], value[0], … type[4], value[4]</c>).
/// </summary>
public readonly record struct MixMaterialInfo(IReadOnlyList<int> Types, IReadOnlyList<int> Values);

/// <summary>
/// The positional reading of a <c>MixResource</c> row: the 109 columns of the reference schema carry
/// <c>id</c>, <c>mix_type</c>, <c>mix_value_01..06</c>, <c>sub_material_count</c>, then a main
/// <c>MaterialInfo</c> and nine sub <c>MaterialInfo</c>s, each as the five pairs
/// <c>type_01, value_01 … type_05, value_05</c> (NGemity loads the row in exactly that order,
/// <c>ObjectMgr.cpp:1108-1146</c>). Reading by name reproduces this order — and only this order.
///
/// The one invariant the reference data carries: <c>sub_material_count</c> is exactly the number of
/// <c>subNN</c> groups whose first <c>type</c> is non-zero (measured on the 754 NGemity rows, 0
/// exception; the remaining groups are zero in types <em>and</em> values). NGemity silently ignores a
/// row whose count does not match the request, so an inconsistent row cannot be spotted at runtime —
/// this class is where the import can.
/// See docs/packet-specs/socle-artisanat-ressources.md §5.1 and §8 (L1a).
/// </summary>
public static class MixResourceRules
{
    /// <summary><c>mix_value_01..06</c> — <c>MixManager.h:23</c>.</summary>
    public const int MixValueCount = 6;

    /// <summary>The five <c>(type, value)</c> pairs of one <c>MaterialInfo</c> — <c>MixManager.h:22</c>.</summary>
    public const int MaterialInfoCount = 5;

    /// <summary>Nine sub-material groups at most — <c>MixManager.h:24</c>, <c>MAX_SUB_MATERIAL_COUNT</c>.</summary>
    public const int MaxSubMaterialGroups = 9;

    /// <summary>The six <c>mix_value_01..06</c> of the row, in order.</summary>
    public static IReadOnlyList<int> MixValues(MixResourceEntity mix)
    {
        return new[]
        {
            mix.MixValue01, mix.MixValue02, mix.MixValue03, mix.MixValue04, mix.MixValue05, mix.MixValue06
        };
    }

    /// <summary>The target material the recipe describes (<c>main_type_01..05</c> / <c>main_value_01..05</c>).</summary>
    public static MixMaterialInfo MainMaterial(MixResourceEntity mix)
    {
        return new MixMaterialInfo(
            new[] { mix.MainType01, mix.MainType02, mix.MainType03, mix.MainType04, mix.MainType05 },
            new[] { mix.MainValue01, mix.MainValue02, mix.MainValue03, mix.MainValue04, mix.MainValue05 });
    }

    /// <summary>
    /// The <paramref name="groupIndex"/>-th sub-material group, 0-based as <c>sub_material[i]</c> in the
    /// reference: group 0 is <c>sub01_*</c>, group 8 is <c>sub09_*</c>.
    /// </summary>
    public static MixMaterialInfo SubMaterial(MixResourceEntity mix, int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= MaxSubMaterialGroups)
        {
            throw new ArgumentOutOfRangeException(nameof(groupIndex), groupIndex,
                $"Sub-material groups are 0..{MaxSubMaterialGroups - 1}");
        }

        return groupIndex switch
        {
            0 => new MixMaterialInfo(
                new[] { mix.Sub01Type01, mix.Sub01Type02, mix.Sub01Type03, mix.Sub01Type04, mix.Sub01Type05 },
                new[] { mix.Sub01Value01, mix.Sub01Value02, mix.Sub01Value03, mix.Sub01Value04, mix.Sub01Value05 }),
            1 => new MixMaterialInfo(
                new[] { mix.Sub02Type01, mix.Sub02Type02, mix.Sub02Type03, mix.Sub02Type04, mix.Sub02Type05 },
                new[] { mix.Sub02Value01, mix.Sub02Value02, mix.Sub02Value03, mix.Sub02Value04, mix.Sub02Value05 }),
            2 => new MixMaterialInfo(
                new[] { mix.Sub03Type01, mix.Sub03Type02, mix.Sub03Type03, mix.Sub03Type04, mix.Sub03Type05 },
                new[] { mix.Sub03Value01, mix.Sub03Value02, mix.Sub03Value03, mix.Sub03Value04, mix.Sub03Value05 }),
            3 => new MixMaterialInfo(
                new[] { mix.Sub04Type01, mix.Sub04Type02, mix.Sub04Type03, mix.Sub04Type04, mix.Sub04Type05 },
                new[] { mix.Sub04Value01, mix.Sub04Value02, mix.Sub04Value03, mix.Sub04Value04, mix.Sub04Value05 }),
            4 => new MixMaterialInfo(
                new[] { mix.Sub05Type01, mix.Sub05Type02, mix.Sub05Type03, mix.Sub05Type04, mix.Sub05Type05 },
                new[] { mix.Sub05Value01, mix.Sub05Value02, mix.Sub05Value03, mix.Sub05Value04, mix.Sub05Value05 }),
            5 => new MixMaterialInfo(
                new[] { mix.Sub06Type01, mix.Sub06Type02, mix.Sub06Type03, mix.Sub06Type04, mix.Sub06Type05 },
                new[] { mix.Sub06Value01, mix.Sub06Value02, mix.Sub06Value03, mix.Sub06Value04, mix.Sub06Value05 }),
            6 => new MixMaterialInfo(
                new[] { mix.Sub07Type01, mix.Sub07Type02, mix.Sub07Type03, mix.Sub07Type04, mix.Sub07Type05 },
                new[] { mix.Sub07Value01, mix.Sub07Value02, mix.Sub07Value03, mix.Sub07Value04, mix.Sub07Value05 }),
            7 => new MixMaterialInfo(
                new[] { mix.Sub08Type01, mix.Sub08Type02, mix.Sub08Type03, mix.Sub08Type04, mix.Sub08Type05 },
                new[] { mix.Sub08Value01, mix.Sub08Value02, mix.Sub08Value03, mix.Sub08Value04, mix.Sub08Value05 }),
            _ => new MixMaterialInfo(
                new[] { mix.Sub09Type01, mix.Sub09Type02, mix.Sub09Type03, mix.Sub09Type04, mix.Sub09Type05 },
                new[] { mix.Sub09Value01, mix.Sub09Value02, mix.Sub09Value03, mix.Sub09Value04, mix.Sub09Value05 })
        };
    }

    /// <summary>
    /// The number of groups the row actually fills: the groups whose first <c>type</c> is non-zero. The
    /// count is what NGemity compares to the number of materials in the request
    /// (<c>MixManager.cpp:246</c> <c>sub_material_cnt != nSubMaterialCount</c>).
    /// </summary>
    public static int DeclaredSubMaterialGroups(MixResourceEntity mix)
    {
        return Enumerable.Range(0, MaxSubMaterialGroups)
            .Count(groupIndex => SubMaterial(mix, groupIndex).Types[0] != 0);
    }

    /// <summary>
    /// <c>false</c> when <c>sub_material_count</c> disagrees with the groups the row fills. The runtime
    /// resolution does not consult it: it compares the count to the request, like NGemity. The import is
    /// the only caller, and reports the row rather than keep it quiet.
    /// </summary>
    public static bool IsSubMaterialCountConsistent(MixResourceEntity mix)
    {
        return mix.SubMaterialCount == DeclaredSubMaterialGroups(mix);
    }
}
