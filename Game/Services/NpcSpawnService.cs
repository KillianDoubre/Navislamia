using System;
using System.Collections.Generic;
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
    private readonly object _lock = new();
    private SpatialIndex<NpcResourceEntity> _index;

    public NpcSpawnService(INpcResourceRepository repository)
    {
        _repository = repository;
        Load();
    }

    private void Load()
    {
        try
        {
            var npcs = _repository.GetAll();
            _index = new SpatialIndex<NpcResourceEntity>(npcs, npc => npc.X, npc => npc.Y,
                WorldVisibility.ViewRange);
            _logger.Information("Loaded and indexed {count} NPCs", _index.Count);
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
            var inRange = GetIndex()?.WithinRange(info.X, info.Y, WorldVisibility.ViewRange)
                ?? Array.Empty<NpcResourceEntity>();
            var inRangeIds = new HashSet<long>(inRange.Count);

            lock (info.NpcVisibilityLock)
            {
                foreach (var npc in inRange)
                {
                    inRangeIds.Add(npc.Id);

                    if (info.SpawnedNpcs.ContainsKey(npc.Id))
                    {
                        continue;
                    }

                    var handle = WorldObjectHandle.Next();
                    client.Connection.Send(GameSpawnPackets.BuildEnterNpc(handle, npc.X, npc.Y, npc.Z,
                        info.Layer, npc.Hp, npc.Level, (byte)npc.RaceId, (int)npc.Id));
                    info.SpawnedNpcs[npc.Id] = handle;
                    info.SpawnedNpcIdsByHandle[handle] = npc.Id;
                }

                SpawnedObjectSet.DespawnMissing(client.Connection, info.SpawnedNpcs, inRangeIds,
                    info.SpawnedNpcIdsByHandle);

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
