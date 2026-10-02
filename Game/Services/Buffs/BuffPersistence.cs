using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Buffs;

public interface IBuffPersistence
{
    Task<CharacterStateEntity[]> LoadAsync(long characterId);
    Task SaveAsync(long characterId, IReadOnlyList<CharacterStateEntity> states);
}

public sealed class BuffPersistence : IBuffPersistence
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _gate;
    public BuffPersistence(DbContextOptions<TelecasterContext> options, CharacterGate gate)
    { _options = options; _gate = gate; }

    public Task<CharacterStateEntity[]> LoadAsync(long characterId)
    {
        // A fast reconnection waits for an already-started logout save of this character.
        return _gate.RunAsync("states:" + characterId, async () =>
        {
            await using var db = new TelecasterContext(_options);
            return await db.CharacterStates.AsNoTracking().Where(s => s.CharacterId == characterId)
                .OrderBy(s => s.Id).ToArrayAsync();
        });
    }

    public async Task SaveAsync(long characterId, IReadOnlyList<CharacterStateEntity> states)
    {
        if (characterId <= 0) return;
        await _gate.RunAsync("states:" + characterId, async () =>
        {
            await using var db = new TelecasterContext(_options);
            // One SaveChanges transaction replaces the snapshot without leaving a partial list.
            db.CharacterStates.RemoveRange(await db.CharacterStates.Where(s => s.CharacterId == characterId).ToArrayAsync());
            foreach (var state in states)
            { state.Id = 0; state.CharacterId = characterId; db.CharacterStates.Add(state); }
            await db.SaveChangesAsync();
        });
    }
}

public static class BuffPersistenceRules
{
    public static CharacterStateEntity Capture(ActiveBuff state, long cardId, IStateCatalog catalog,
        uint now, DateTime utc)
    {
        if (state.AuraProjection || (catalog.GetRule(state.StateId).TimeType & StateTimeType.EraseOnLogout) != 0)
            return null;
        var infinite = state.EndTick == uint.MaxValue;
        var remaining = infinite ? 0 : Math.Max(0, unchecked((int)(state.EndTick - now)));
        if (!infinite && remaining == 0) return null;
        return new CharacterStateEntity { SummonCardId = cardId, StateId = state.StateId, SkillId = state.SkillId,
            StateLevel = state.StateLevel, Infinite = infinite, RemainingTicks = remaining, SavedAtUtc = utc };
    }

    public static ActiveBuff? Restore(CharacterStateEntity state, IStateCatalog catalog, uint now, DateTime utc,
        ushort handle)
    {
        if (!catalog.Exists(state.StateId) || state.StateLevel < 0
            || (catalog.GetRule(state.StateId).TimeType & StateTimeType.EraseOnLogout) != 0) return null;
        var remaining = Math.Clamp(state.RemainingTicks, 0, int.MaxValue);
        if (!state.Infinite && (catalog.GetRule(state.StateId).TimeType & StateTimeType.TimeDecreaseOnLogout) != 0)
            remaining -= (long)Math.Min(int.MaxValue, Math.Max(0, (utc - state.SavedAtUtc).TotalSeconds * ServerClock.TicksPerSecond));
        if (!state.Infinite && remaining <= 0) return null;
        return new ActiveBuff(handle, state.StateId, state.SkillId, state.StateLevel, now,
            state.Infinite ? uint.MaxValue : unchecked(now + (uint)remaining));
    }
}
