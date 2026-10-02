using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Buffs;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    public async Task RestoreBuffsAsync(GameClient client)
    {
        if (_buffPersistence is null) return;
        var info = client.ConnectionInfo;
        var rows = await _buffPersistence.LoadAsync(info.CharacterHandle);
        var now = ServerClock.Now; var utc = DateTime.UtcNow;
        lock (info.BuffLock)
        {
            info.ActiveBuffs.Clear(); info.ActiveAuras.Clear(); info.StoredSummonBuffs.Clear();
            foreach (var row in rows)
            {
                if (row.SummonCardId == 0 && row.StateId == 0 && row.Infinite
                    && _catalog.TryGet(row.SkillId, out var aura) && aura.Kind == SkillCastKind.Aura
                    && (_stateCatalog.GetRule(aura.StateId).TimeType
                        & DataAccess.Entities.Enums.StateTimeType.EraseOnLogout) == 0)
                { info.ActiveAuras[aura.ToggleGroup] = row.SkillId; continue; }
                var state = BuffPersistenceRules.Restore(row, _stateCatalog, now, utc, ++info.NextStateHandle);
                if (state is null) continue;
                if (row.SummonCardId == 0)
                {
                    info.ActiveBuffs.Add(state.Value);
                    if (row.Infinite && _catalog.TryGet(row.SkillId, out var skill) && skill.Kind == SkillCastKind.Aura)
                        info.ActiveAuras[skill.ToggleGroup] = row.SkillId;
                }
                else if (row.SummonCardId is > 0 and <= uint.MaxValue)
                {
                    var card = (uint)row.SummonCardId;
                    if (!info.StoredSummonBuffs.TryGetValue(card, out var states))
                        info.StoredSummonBuffs[card] = states = new List<ActiveBuff>();
                    states.Add(state.Value);
                }
            }
        }
        _statService.RefreshBuffs(info);
    }

    public Task SaveBuffsAsync(GameClient client)
    {
        if (_buffPersistence is null) return Task.CompletedTask;
        var info = client.ConnectionInfo; var now = ServerClock.Now; var utc = DateTime.UtcNow;
        var rows = new List<CharacterStateEntity>();
        void Capture(IEnumerable<ActiveBuff> states, long card)
        {
            foreach (var state in states)
                if (BuffPersistenceRules.Capture(state, card, _stateCatalog, now, utc) is { } row) rows.Add(row);
        }
        lock (info.BuffLock)
        {
            Capture(info.ActiveBuffs, 0);
            foreach (var skillId in info.ActiveAuras.Values)
                if (!info.ActiveBuffs.Any(s => s.SkillId == skillId && !s.AuraProjection)
                    && _catalog.TryGet(skillId, out var aura)
                    && (_stateCatalog.GetRule(aura.StateId).TimeType
                        & DataAccess.Entities.Enums.StateTimeType.EraseOnLogout) == 0)
                    rows.Add(new CharacterStateEntity { StateId = 0, SkillId = skillId, Infinite = true, SavedAtUtc = utc });
            foreach (var (card, states) in info.StoredSummonBuffs) Capture(states, card);
        }
        foreach (var summon in info.Summons)
            if (summon.Entry.CardHandle != 0)
                lock (summon.BuffLock) Capture(summon.ActiveBuffs, summon.Entry.CardHandle);
        return _buffPersistence.SaveAsync(info.CharacterHandle, rows);
    }

    public void SynchronizeBuffs(GameClient client)
    {
        foreach (var packet in CompanionFrames.States(client.ConnectionInfo)) client.Connection.Send(packet);
    }
}
