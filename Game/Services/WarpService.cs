using System;
using System.Collections.Generic;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Pets;
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
    private readonly IPetSummonService _petSummon;
    private readonly Creatures.ICreatureService _creatures;
    private readonly IPlayerVisibilityService _playerVisibility;
    private readonly IGroundItemService _groundItems;
    private readonly Dungeons.DungeonRooms _dungeons;

    public WarpService(INpcSpawnService npcSpawnService, IMonsterSpawnService monsterSpawnService,
        IFieldPropService fieldPropService, ICombatService combatService, IPetSummonService petSummon,
        IPlayerVisibilityService playerVisibility, IGroundItemService groundItems,
        Casting.ICastInterrupts casts = null, Compete.ICompeteService compete = null,
        Creatures.ICreatureService creatures = null, Dungeons.DungeonRooms dungeons = null)
    {
        _dungeons = dungeons;
        _creatures = creatures;
        _compete = compete;
        _casts = casts;
        _petSummon = petSummon;
        _npcSpawnService = npcSpawnService;
        _monsterSpawnService = monsterSpawnService;
        _fieldPropService = fieldPropService;
        _combatService = combatService;
        _playerVisibility = playerVisibility;
        _groundItems = groundItems;
    }

    /// <summary>WarpBegin cancels the cast in progress (StructCreature::CancelSkill).</summary>
    private readonly Casting.ICastInterrupts _casts;
    private readonly Compete.ICompeteService _compete;

    public void Warp(GameClient client, float x, float y)
        => Warp(client, x, y, client.ConnectionInfo.Layer);

    public void Warp(GameClient client, float x, float y, byte layer)
    {
        var info = client.ConnectionInfo;

        try
        {
            // The current target is about to be a world away, so the swing loop has to stop before
            // the position changes rather than keep hitting across the map.
            _combatService.StopAttack(client);
            _casts?.Interrupt(client);
            _compete?.Leave(client, Compete.CompeteEndType.LeftField);
            _combatService.DropAggro(client);
            info.TargetHandle = 0;

            LeaveEverything(client);

            info.Layer = _dungeons?.OnWarp(client, x, y, layer) ?? layer;

            info.X = x;
            info.Y = y;
            info.DestinationX = x;
            info.DestinationY = y;
            info.MoveStartTick = ServerClock.Now;
            client.Connection.Send(GameSpawnPackets.BuildWarp(x, y, 0f, (sbyte)info.Layer));

            _npcSpawnService.Sync(client);
            _monsterSpawnService.Sync(client);
            _fieldPropService.Sync(client);
            _groundItems.Sync(client);

            // The new place registers itself in the presence index and the pairs exchange their ENTER:
            // the players' side of the re-entry (docs/packet-specs/socle-visibilite-joueurs.md §5.3,
            // trigger 4).
            _playerVisibility.EnterWorld(client);

            // The pet is not in a visible set: it follows its master to the new place explicitly.
            _petSummon.FollowWarp(client);
            _creatures?.FollowWarp(client);

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
    private void LeaveEverything(GameClient client)
    {
        var info = client.ConnectionInfo;

        // The players' pass: the departure is announced to every observer, and the parting client is
        // told its peers are gone, exactly as the object sets below are emptied for it.
        _playerVisibility.LeaveWorld(client, notifyWalker: true);
        _groundItems.LeaveWorld(client);

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
