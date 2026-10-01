using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>The quest types and value slots used by the server's QuestBase and StructQuest.</summary>
public static class QuestRules
{
    public const byte InProgress = 1;
    public const byte Finishable = 2;
    public const int MaxActive = 20;

    public static bool Supported(int type) => type is 101 or 102 or 103 or 106 or 107 or 109 or 201 or 301 or 302 or 401 or 501 or 601 or 901;
    public static bool Supported(QuestResourceEntity quest) => Supported(quest.Type)
        && (quest.Type != 601 || Enumerable.Range(0, 3).All(i => Values(quest)[i * 3] is 0 or 99));

    public static int[] Values(QuestResourceEntity quest) => new[]
    {
        quest.Value1, quest.Value2, quest.Value3, quest.Value4, quest.Value5, quest.Value6,
        quest.Value7, quest.Value8, quest.Value9, quest.Value10, quest.Value11, quest.Value12
    };

    public static bool CanStart(QuestResourceEntity quest, ConnectionInfo player,
        IReadOnlyCollection<CharacterQuestEntity> active, IReadOnlyDictionary<int, DateTime> completed,
        DateTime now, int jobClass = 1, int jobDepth = 0)
    {
        if (!Supported(quest) || active.Count >= MaxActive || active.Any(q => q.Code == quest.Id)) return false;
        var seconds = (int)now.ToLocalTime().TimeOfDay.TotalSeconds;
        if (quest.LimitBeginTime > 0 && seconds < quest.LimitBeginTime
            || quest.LimitEndTime > 0 && seconds > quest.LimitEndTime) return false;
        if (quest.LimitFavor > 0) return false;
        if (completed.TryGetValue(quest.Id, out var last))
        {
            if (quest.Repeatable != "1" || now < last.AddSeconds(Math.Max(0, quest.CoolTime))) return false;
        }

        var fore = new[] { quest.ForeQuest1, quest.ForeQuest2, quest.ForeQuest3 }.Where(id => id > 0).ToArray();
        if (fore.Length > 0 && (quest.OrFlag == "1"
                ? !fore.Any(completed.ContainsKey) : fore.Any(id => !completed.ContainsKey(id)))) return false;
        if (player.CharacterLevel < quest.LimitLevel || quest.LimitMaxLevel > 0 && player.CharacterLevel > quest.LimitMaxLevel
            || player.CharacterJobLevel < quest.LimitJobLevel
            || quest.LimitMaxJobLevel > 0 && player.CharacterJobLevel > quest.LimitMaxJobLevel) return false;
        if (quest.LimitJob > 0 && player.CharacterJob != quest.LimitJob) return false;
        if (quest.LimitJobDepth > 0 && (quest.LimitJobDepth & (1 << jobDepth)) == 0) return false;
        var raceAllowed = player.CharacterRace switch
        {
            3 => quest.LimitGaia == "1",
            4 => quest.LimitDeva == "1",
            5 => quest.LimitAsura == "1",
            _ => false
        };
        if (!raceAllowed) return false;
        if (quest.LimitJob > 0) return true;
        return jobClass switch
        {
            1 => quest.LimitFighter == "1",
            2 => quest.LimitHunter == "1",
            3 => quest.LimitMagician == "1",
            4 => quest.LimitSummoner == "1",
            _ => false
        };
    }

    public static bool ApplyKill(QuestResourceEntity resource, CharacterQuestEntity quest, int monsterId,
        Func<int, int, bool> matches = null)
    {
        if (resource.Type is not (101 or 102 or 901)) return false;
        matches ??= (key, id) => key == id;
        var values = resource.Type == 901 ? Slots(quest.Value) : Values(resource);
        var status = Slots(quest.Status);
        var before = (int[])status.Clone();
        if (resource.Type == 101)
        {
            if (Enumerable.Range(0, 3).Any(i => values[i * 2] != 0 && matches(values[i * 2], monsterId)))
                status[0] = Math.Min(values[1], status[0] + 1);
        }
        else
        {
            for (var i = 0; i < 3; i++)
                if (values[i * 2] != 0 && matches(values[i * 2], monsterId) && status[i] < values[i * 2 + 1])
                {
                    status[i]++;
                    break;
                }
        }
        if (before.SequenceEqual(status)) return false;
        quest.Status = status;
        quest.Progress = FinishableNow(resource, quest) ? Finishable : InProgress;
        return true;
    }

