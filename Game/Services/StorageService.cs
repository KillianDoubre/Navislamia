using System;
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
    /// One context serves every session, so the moves of two players are serialised the way
    /// <c>CharacterService</c> serialises its own item operations.
    /// </summary>
    private readonly SemaphoreSlim _databaseGate = new(1, 1);

    public StorageService(IStorageRepository repository)
    {
        _repository = repository;
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
            var items = await RunExclusiveAsync(() => _repository.GetStorageItemsAsync(info.CharacterName));

            // NGemity marks the session as using the storage before it sends anything
            // (Player::openStorage, Player.cpp:2961-2965) and sends the item list ahead of the frame
            // (Player::OpenStorage, Player.cpp:2918-2937).
            info.StorageSecurityCheck = true;

            client.Connection.Send(GameStoragePackets.BuildOpenStorage());

            foreach (var packet in GameCharacterPackets.BuildInventory(items))
            {
                client.Connection.Send(packet);
            }

            // No stored gold exists yet: the value has no column of its own in this repository and the
            // mode-2/3 path that would create one is not open (§7.5). The property is still sent, the way
            // NGemity sends it after every gold change, so the client does not keep a stale reading.
            client.Connection.Send(
                GameStatPackets.BuildProperty(info.CharacterHandle, StorageGoldProperty, 0));

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
            // The gold modes are wired but not open: NGemity keeps the stored gold in a dummy item row of
            // code 0 (CharacterDatabase.cpp:97) and this repository has no column for it, so the table and
            // the scope of the gold are an open decision (§7.5). Refusing keeps the character's own gold
            // intact instead of taking gold the server could not give back.
            client.SendResult(RequestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        try
        {
            var move = await RunExclusiveAsync(() => _repository.MoveAsync(info.CharacterName, request.ItemHandle,
                StorageRules.MovesToStorage(request.Mode), request.Count));

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

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation)
    {
        await _databaseGate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _databaseGate.Release();
        }
    }
}
