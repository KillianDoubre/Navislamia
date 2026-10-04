using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services;

public static class EventAreaActivation
{
    public static string CountKey(long id) => "event_area_" + id + "_count";
    private static readonly int[] Jobs = { 100,101,102,103,110,111,112,113,114,120,121,122,123,124,
        200,201,202,203,210,211,212,213,214,220,221,222,223,224,
        300,301,302,303,310,311,312,313,314,320,321,322,323,324 };
    public static bool Allows(EventAreaResourceEntity row, GameClient client, CharacterEntity character,
        IReadOnlyDictionary<string,string> flags, DateTimeOffset now, IQuestService quests)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHp <= 0) return false;
        var seconds = (int)TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).TimeOfDay.TotalSeconds;
        if (row.BeginTime > 0 && seconds < row.BeginTime || row.EndTime > 0 && seconds > row.EndTime
            || row.MinLevel > 0 && info.CharacterLevel < row.MinLevel || row.MaxLevel > 0 && info.CharacterLevel > row.MaxLevel)
            return false;
        if (row.CountLimit > 0 && long.TryParse(flags.GetValueOrDefault(CountKey(row.Id)), out var count) && count >= row.CountLimit)
            return false;
        var raceBit = info.CharacterRace switch { 3 => 1L, 4 => 2L, 5 => 4L, _ => 0L };
        if ((row.RaceJobLimit & 7L) != 0 && (raceBit == 0 || (row.RaceJobLimit & raceBit) == 0)) return false;
        var jobIndex = Array.IndexOf(Jobs, info.CharacterJob);
        if ((row.RaceJobLimit & ~7L) != 0 && (jobIndex < 0 || (row.RaceJobLimit & (1L << (3 + jobIndex))) == 0)) return false;
        var inventory = (character.Items ?? Array.Empty<ItemEntity>()).Where(i => i.Amount > 0 && i.AccountId == null
            && i.AuctionId == null && i.StorageId == null).ToArray();
        for (var i = 0; i < row.Conditions.Length; i++)
        {
            if (row.Conditions[i] == 0) continue;
            if (row.Values.Length < i * 2 + 2) return false;
            var code = row.Values[i * 2]; var value = row.Values[i * 2 + 1];
            bool Presence(bool present) => value == 1 && present || value == 2 && !present;
            switch (row.Conditions[i])
            {
                case 1: if (inventory.Where(item => item.ItemResourceId == code).Sum(item => item.Amount) < value) return false; break;
                case 2: if (quests?.GetQuestProgressAsync(client, code).GetAwaiter().GetResult() != value) return false; break;
                case 3: if (character.Skills?.Any(s => s.SkillId == code && s.Level >= value) != true) return false; break;
                case 4: if (!Presence(inventory.Any(item => item.ItemResourceId == code && item.WearInfo != ItemWearType.None
                    && item.EquippedBySummonId == null))) return false; break;
                case 5: if (!Presence(info.Summons.Any(s => s.Entry.Code == code))) return false; break;
                case 6: lock (info.BuffLock) if (!Presence(info.ActiveBuffs.Any(s => s.StateId == code
                    && (s.EndTick == uint.MaxValue || unchecked((int)(s.EndTick - ServerClock.Now)) > 0)))) return false; break;
                default: return false;
            }
        }
        return true;
    }
}