    public static bool ApplyItemCounts(QuestResourceEntity resource, CharacterQuestEntity quest,
        IReadOnlyDictionary<int, long> counts)
    {
        if (resource.Type is not (103 or 106 or 107 or 109)) return false;
        var values = Values(resource);
        var status = Slots(quest.Status);
        var changed = false;
        var slots = resource.Type == 103 ? 6 : 3;
        var available = new Dictionary<int, long>(counts);
        for (var i = 0; i < slots; i++)
        {
            var code = values[i * 2];
            var next = code > 0 && available.TryGetValue(code, out var count)
                ? (int)Math.Min(values[i * 2 + 1], count) : 0;
            if (code > 0) available[code] = available.GetValueOrDefault(code) - next;
            if (status[i] == next) continue;
            status[i] = next;
            changed = true;
        }
        if (!changed) return false;
        quest.Status = status;
        quest.Progress = FinishableNow(resource, quest) ? Finishable : InProgress;
        return true;
    }

    public static bool FinishableNow(QuestResourceEntity resource, CharacterQuestEntity quest)
    {
        var values = resource.Type == 901 ? Slots(quest.Value) : Values(resource);
        var status = Slots(quest.Status);
        if (resource.Type == 401) return true;
        if (resource.Type == 101) return status[0] >= values[1];
        if (resource.Type is 102 or 106 or 107 or 109 or 301 or 302 or 901)
            return Enumerable.Range(0, 3).All(i => status[i] >= values[i * 2 + 1]);
        if (resource.Type == 201)
            return Enumerable.Range(0, 3).All(i => values[i * 2] == 0 || status[i] >= values[i * 2 + 1]);
        if (resource.Type == 501) return status[0] >= values[0] && status[1] >= values[1];
        if (resource.Type == 601)
            return Enumerable.Range(0, 3).All(i => values[i * 3] == 0 || values[i * 3 + 1] switch
            {
                -2 => status[i] < values[i * 3 + 2], -1 => status[i] <= values[i * 3 + 2],
                0 => status[i] == values[i * 3 + 2], 1 => status[i] >= values[i * 3 + 2],
                2 => status[i] > values[i * 3 + 2], _ => false
            });
        if (resource.Type == 103)
            return Enumerable.Range(0, 6).All(i => status[i] >= values[i * 2 + 1]);
        return false;
    }

    public static bool ApplyPlayerState(QuestResourceEntity resource, CharacterQuestEntity quest,
        ConnectionInfo player, IEnumerable<ItemEntity> items, int jobDepth)
    {
        if (resource.Type is not (201 or 301 or 302 or 501 or 601)) return false;
        var values = Values(resource);
        var status = Slots(quest.Status);
        var before = (int[])status.Clone();
        var carried = items.Where(i => i.DeletedOn == null && i.Amount > 0 && i.StorageId == null && i.AuctionId == null).ToArray();
        if (resource.Type == 501) { status[0] = jobDepth; status[1] = player.CharacterJobLevel; }
        else for (var i = 0; i < 3; i++)
        {
            var key = values[i * 2];
            status[i] = resource.Type switch
            {
                201 => Math.Min(values[i * 2 + 1], player.LearnedSkills.GetValueOrDefault(key)),
                301 => (int)Math.Min(values[i * 2 + 1], carried.Where(item => (int)item.WearInfo == key
                    && item.EquippedBySummonId == null).Select(item => (long)item.Level).DefaultIfEmpty(0).Max()),
                302 => (int)Math.Min(values[i * 2 + 1], carried.Where(item => item.ItemResourceId == key)
                    .Select(item => (long)item.Enhance).DefaultIfEmpty(0).Max()),
                601 => values[i * 3] == 99 ? player.CharacterChaos : 0,
                _ => 0
            };
        }
        var progress = FinishableWithStatus(resource, quest, status) ? Finishable : InProgress;
        if (status.SequenceEqual(before) && quest.Progress == progress) return false;
        quest.Status = status;
        quest.Progress = progress;
        return true;
    }

    private static bool FinishableWithStatus(QuestResourceEntity resource, CharacterQuestEntity quest, int[] status)
    {
        var previous = quest.Status;
        quest.Status = status;
        var finishable = FinishableNow(resource, quest);
        quest.Status = previous;
        return finishable;
    }

    public static int[] Slots(int[] status)
    {
        var result = new int[6];
        if (status is not null) Array.Copy(status, result, Math.Min(status.Length, result.Length));
        return result;
    }
}
