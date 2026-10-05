using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The character storage, commanded by the account (docs/packet-specs/211-212-storage.md §5). The window
/// is opened by an NPC trigger, not by the packet: nothing else opens it, and no packet announces the
/// opening to the player — the session only records it.
/// </summary>
public class StorageService : IStorageService
{
    private const ushort RequestId = (ushort)GamePackets.TM_CS_STORAGE;

    /// <summary>The property the client reads for the stored gold (Player.cpp:2948).</summary>
    private const string StorageGoldProperty = "storage_gold";

    private readonly ILogger _logger = Log.ForContext<StorageService>();
    private readonly IStorageRepository _repository;

    /// <summary>
    /// The gate <c>CharacterService</c> takes too, keyed by character: a storage move and an inventory
    /// operation on the same character exclude each other, and two players no longer wait on each other.
    /// </summary>
    private readonly CharacterGate _gate;

    private readonly Weight.ICarriedWeightService _weights;
    private readonly Weight.IInventoryChangeFeed _inventoryFeed;

    public StorageService(IStorageRepository repository, CharacterGate gate,
        Weight.ICarriedWeightService weights = null, Weight.IInventoryChangeFeed inventoryFeed = null)
    {
        _weights = weights;
        _inventoryFeed = inventoryFeed;
        _repository = repository;
        _gate = gate;
    }

    public async Task OpenAsync(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (string.IsNullOrEmpty(info.CharacterName))
        {
            _logger.Warning("Refused to open a storage on {clientTag}: no character in session", client.ClientTag);
            return;
        }

        try
        {
            var (items, gold) = await _gate.RunAsync(info.CharacterName, async () =>
                (await _repository.GetStorageItemsAsync(info.CharacterName),
                 await _repository.GetStorageGoldAsync(info.CharacterName)));

            // NGemity marks the session as using the storage before it sends anything
            // (Player::openStorage, Player.cpp:2961-2965) and sends the item list ahead of the frame
            // (Player::OpenStorage, Player.cpp:2918-2937).
            info.StorageSecurityCheck = true;

            client.Connection.Send(GameStoragePackets.BuildOpenStorage());

            foreach (var packet in GameCharacterPackets.BuildInventory(items))
            {
                client.Connection.Send(packet);
            }

            // The stored gold of the account (AccountStorageGolds, socle-entrepot-or.md §3).
            client.Connection.Send(
                GameStatPackets.BuildProperty(info.CharacterHandle, StorageGoldProperty, gold));

            _logger.Debug("Opened the storage of {characterName} ({itemCount} items)", info.CharacterName,
                items.Length);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not open the storage of {characterName}", info.CharacterName);
        }
    }

