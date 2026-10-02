using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Serilog;

namespace Navislamia.Game.Services;

public class GroundItemService : IGroundItemService
{
    private const int TickIntervalMs = 1000;
    private const float ScatterRadius = 30f;
    private const float PickupRange = 300f;
    private const ushort TakeRequestId = (ushort)GamePackets.TM_CS_TAKE_ITEM;

    private readonly ILogger _logger = Log.ForContext<GroundItemService>();
    private readonly IMonsterDropCatalog _catalog;
    private readonly ICharacterService _characterService;
    private readonly IItemGroupCatalog _itemGroups;
    private readonly IRateService _rates;
    private readonly IPlayerVisibilityService _players;
    private readonly Weight.ICarriedWeightService _weights;
    private readonly IPartyService _parties;
    private readonly ConcurrentDictionary<uint, GroundItem> _items = new();
    private readonly Random _random = new();

    public GroundItemService(IMonsterDropCatalog catalog, ICharacterService characterService,
        IItemGroupCatalog itemGroups, IRateService rates, IPlayerVisibilityService players,
        Weight.ICarriedWeightService weights = null, IPartyService parties = null)
    {
        _parties = parties;
        _weights = weights;
        _rates = rates;
        _catalog = catalog;
        _characterService = characterService;
        _itemGroups = itemGroups;
        _players = players;
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

        // Read once per kill: an event starting mid-roll must not apply to half a table.
        var dropRate = _rates.Get(RateType.ItemDrop);
        var cardRate = _rates.Get(RateType.CreatureCardDrop);

        IReadOnlyList<DroppedItem> rolled;
        lock (_random)
        {
            rolled = DropRoll.Roll(entries, _catalog.Groups, _random, dropRate,
                itemId => IsSummonCard(itemId) ? cardRate : 1);
        }

        _logger.Debug("Monster {monsterId} dropped {dropped} of {entries} entries", monsterId, rolled.Count,
            entries.Count);

        if (rolled.Count == 0)
        {
            return;
        }

        var info = killer.ConnectionInfo;
        var expiresAt = DateTime.UtcNow + _rates.GroundItemLifetime;

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
                PartyId = info.PartyId,
                MonsterDrop = true,
                ExpiresAt = expiresAt
            };

            _items[item.Handle] = item;

