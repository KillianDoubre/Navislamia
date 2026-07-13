using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class NpcSpawnService : INpcSpawnService
{
    private const float NpcViewRange = 15000f;

    private readonly ILogger _logger = Log.ForContext<NpcSpawnService>();
    private readonly INpcResourceRepository _repository;
    private readonly object _lock = new();
    private IReadOnlyList<NpcResourceEntity> _npcs;

    public NpcSpawnService(INpcResourceRepository repository)
    {
        _repository = repository;
    }

    public void Sync(GameClient client)
    {
        try
        {
            var npcs = GetNpcs();
            var info = client.ConnectionInfo;
            var inRange = NpcSpawnSelector.WithinRange(npcs, info.X, info.Y, NpcViewRange).ToList();

            var inRangeIds = new HashSet<long>(inRange.Count);
            foreach (var npc in inRange)
            {
                inRangeIds.Add(npc.Id);
            }

            foreach (var npc in inRange)
            {
                if (info.SpawnedNpcs.ContainsKey(npc.Id))
                {
                    continue;
                }

                var handle = WorldObjectHandle.Next();
                client.Connection.Send(GameSpawnPackets.BuildEnterNpc(handle, npc.X, npc.Y, npc.Z, info.Layer,
                    npc.Hp, npc.Level, (byte)npc.RaceId, (int)npc.Id));
                info.SpawnedNpcs[npc.Id] = handle;
            }

            foreach (var id in info.SpawnedNpcs.Keys.ToList())
            {
                if (inRangeIds.Contains(id))
                {
                    continue;
                }

                client.Connection.Send(GameSpawnPackets.BuildLeave(info.SpawnedNpcs[id]));
                info.SpawnedNpcs.Remove(id);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} NPC sync failed", client.ClientTag);
        }
    }

    private IReadOnlyList<NpcResourceEntity> GetNpcs()
    {
        if (_npcs != null)
        {
            return _npcs;
        }

        lock (_lock)
        {
            _npcs ??= _repository.GetAll();
        }

        return _npcs;
    }
}