    public async Task HandleAsync(GameClient client, GameActionPackets.StorageRequest request)
    {
        var info = client.ConnectionInfo;
        var target = unchecked((int)request.ItemHandle);

        // WorldSession::onStorage opens on the session state, not on the mode (WorldSession.cpp:1595-1598).
        if (!info.StorageSecurityCheck)
        {
            client.SendResult(RequestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        if (!StorageRules.IsKnownMode(request.Mode))
        {
            client.SendResult(RequestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        // The close carries no handle and gets no answer (WorldSession.cpp:1670-1672).
        if (request.Mode == StorageRules.CloseMode)
        {
            info.StorageSecurityCheck = false;
            _logger.Debug("{clientTag} closed the storage of {characterName}", client.ClientTag, info.CharacterName);
            return;
        }

        // MoveInventoryToStorage starts with IsErasable (StructPlayer.cpp:3458) and onStorage ignores its false: an item a
        // creature or the pet out holds stays in the bag, silently, like a worn one.
        if (request.Mode == StorageRules.ItemToStorage && !Creatures.HeldItemRules.IsErasable(info, request.ItemHandle))
        {
            _logger.Debug("{clientTag} kept item {handle} in the bag: a creature or the pet holds it", client.ClientTag,
                request.ItemHandle);
            return;
        }

        // WorldSession.cpp:1603-1606 refuses a unit count of zero or less with NOT_ENOUGH_MONEY, for the
        // gold modes as well — NGemity's own gold branch tests the balance first, which a negative count
        // slips through and turns into an increase of the total gold.
        if (request.Count <= 0)
        {
            client.SendResult(RequestId, (ushort)ResultCode.NotEnoughMoney, target);
            return;
        }

        if (StorageRules.IsGoldMode(request.Mode))
        {
            await MoveGoldAsync(client, StorageRules.MovesToStorage(request.Mode), request.Count, target);
            return;
        }

        try
        {
            var toStorage = StorageRules.MovesToStorage(request.Mode);
            var checkedMove = await _gate.RunAsync<StorageMoveResult?>(info.CharacterName, async () =>
            {
                if (!toStorage && _weights is not null)
                {
                    var stored = (await _repository.GetStorageItemsAsync(info.CharacterName))
                        ?.FirstOrDefault(row => (uint)row.Id == request.ItemHandle);
                    var units = stored is null ? 0 : StorageRules.MoveCount(request.Count, stored.Amount);
                    if (units > 0 && !_weights.CanCarry(info, (int)stored.ItemResourceId, units))
                    {
                        return null;
                    }
                }

                return await _repository.MoveAsync(info.CharacterName, request.ItemHandle, toStorage, request.Count);
            });

            if (checkedMove is not { } move)
            {
                client.SendResult(RequestId, (ushort)ResultCode.TooHeavy, target);
                return;
            }

            _inventoryFeed?.Publish(info.CharacterName);

            switch (move.Outcome)
            {
                case StorageMoveOutcome.Moved:
                    // The stack leaves its list without changing handle: the source list drops it
                    // (Player::onRemove → SendItemDestroyMessage, Player.cpp:1242) and the destination list
                    // adds it (Player::onAdd → SendItemMessage, Player.cpp:1200).
                    client.Connection.Send(GameCharacterPackets.BuildDestroyItem(request.ItemHandle));
                    SendAddedToDestination(client, move.Destination);
                    break;

                case StorageMoveOutcome.Split:
                    // Both halves are reported: the source keeps its handle and the new count
                    // (Player::onChangeCount → SendItemCountMessage, Player.cpp:1247).
                    client.Connection.Send(GameCharacterPackets.BuildUpdateItemCount(request.ItemHandle, move.Remaining));
                    SendAddedToDestination(client, move.Destination);
                    break;

                case StorageMoveOutcome.UnknownHandle:
                    client.SendResult(RequestId, (ushort)ResultCode.NotExist, target);
                    break;

                case StorageMoveOutcome.StorageFull:
                    // The official onStorage answers 11 once the storage holds its capacity (§1).
                    client.SendResult(RequestId, (ushort)ResultCode.TooHeavy, target);
                    break;

                case StorageMoveOutcome.AccessDenied:
                    client.SendResult(RequestId, (ushort)ResultCode.AccessDenied, target);
                    break;

                case StorageMoveOutcome.UnknownCharacter:
                    // A request from a session whose character row is gone is not actable, not a move of
                    // someone else's item.
                    client.SendResult(RequestId, (ushort)ResultCode.NotActable, target);
                    break;

                default:
                    // The item already sat on the requested side: NGemity answers nothing at all.
                    _logger.Debug("Ignored storage mode {mode} for handle {handle} of {characterName}",
                        request.Mode, request.ItemHandle, info.CharacterName);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process storage mode {mode} for {clientTag}", request.Mode,
                client.ClientTag);
            client.SendResult(RequestId, (ushort)ResultCode.DBError, target);
        }
    }

    /// <summary>
    /// Modes 2 and 3 of the official <c>onStorage</c>: the stored gold belongs to the account
    /// (<c>smp_update_storage_gold</c>), the carried gold to the character, and both balances are written in
    /// one save. A refusal is <c>NotEnoughMoney</c> (10) or <c>TooMuchMoney</c> (53); a success answers with
    /// the gold update (1001) and the <c>storage_gold</c> property (socle-entrepot-or.md §2).
    /// </summary>
    private async Task MoveGoldAsync(GameClient client, bool toStorage, long amount, int target)
    {
        var info = client.ConnectionInfo;
        try
        {
            var outcome = await _gate.RunAsync<(ResultCode Verdict, long Stored)>(info.CharacterName, async () =>
            {
                var stored = await _repository.GetStorageGoldAsync(info.CharacterName);
                var verdict = StorageRules.JudgeGold(toStorage, amount, info.CharacterGold, stored);
                if (verdict != ResultCode.Success)
                {
                    return (verdict, stored);
                }

                // The session balance moves under its own lock: loot or a trade may have changed it since
                // the judgement, and TryDebit/TryCredit re-check it where it is written.
                var moved = toStorage
                    ? info.TryDebitGold(amount)
                    : info.TryCreditGold(amount, GoldRules.MaxCarried);
                if (!moved)
                {
                    return (toStorage ? ResultCode.NotEnoughMoney : ResultCode.TooMuchMoney, stored);
                }

                var newStored = toStorage ? stored + amount : stored - amount;
                try
                {
                    await _repository.SaveGoldAsync(info.CharacterName, info.CharacterGold, newStored);
                }
                catch
                {
                    info.AddGold(toStorage ? amount : -amount);
                    throw;
                }

                return (ResultCode.Success, newStored);
            });

            if (outcome.Verdict != ResultCode.Success)
            {
                client.SendResult(RequestId, (ushort)outcome.Verdict, target);
                return;
            }

            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            client.Connection.Send(
                GameStatPackets.BuildProperty(info.CharacterHandle, StorageGoldProperty, outcome.Stored));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not move {amount} gold for {clientTag}", amount, client.ClientTag);
            client.SendResult(RequestId, (ushort)ResultCode.DBError, target);
        }
    }

    /// <summary>
    /// The moved row belongs to one list at a time and the frame carries no side, exactly like
    /// <c>Messages::SendItemMessage</c>: a handle sitting in the storage is what the storage window draws.
    /// </summary>
    private static void SendAddedToDestination(GameClient client, Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity item)
    {
        foreach (var packet in GameCharacterPackets.BuildInventory(new[] { item }))
        {
            client.Connection.Send(packet);
        }
    }
}
