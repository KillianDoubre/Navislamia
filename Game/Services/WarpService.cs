using System;
using System.Collections.Generic;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Moves a character across the world, mirroring the reference server's World::WarpBegin/WarpEnd:
/// leave the world, take the new position, warp, re-enter and re-stream the surroundings.
/// </summary>
public class WarpService : IWarpService
{
    private readonly ILogger _logger = Log.ForContext<WarpService>();
    private readonly INpcSpawnService _npcSpawnService;
    private readonly IMonsterSpawnService _monsterSpawnService;
    private readonly IFieldPropService _fieldPropService;
    private readonly ICombatService _combatService;

    public WarpService(INpcSpawnService npcSpawnService, IMonsterSpawnService monsterSpawnService,
        IFieldPropService fieldPropService, ICombatService combatService)
    {
        _npcSpawnService = npcSpawnService;
        _monsterSpawnService = monsterSpawnService;
        _fieldPropService = fieldPropService;
        _combatService = combatService;
    }

    public void Warp(GameClient client, float x, float y)
    {
        var info = client.ConnectionInfo;

        try
        {
            // The current target is about to be a world away, so the swing loop has to stop before
            // the position changes rather than keep hitting across the map.
            _combatService.StopAttack(client);
            info.TargetHandle = 0;

            LeaveEverything(client);

            info.X = x;
            info.Y = y;
            client.Connection.Send(GameSpawnPackets.BuildWarp(x, y, 0f, (sbyte)info.Layer));

            _npcSpawnService.Sync(client);
            _monsterSpawnService.Sync(client);
            _fieldPropService.Sync(client);

            _logger.Debug("{clientTag} warped to ({x}, {y})", client.ClientTag, x, y);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "{clientTag} warp to ({x}, {y}) failed", client.ClientTag, x, y);
        }
    }

    /// <summary>
    /// The client keeps every object it was told about until it is told otherwise, so a warp that
    /// skips this leaves the old zone's objects floating at the new one.
    /// </summary>
    private static void LeaveEverything(GameClient client)
    {
        var info = client.ConnectionInfo;

        LeaveAll(client, info.NpcVisibilityLock, info.SpawnedNpcs, info.SpawnedNpcIdsByHandle);
        LeaveAll(client, info.MonsterVisibilityLock, info.SpawnedMonsters);
        LeaveAll(client, info.PropVisibilityLock, info.SpawnedProps, info.SpawnedPropInstancesByHandle);

        lock (info.NpcVisibilityLock)
        {
            info.ClearNpcDialog();
        }
    }

    private static void LeaveAll(GameClient client, object visibilityLock, Dictionary<long, uint> spawned,
        Dictionary<uint, long> idsByHandle = null)
    {
        lock (visibilityLock)
        {
            foreach (var handle in spawned.Values)
            {
                client.Connection.Send(GameSpawnPackets.BuildLeave(handle));
            }

            spawned.Clear();
            idsByHandle?.Clear();
        }
    }
}
