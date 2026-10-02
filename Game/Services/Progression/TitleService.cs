using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Progression;

public interface ITitleService
{
    Task RefreshAsync(GameClient client);
    Task RecordMonsterKillAsync(GameClient client, MonsterInstance monster);
    Task<bool> SetConditionAsync(GameClient client, int conditionId, long count);
    Task<IReadOnlyList<int>> GetOwnedAsync(GameClient client);
    Task<bool> SelectAsync(GameClient client, int titleId);
}

public sealed class TitleService : ITitleService
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _gate;
    private readonly TitleCatalog _catalog;
    private readonly IStatService _stats;
    private readonly ILogger _logger = Log.ForContext<TitleService>();

    public TitleService(DbContextOptions<TelecasterContext> options, CharacterGate gate, TitleCatalog catalog,
        IStatService stats)
    { _options = options; _gate = gate; _catalog = catalog; _stats = stats; }

    public Task RefreshAsync(GameClient client) => UpdateAsync(client, null);

    public Task RecordMonsterKillAsync(GameClient client, MonsterInstance monster) => UpdateAsync(client, counts =>
    {
        foreach (var type in _catalog.Types.Values)
            if (type.Category == 1001 && type.Values[0] == monster.MonsterId
                || type.Category == 1002 && monster.TamingId > 0 && type.Values[0] == monster.TamingId)
                counts[type.Id] = type.Set ? 1 : CombatRewards.AddProgress(counts.GetValueOrDefault(type.Id), 1);
    });

    public async Task<bool> SetConditionAsync(GameClient client, int conditionId, long count)
    {
        if (!_catalog.Types.ContainsKey(conditionId) || count < 0) return false;
        return await UpdateAsync(client, counts => counts[conditionId] = _catalog.Types[conditionId].Set
            ? count : CombatRewards.AddProgress(counts.GetValueOrDefault(conditionId), count));
    }

    private async Task<bool> UpdateAsync(GameClient client, Action<Dictionary<int, long>> update)
    {
        var name = client.ConnectionInfo.CharacterName;
        if (client.ConnectionInfo.CharacterHandle == 0 || string.IsNullOrEmpty(name)) return false;
        try
        {
            return await _gate.RunAsync(name, async () =>
            {
                await using var db = new TelecasterContext(_options);
                var character = await db.Characters.Include(c => c.Skills).SingleOrDefaultAsync(c => c.CharacterName == name);
                if (character is null) return false;
                var state = await db.CharacterTitleStates.SingleOrDefaultAsync(s => s.CharacterId == character.Id);
                if (state is null)
                {
                    state = new CharacterTitleStateEntity { CharacterId = character.Id };
                    db.CharacterTitleStates.Add(state);
                }
                var counters = state.ConditionIds.Select((id, i) => (id, count: state.ConditionCounts[i]))
                    .ToDictionary(c => c.id, c => c.count);
                var opened = state.OpenedTitleIds.ToHashSet();
                var owned = state.OwnedTitleIds.ToHashSet();
                var started = (await db.CharacterQuests.IgnoreQueryFilters().Where(q => q.CharacterId == character.Id)
                    .Select(q => q.Code).ToArrayAsync()).ToHashSet();
                var ended = (await db.CharacterQuestCompletions.Where(q => q.CharacterId == character.Id)
                    .Select(q => q.Code).ToArrayAsync()).ToHashSet();
                foreach (var type in _catalog.Types.Values)
                {
                    long? value = type.Category switch
                    {
                        4001 => started.Contains(type.Values[0]) || ended.Contains(type.Values[0]) ? 1 : 0,
                        4002 => ended.Contains(type.Values[0]) ? 1 : 0,
                        7001 => character.Skills?.Any(s => s.SkillId == type.Values[0] && s.Level >= type.Values[1]) == true ? 1 : 0,
                        9001 => client.ConnectionInfo.CharacterGold >= type.Values[0] ? 1 : 0,
                        6001 => client.ConnectionInfo.PkMode ? 1 : 0,
                        6101 => client.ConnectionInfo.ImmoralPoint >= type.Values[0]
                            && client.ConnectionInfo.ImmoralPoint <= type.Values[1] ? 1 : 0,
                        _ => null
                    };
                    if (value.HasValue) counters[type.Id] = value.Value;
                }
                update?.Invoke(counters);
                var acquired = new List<int>();
                bool changed;
                do
                {
                    changed = false;
                    foreach (var type in _catalog.Types.Values.Where(t => t.Category == 8001))
                        counters[type.Id] = owned.Contains(type.Values[0]) ? 1 : 0;
                    foreach (var (id, conditions) in _catalog.Conditions.OrderBy(p => p.Key))
                    {
                        if (owned.Contains(id) || !_catalog.IsAvailable(id, DateTime.UtcNow)) continue;
                        if (!opened.Contains(id) && TitleCatalog.Satisfied(conditions.Where(c => !c.Achieve), counters))
                            opened.Add(id);
                        if (opened.Contains(id) && TitleCatalog.Satisfied(conditions.Where(c => c.Achieve), counters))
                        { owned.Add(id); acquired.Add(id); changed = true; }
                    }
                } while (changed);
                state.OpenedTitleIds = opened.OrderBy(id => id).ToArray();
                state.OwnedTitleIds = owned.OrderBy(id => id).ToArray();
                state.ConditionIds = counters.Keys.OrderBy(id => id).ToArray();
                state.ConditionCounts = state.ConditionIds.Select(id => counters[id]).ToArray();
                if (character.MainTitleId != 0 && !_catalog.IsAvailable(character.MainTitleId, DateTime.UtcNow))
                    character.MainTitleId = 0;
                if (character.MainTitleId == 0 && acquired.Count > 0) character.MainTitleId = acquired[0];
                await db.SaveChangesAsync();
                ApplySelection(client, character.MainTitleId);
                foreach (var id in acquired) client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", 2,
                    $"Title acquired: {id}. /titles, /title <id>"));
                return true;
            });
        }
        catch (Exception ex) { _logger.Error(ex, "Could not update titles for {Character}", name); return false; }
    }

    public async Task<IReadOnlyList<int>> GetOwnedAsync(GameClient client)
    {
        await RefreshAsync(client);
        await using var db = new TelecasterContext(_options);
        var state = await db.CharacterTitleStates.SingleOrDefaultAsync(s => s.CharacterId == client.ConnectionInfo.CharacterHandle);
        return state?.OwnedTitleIds ?? Array.Empty<int>();
    }

    public async Task<bool> SelectAsync(GameClient client, int titleId)
    {
        if (titleId < 0) return false;
        await RefreshAsync(client);
        return await _gate.RunAsync(client.ConnectionInfo.CharacterName, async () =>
        {
            await using var db = new TelecasterContext(_options);
            var character = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == client.ConnectionInfo.CharacterName);
            if (character is null) return false;
            var state = await db.CharacterTitleStates.SingleOrDefaultAsync(s => s.CharacterId == character.Id);
            if (titleId != 0 && (state?.OwnedTitleIds.Contains(titleId) != true || !_catalog.IsAvailable(titleId, DateTime.UtcNow)))
                return false;
            character.MainTitleId = titleId;
            await db.SaveChangesAsync();
            ApplySelection(client, titleId);
            return true;
        });
    }

    private void ApplySelection(GameClient client, int id)
    {
        var info = client.ConnectionInfo;
        if (info.MainTitleId == id) return;
        info.MainTitleId = id;
        info.TitleEffects = _catalog.GetEffects(id);
        var result = _stats.Compute(info);
        client.Connection.Send(GameStatPackets.BuildStatInfo(info.CharacterHandle, result.Total,
            StatInfoType.Total));
        info.CharacterMaxHp = (int)result.Total.MaxHp;
        info.CharacterHp = Math.Min(info.CharacterHp, info.CharacterMaxHp);
        info.CharacterMp = Math.Min(info.CharacterMp, (int)result.Total.MaxMp);
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "max_hp", info.CharacterMaxHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "max_mp", (int)result.Total.MaxMp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
    }
}
