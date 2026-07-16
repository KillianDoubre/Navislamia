using System;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

public static class CharacterDefaults
{
    public static string DefaultClientInfo { get; } = BuildDefaultClientInfo();

    public static Job GetStarterJob(int race)
    {
        return (Race)race switch
        {
            Race.Gaia => Job.Rogue,
            Race.Deva => Job.Guide,
            Race.Asura => Job.Stepper,
            _ => throw new ArgumentOutOfRangeException(nameof(race), race, null)
        };
    }

    public static bool Apply(CharacterEntity character)
    {
        var changed = false;

        if (character.Lv <= 0)
        {
            character.Lv = 1;
            changed = true;
        }

        if (character.MaxReachedLv < character.Lv)
        {
            character.MaxReachedLv = character.Lv;
            changed = true;
        }

        if ((int)character.CurrentJob == 0)
        {
            character.CurrentJob = GetStarterJob(character.Race);
            changed = true;
        }

        if (character.Jlv <= 0)
        {
            character.Jlv = 1;
            changed = true;
        }

        if (character.JobDepth == 0)
        {
            character.JobDepth = JobDepth.Base;
            changed = true;
        }

        if (character.PreviousJobs is not { Length: 3 })
        {
            character.PreviousJobs = new Job[3];
            changed = true;
        }

        if (character.JobLvs is not { Length: 3 })
        {
            character.JobLvs = new int[3];
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(character.ClientInfo))
        {
            character.ClientInfo = DefaultClientInfo;
            changed = true;
        }

        changed |= ApplyItemIndices(character);

        return changed;
    }

    private static bool ApplyItemIndices(CharacterEntity character)
    {
        return character.Items is not null
               && InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
    }

    private static string BuildDefaultClientInfo()
    {
        var entries = new System.Collections.Generic.List<string>
        {
            "QS2=0,2,0", "QS2=1,2,2", "QS2=11,2,1", "QS2=24,2,7", "QS2=25,2,8", "QS2=35,2,28"
        };

        AddKeyBindings(entries, 0, 0, 0, new[] { 192, 73, 83, 67, 89, 69, 82, 70, 71, 80, 81, 77, 84, 72, 90, 79, 88, 86, 78 });
        AddKeyBindings(entries, 19, 0, 1, new[] { 115, 70, 72, 219, 221, 80 });
        AddKeyBindings(entries, 25, 0, 0, new[] { 9, 32, 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187 });
        AddKeyBindings(entries, 39, 1, 0, new[] { 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187 });
        AddKeyBindings(entries, 51, 0, 1, new[] { 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187 });
        AddKeyBindings(entries, 63, 0, 0, new[] { 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 220 });
        AddKeyBindings(entries, 75, 0, 0, new int[48]);
        AddKeyBindings(entries, 123, 0, 0, new[] { 66, 68, 85, 74, 75, 76 });

        entries.Add("ENTERCHATMODE=1");
        entries.Add("ENTERCHATMODE2=1");
        entries.Add("PREVINSTANCEGAME=0");
        entries.Add("CLIENTVER=1");
        return string.Join('|', entries);
    }

    private static void AddKeyBindings(System.Collections.Generic.ICollection<string> entries, int start,
        int modifier1, int modifier2, System.Collections.Generic.IReadOnlyList<int> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            entries.Add($"KMT={start + i},0,{modifier1},{modifier2},{keys[i]}");
        }
    }
}
