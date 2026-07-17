using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class GroundItemService : IGroundItemService
{
    private const int TickIntervalMs = 1000;
    private const int LifetimeSeconds = 120;
    private const float ScatterRadius = 30f;
    private const float PickupRange = 300f;
    private const double DropChanceMultiplier = 1;
    private const ushort TakeRequestId = (ushort)GamePackets.TM_CS_TAKE_ITEM;

    private readonly ILogger _logger = Log.ForContext<GroundItemService>();
    private readonly IMonsterDropCatalog _catalog;
    private readonly ICharacterService _characterService;
    private readonly ConcurrentDictionary<uint, GroundItem> _items = new();
    private readonly Random _random = new();

    public GroundItemService(IMonsterDropCatalog catalog, ICharacterService characterService)
    {
        _catalog = catalog;
        _characterService = characterService;
        _ = RunAsync();
    }

    public void DropForMonster(GameClient killer, int monsterId, float x, float y, float z)
    {
        var entries = _catalog.GetDrops(monsterId);
        if (entries.Count == 0)
        {
            _logger.Debug("Monster {monsterId} has no drop table", monsterId);
            return;
        }

        IReadOnlyList<DroppedItem> rolled;
        lock (_random)
        {
            rolled = DropRoll.Roll(entries, _random, DropChanceMultiplier);
        }

        _logger.Debug("Monster {monsterId} dropped {dropped} of {entries} entries", monsterId, rolled.Count,
            entries.Count);

        if (rolled.Count == 0)
        {
            return;
        }

        var info = killer.ConnectionInfo;
        var expiresAt = DateTime.UtcNow.AddSeconds(LifetimeSeconds);

        foreach (var drop in rolled)
        {
            var (offsetX, offsetY) = NextScatter();
            var item = new GroundItem
            {
                Handle = WorldObjectHandle.Next(),
                ItemCode = drop.ItemId,
                Count = drop.Count,
                X = x + offsetX,
                Y = y + offsetY,
                Z = z,
                Layer = info.Layer,
                Owner = killer,
                OwnerHandle = info.CharacterHandle,
                ExpiresAt = expiresAt
            };

            _items[item.Handle] = item;

            var dropTime = unchecked(ServerClock.Now + info.ClientClockOffset);
            killer.Connection.Send(GameSpawnPackets.BuildEnterItem(item.Handle, item.X, item.Y, item.Z,
                item.Layer, item.ItemCode, item.Count, dropTime, item.OwnerHandle));
        }
    }

    public async Task TakeAsync(GameClient client, uint itemHandle)
    {
        if (!_items.TryGetValue(itemHandle, out var item) || !ReferenceEquals(item.Owner, client))
        {
            client.SendResult(TakeRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        if (!WithinPickupRange(client.ConnectionInfo, item))
        {
            client.SendResult(TakeRequestId, (ushort)ResultCode.TooFar, 0);
            return;
        }

        if (Interlocked.CompareExchange(ref item.TakenBy, 1, 0) != 0)
        {
            client.SendResult(TakeRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        try
        {
            var added = await _characterService.AddItemAsync(client.ConnectionInfo.CharacterName,
                item.ItemCode, item.Count);
            if (added is null)
            {
                item.TakenBy = 0;
                client.SendResult(TakeRequestId, (ushort)ResultCode.NotExist, 0);
                return;
            }

            client.Connection.Send(GameSpawnPackets.BuildTakeItemResult(item.Handle,
                client.ConnectionInfo.CharacterHandle));
            Remove(item);

            foreach (var packet in GameCharacterPackets.BuildInventory(new[] { added }))
            {
                client.Connection.Send(packet);
            }

            client.SendResult(TakeRequestId, (ushort)ResultCode.Success, 0);
        }
        catch (Exception exception)
        {
            item.TakenBy = 0;
            _logger.Error(exception, "Could not take item {itemHandle} for {clientTag}", itemHandle,
                client.ClientTag);
            client.SendResult(TakeRequestId, (ushort)ResultCode.DBError, 0);
        }
    }

    private (float X, float Y) NextScatter()
    {
        lock (_random)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = _random.NextDouble() * ScatterRadius;
            return ((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
        }
    }

    private static bool WithinPickupRange(ConnectionInfo info, GroundItem item)
    {
        var dx = info.X - item.X;
        var dy = info.Y - item.Y;
        return dx * dx + dy * dy <= PickupRange * PickupRange;
    }

    private void Remove(GroundItem item)
    {
        _items.TryRemove(item.Handle, out _);
        item.Owner.Connection.Send(GameSpawnPackets.BuildLeave(item.Handle));
    }

    private async Task RunAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Expire(DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Ground item expiry tick failed");
            }
        }
    }

    private void Expire(DateTime now)
    {
        foreach (var pair in _items)
        {
            if (pair.Value.ExpiresAt <= now && Interlocked.CompareExchange(ref pair.Value.TakenBy, 1, 0) == 0)
            {
                Remove(pair.Value);
            }
        }
    }
}
