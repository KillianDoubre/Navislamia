using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class NpcSpawnService : INpcSpawnService
{
    private readonly ILogger _logger = Log.ForContext<NpcSpawnService>();
    private readonly INpcResourceRepository _repository;
    private readonly GameRuleOptions _rules;
    private readonly Func<DateTime> _utcNow;
    private readonly IReadOnlyDictionary<long, int> _clientFlags;
    private readonly object _lock = new();
    private SpatialIndex<NpcResourceEntity> _index;

    public NpcSpawnService(INpcResourceRepository repository, IOptions<GameRuleOptions> rules = null,
        Func<DateTime> utcNow = null, IReadOnlyDictionary<long, int> clientFlags = null)
    {
        _repository = repository;
        _rules = rules?.Value ?? new GameRuleOptions();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _clientFlags = clientFlags ?? NpcSpawnRules.ClientFlags;
        Load();
    }

    private void Load()
    {
        try
        {
            // onNPCData: the country, the live/test server bits and an elapsed period decide once, at load.
            var all = _repository.GetAll();
            foreach (var npc in all) npc.LocalFlag = NpcSpawnRules.EffectiveFlag(npc, _clientFlags);
            var now = _utcNow();
            var npcs = all.Where(npc => NpcSpawnRules.IsLoaded(npc, _rules.LocalFlag, _rules.ServiceServer, now)).ToList();
            _index = new SpatialIndex<NpcResourceEntity>(npcs, npc => npc.X, npc => npc.Y,
                WorldVisibility.ViewRange);
            _logger.Information("Loaded and indexed {count} of {total} NPCs (country flag {flag}, service server {service})",
                _index.Count, all.Count, _rules.LocalFlag, _rules.ServiceServer);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load NPCs at startup; will retry on first sync");
        }
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            // A periodic NPC stands only inside its period: before it nobody sees it, after it it leaves the view. An
            // event's NPC stands only while GameRules:Events opens its event (NpcEvents).
            var now = _utcNow();
            var localNow = now.ToLocalTime();
            var inRange = (GetIndex()?.WithinRange(info.X, info.Y, WorldVisibility.ViewRange)
                    ?? Array.Empty<NpcResourceEntity>())
                .Where(npc => NpcSpawnRules.IsPresent(npc, now) && NpcEvents.IsNpcShown(npc.Id, _rules.Events, localNow))
                .ToList();

            WorldObjectStreamer.Stream(client, info.NpcVisibilityLock, inRange,
                npc => npc.Id,
                (npc, handle) => GameSpawnPackets.BuildEnterNpc(handle, npc.X, npc.Y, npc.Z,
                    info.Layer, npc.Hp, npc.Level, (byte)npc.RaceId, (int)npc.Id,
                    info.NpcQuestMarks.GetValueOrDefault((int)npc.Id)),
                info.SpawnedNpcs,
                info.SpawnedNpcIdsByHandle);

            lock (info.NpcVisibilityLock)
            {
                // An open dialog dies with the NPC leaving the view.
                if (info.NpcDialogHandle != 0 &&
                    !info.SpawnedNpcIdsByHandle.ContainsKey(info.NpcDialogHandle))
                {
                    info.ClearNpcDialog();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} NPC sync failed", client.ClientTag);
        }
    }

    private SpatialIndex<NpcResourceEntity> GetIndex()
    {
        if (_index != null)
        {
            return _index;
        }

        lock (_lock)
        {
            if (_index == null)
            {
                Load();
            }
        }

        return _index;
    }
}