            ShowTo(killer, item);
            ShowToNearby(item, killer);
        }
    }

    public void DropQuestItem(GameClient owner, int itemId, float x, float y, float z)
    {
        if (itemId <= 0) return;
        var item = new GroundItem
        {
            Handle = WorldObjectHandle.Next(), ItemCode = itemId, Count = 1,
            X = x, Y = y, Z = z, Layer = owner.ConnectionInfo.Layer,
            Owner = owner, OwnerHandle = owner.ConnectionInfo.CharacterHandle,
            ExpiresAt = DateTime.UtcNow + _rates.GroundItemLifetime
        };
        _items[item.Handle] = item;
        ShowTo(owner, item);
        ShowToNearby(item, owner);
    }

    public void DropGoldForMonster(GameClient killer, long amount, float x, float y, float z)
    {
        if (amount <= 0) return;
        var p = NextScatter();
        var item = new GroundItem
        {
            Handle = WorldObjectHandle.Next(), ItemCode = 0, Count = amount,
            X = x + p.X, Y = y + p.Y, Z = z, Layer = killer.ConnectionInfo.Layer,
            Owner = killer, OwnerHandle = killer.ConnectionInfo.CharacterHandle,
            PartyId = killer.ConnectionInfo.PartyId, MonsterDrop = true,
            ExpiresAt = DateTime.UtcNow + _rates.GroundItemLifetime
        };
        _items[item.Handle] = item;
        ShowTo(killer, item);
        ShowToNearby(item, killer);
    }

    private bool IsSummonCard(int itemId) =>
        _itemGroups.TryGetGroup(itemId, out var group) && group == ItemGroup.Summoncard;

    /// <summary>
    /// <c>TM_CS_DROP_ITEM</c> (203). The item is created at the character's exact position (no
    /// dispersion, unlike monster drops) and lives as long as a monster drop. The refusals and the clamp
    /// are judged inside <see cref="ICharacterService.RemoveItemAsync"/>, atomically with the removal,
    /// which reports what it really removed: the ground spawn and the erase notification carry that
    /// count and the acknowledgement is <c>true</c> only when something was removed. NGemity answers
    /// <c>true</c> even when its own removal failed — a defect this implementation does not reproduce.
    /// </summary>
    public async Task DropFromInventoryAsync(GameClient client, uint itemHandle, int count)
    {
        if (count <= 0)
        {
            SendDropResult(client, itemHandle, false);
            return;
        }

        var info = client.ConnectionInfo;

        try
        {
            // The rules are judged inside the removal, under the database gate, so an equip handled
            // between a separate read and the erase cannot slip a worn item through.
            var removal = await _characterService.RemoveItemAsync(info.CharacterName, itemHandle,
                item => ResolveDropCount(item, count));
            if (removal.Removed <= 0)
            {
                SendDropResult(client, itemHandle, false);
                return;
            }

            var item = removal.Item;
            var dropped = new GroundItem
            {
                Handle = WorldObjectHandle.Next(),
                ItemCode = (int)item.ItemResourceId,
                Count = removal.Removed,
                X = info.X,
                Y = info.Y,
                Z = info.Z,
                Layer = info.Layer,
                Owner = client,
                OwnerHandle = info.CharacterHandle,
                ExpiresAt = DateTime.UtcNow + _rates.GroundItemLifetime
            };

            _items[dropped.Handle] = dropped;

            ShowTo(client, dropped);
            ShowToNearby(dropped, client);
            client.Connection.Send(GameCharacterPackets.BuildEraseItem(new[] { (itemHandle, removal.Removed) }));
            SendDropResult(client, itemHandle, true);

            _logger.Debug("{clientTag} dropped {count} of item {itemHandle} as ground item {handle}",
                client.ClientTag, dropped.Count, itemHandle, dropped.Handle);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not drop item {itemHandle} for {clientTag}", itemHandle,
                client.ClientTag);
            SendDropResult(client, itemHandle, false);
        }
    }

    public async Task TakeAsync(GameClient client, uint itemHandle)
    {
        if (!_items.TryGetValue(itemHandle, out var item) || !CanTake(client, item))
        {
            client.SendResult(TakeRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        if (!WithinPickupRange(client.ConnectionInfo, item))
        {
            client.SendResult(TakeRequestId, (ushort)ResultCode.TooFar, 0);
            return;
        }

        var result = await TakeAsync(client, item, client.ConnectionInfo.CharacterHandle);
        client.SendResult(TakeRequestId, (ushort)result, 0);
    }

    public bool TryFindNearest(GameClient owner, float x, float y, byte layer, float range, out GroundItemSpot spot)
    {
        spot = default;
        var best = float.MaxValue;
        foreach (var item in _items.Values)
        {
            if (!CanTake(owner, item) || item.Layer != layer || Volatile.Read(ref item.TakenBy) != 0)
            {
                continue;
            }

            var dx = item.X - x;
            var dy = item.Y - y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance <= range && distance < best)
            {
                best = distance;
                spot = new GroundItemSpot(item.Handle, item.X, item.Y);
            }
        }

        return best < float.MaxValue;
    }

    public async Task<bool> TakeForPetAsync(GameClient owner, uint itemHandle, uint petHandle)
    {
        if (!_items.TryGetValue(itemHandle, out var item) || !CanTake(owner, item))
        {
            return false;
        }

        return WithinPickupRange(owner.ConnectionInfo, item)
            && await TakeAsync(owner, item, petHandle) == ResultCode.Success;
    }

    private bool CanTake(GameClient picker, GroundItem item) =>
        ReferenceEquals(item.Owner, picker) || item.MonsterDrop && _parties is not null
            && _parties.CanTakeDrop(item.Owner, picker, item.PartyId);

    /// <summary>
    /// The take itself, shared by the player and its pet: claim the item so a second request cannot
    /// duplicate it, add it to the bag, then <c>TS_SC_TAKE_ITEM_RESULT</c> (210) naming the actor the client
    /// animates, the <c>TS_SC_LEAVE</c> and the one-record inventory.
    /// </summary>
    private async Task<ResultCode> TakeAsync(GameClient client, GroundItem item, uint takerHandle)
    {
        if (item.ItemCode == 0) return TakeGold(client, item, takerHandle);
        var recipient = item.MonsterDrop && _parties is not null
            ? _parties.LootRecipient(client, item.PartyId, item.X, item.Y, item.Layer)
            : client;
        // Player::IsTakeable: the item's weight on top of the load must stay within the maximum.
        if (_weights is not null && !_weights.CanCarry(recipient.ConnectionInfo, item.ItemCode, item.Count))
        {
            return ResultCode.TooHeavy;
        }

        if (Interlocked.CompareExchange(ref item.TakenBy, 1, 0) != 0)
        {
            return ResultCode.NotExist;
        }

        try
        {
            var added = await _characterService.AddItemAsync(recipient.ConnectionInfo.CharacterName,
                item.ItemCode, item.Count);
            if (added is null)
            {
                item.TakenBy = 0;
                return ResultCode.NotExist;
            }

            client.Connection.Send(GameSpawnPackets.BuildTakeItemResult(item.Handle, takerHandle));
            Remove(item);

            foreach (var packet in GameCharacterPackets.BuildInventory(new[] { added }))
            {
                recipient.Connection.Send(packet);
            }

            return ResultCode.Success;
        }
        catch (Exception exception)
        {
            item.TakenBy = 0;
            _logger.Error(exception, "Could not take item {itemHandle} for {clientTag}", item.Handle,
                client.ClientTag);
            return ResultCode.DBError;
        }
    }

    private ResultCode TakeGold(GameClient picker, GroundItem item, uint takerHandle)
    {
        if (Interlocked.CompareExchange(ref item.TakenBy, 1, 0) != 0) return ResultCode.NotExist;
        var credited = false;
        try
        {
            var info = picker.ConnectionInfo;
            var members = (_parties?.RewardMembers(picker, info.X, info.Y, info.Layer) ?? new[] { picker })
                .Where(c => c.ConnectionInfo.Layer == info.Layer
                    && CombatRange.Distance(info.X, info.Y, c.ConnectionInfo.X, c.ConnectionInfo.Y) <= 400)
                .Distinct().OrderBy(c => c.ConnectionInfo.CharacterHandle).ToArray();
            if (members.Length == 0) members = new[] { picker };
            var locked = 0;
            try
            {
                foreach (var member in members) { Monitor.Enter(member.ConnectionInfo.GoldLock); locked++; }
                for (var i = 0; i < members.Length; i++)
                    if (!GoldRules.Fits(members[i].ConnectionInfo.CharacterGold,
                        CombatRewards.Share(item.Count, members.Length, i), GoldRules.MaxCarried))
                    { item.TakenBy = 0; return ResultCode.TooMuchMoney; }
                for (var i = 0; i < members.Length; i++)
                    members[i].ConnectionInfo.AddGold(CombatRewards.Share(item.Count, members.Length, i));
                credited = true;
            }
            finally
            {
                while (locked > 0) Monitor.Exit(members[--locked].ConnectionInfo.GoldLock);
            }
            // Consume before notifying: a network failure must never make credited gold available again.
            _items.TryRemove(item.Handle, out _);
            picker.Connection.Send(GameSpawnPackets.BuildTakeItemResult(item.Handle, takerHandle));
            Remove(item);
            foreach (var member in members)
                member.Connection.Send(GameCharacterPackets.BuildGoldUpdate(
                    member.ConnectionInfo.CharacterGold, member.ConnectionInfo.CharacterChaos));
            return ResultCode.Success;
        }
        catch (Exception exception)
        {
            if (!credited) item.TakenBy = 0;
            else _items.TryRemove(item.Handle, out _);
            _logger.Error(exception, "Could not complete gold pickup notification for {Handle}", item.Handle);
            return credited ? ResultCode.Success : ResultCode.DBError;
        }
    }

    /// <summary>
    /// Units a drop may take off <paramref name="item"/>, or <c>0</c> to refuse: a worn item and a bound
    /// summon card are refused, a request larger than the stack is clamped.
    /// </summary>
    private long ResolveDropCount(ItemEntity item, int requested)
    {
        if (GroundItemDropRules.IsEquipped(item.WearInfo))
        {
            return 0;
        }

        ItemGroup? group = _itemGroups.TryGetGroup(item.ItemResourceId, out var knownGroup) ? knownGroup : null;
        if (GroundItemDropRules.IsBoundSummonCard(item.Flag, group))
        {
            return 0;
        }

        return GroundItemDropRules.ResolveDropCount(requested, item.Amount);
    }

    private static void SendDropResult(GameClient client, uint itemHandle, bool isAccepted)
    {
        client.Connection.Send(GameCharacterPackets.BuildDropResult(itemHandle, isAccepted));
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
        if (info.Layer != item.Layer) return false;
        var dx = info.X - item.X;
        var dy = info.Y - item.Y;
        return dx * dx + dy * dy <= PickupRange * PickupRange;
    }

    private void Remove(GroundItem item)
    {
        _items.TryRemove(item.Handle, out _);
        HideFrom(item.Owner, item.Handle);
        foreach (var peer in _players.Registry.Clients)
        {
            if (!ReferenceEquals(peer, item.Owner)) HideFrom(peer, item.Handle);
        }
    }

    public void Sync(GameClient client)
    {
        var info = client.ConnectionInfo;
        foreach (var item in _items.Values)
        {
            if (Volatile.Read(ref item.TakenBy) != 0 || !InView(info, item)) continue;
            ShowTo(client, item);
        }

        // What left the view is judged per item under the lock, never against a snapshot taken before it:
        // a drop shown by another thread in between (ShowToNearby) would otherwise be sent a LEAVE at once.
        // An item being taken stays: Remove hides it once the take succeeds, after the 210 that animates it.
        lock (info.GroundItemVisibilityLock)
        {
            info.SpawnedGroundItems.RemoveWhere(handle =>
            {
                if (_items.TryGetValue(handle, out var item) && InView(info, item))
                {
                    return false;
                }

                client.Connection.Send(GameSpawnPackets.BuildLeave(handle));
                return true;
            });
        }
    }

    public void LeaveWorld(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (info.GroundItemVisibilityLock)
        {
            foreach (var handle in info.SpawnedGroundItems)
                client.Connection.Send(GameSpawnPackets.BuildLeave(handle));
            info.SpawnedGroundItems.Clear();
        }
    }

    private void ShowToNearby(GroundItem item, GameClient owner)
    {
        foreach (var peer in _players.Registry.Clients)
        {
            if (!ReferenceEquals(peer, owner) && InView(peer.ConnectionInfo, item)) ShowTo(peer, item);
        }
    }

    private static bool InView(ConnectionInfo info, GroundItem item)
    {
        if (info.CharacterHandle == 0 || info.Layer != item.Layer) return false;
        var dx = info.X - item.X;
        var dy = info.Y - item.Y;
        return dx * dx + dy * dy <= WorldVisibility.ViewRange * WorldVisibility.ViewRange;
    }

    private static void ShowTo(GameClient client, GroundItem item)
    {
        var info = client.ConnectionInfo;
        lock (info.GroundItemVisibilityLock)
        {
            if (!info.SpawnedGroundItems.Add(item.Handle)) return;
            var dropTime = unchecked(ServerClock.Now + info.ClientClockOffset);
            client.Connection.Send(GameSpawnPackets.BuildEnterItem(item.Handle, item.X, item.Y, item.Z,
                item.Layer, item.ItemCode, item.Count, dropTime, item.OwnerHandle,
                item.MonsterDrop ? (uint)(item.PartyId ?? 0) : 0));
        }
    }

    private static void HideFrom(GameClient client, uint handle)
    {
        var info = client.ConnectionInfo;
        lock (info.GroundItemVisibilityLock)
        {
            if (info.SpawnedGroundItems.Remove(handle)) client.Connection.Send(GameSpawnPackets.BuildLeave(handle));
        }
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
