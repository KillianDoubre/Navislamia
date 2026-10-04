using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Pets;

public interface IPetSummonService
{
    /// <summary>
    /// Called on a successful use of an item: when it is a known cage, calls its pet, puts it away or swaps
    /// it for the one already out. Returns false, doing nothing, for any other item.
    /// </summary>
    Task<bool> TryUseCageAsync(GameClient client, long itemResourceId, uint itemHandle);

    /// <summary>Whether a pet is out, i.e. whether a rename item has something to rename.</summary>
    bool HasPetOut(GameClient client);

    /// <summary>Opens the client's name box on the pet out (<c>TM_SC_SHOW_SET_PET_NAME</c>, 353).</summary>
    void OfferRename(GameClient client);

    /// <summary><c>TM_CS_SET_PET_NAME</c> (354): renames the pet out when the handle is the one 353 carried.</summary>
    Task RenameAsync(GameClient client, uint handle, string name);

    /// <summary><c>TM_CS_SET_PET_FILTER</c> (355): keeps the raw value; its meaning is not established.</summary>
    void SetPickupFilter(GameClient client, uint handle, uint filter);

    /// <summary>After a warp: the pet out is taken out of the old place and brought in at the master's new one.</summary>
    void FollowWarp(GameClient client);

    /// <summary>Takes the pet out and brings it back beside its master, keeping its state; under the pet lock.</summary>
    void Recall(GameClient client);

    /// <summary>
    /// <c>StructPlayer::Login</c>: the 351 of every pet the bag's cages hold, each under the handle its calls will use.
    /// </summary>
    Task SendPetInfoAsync(GameClient client, IEnumerable<Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity> items) =>
        Task.CompletedTask;
}

/// <summary>
/// Calling a familier (pet) with its cage, and naming it. The 7.3 cage item carries the effect
/// <c>SummonPet</c> (90) with no pet in its values: the pet is the client table's row whose <c>cage_id</c> is
/// the item (<see cref="IPetCatalog"/>). One pet at a time, toggled by its cage
/// (<see cref="PetSummonRules.Decide"/>). Its name is stored per cage (<c>Pets</c>); an unnamed pet opens the
/// client's name box (353) instead of coming out until the master names it (354). The frames follow the official
/// server: every pet is registered once (351) under the handle its calls use, a call only puts it in the world;
/// see docs/packet-specs/socle-familier-pet.md §15-18.
/// </summary>
public class PetSummonService : IPetSummonService
{
    private readonly ILogger _logger = Log.ForContext<PetSummonService>();
    private readonly IPetCatalog _catalog;
    private readonly PetWorldService _world;
    private readonly ICharacterService _characterService;
    private readonly IBannedWordsRepository _bannedWords;

    public PetSummonService(IPetCatalog catalog, PetWorldService world, ICharacterService characterService,
        IBannedWordsRepository bannedWords)
    {
        _catalog = catalog;
        _world = world;
        _characterService = characterService;
        _bannedWords = bannedWords;
    }

    public async Task<bool> TryUseCageAsync(GameClient client, long itemResourceId, uint itemHandle)
    {
        if (!_catalog.TryGetByCage(itemResourceId, out var pet))
        {
            return false;
        }

        // The cage handle is the item's id: its pet row carries the name, created with the pet.
        var info = client.ConnectionInfo;
        PetRecord record;
        try
        {
            record = await _characterService.GetOrCreatePetAsync(info.CharacterName, info.CharacterHandle,
                info.AccountId, itemHandle, pet.PetId, pet.Name);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read the pet of cage {cageHandle} for {clientTag}", itemHandle,
                client.ClientTag);
            record = new PetRecord(pet.Name, true);
        }

        lock (info.PetLock)
        {
            // A cage got since the world entry is registered now (351), like StructPlayer::AddPet.
            if (!info.PetHandles.TryGetValue(itemHandle, out var petHandle))
            {
                petHandle = WorldObjectHandle.Next();
                info.PetHandles[itemHandle] = petHandle;
                client.Connection.Send(PetWorldService.BuildInfo(itemHandle, petHandle, record.Name, pet.PetId,
                    PetSummonDefaults.Unknown));
            }

            // ITEM_EFFECT_INSTANT SUMMON_PET: a rare pet whose name was never set is named first (353), not called.
            if (!record.WasNameChanged)
            {
                info.PetNameOffers[petHandle] = itemHandle;
                client.Connection.Send(GamePetPackets.BuildShowSetPetName(petHandle));
                _logger.Debug("{clientTag} was offered a name for pet {handle} of cage {cageHandle} (353)",
                    client.ClientTag, petHandle, itemHandle);
                return true;
            }

            var action = PetSummonRules.Decide(info.ActivePet, itemHandle);
            _logger.Debug("{clientTag} used cage {cageHandle} (pet {petId}): {action}", client.ClientTag,
                itemHandle, pet.PetId, action);
            if (action is PetCageAction.Dismiss or PetCageAction.Swap)
            {
                _world.Leave(info, client.ClientTag, client.Connection, info.ActivePet!.Handle, client);
                info.ActivePet = null;
            }

            if (action is not (PetCageAction.Summon or PetCageAction.Swap))
            {
                return true;
            }

            // SummonPet: the registered pet enters the world under its handle; its 351 already went.
            var entry = PetSummonRules.BuildEntry(pet, itemHandle, info.X, info.Y, info.Z, info.Layer,
                isFirstEnter: true, name: record.Name);
            var handle = _world.Enter(info, client.ClientTag, client.Connection, entry, client, handle: petHandle,
                sendInfo: false);
            if (handle != 0)
            {
                info.ActivePet = new ActivePet(handle, itemHandle, entry, pet.CollectRange);
            }
        }

        return true;
    }

    public async Task SendPetInfoAsync(GameClient client, IEnumerable<Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity> items)
    {
        var info = client.ConnectionInfo;
        foreach (var cage in items ?? Array.Empty<Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity>())
        {
            if (cage.Amount <= 0 || !_catalog.TryGetByCage(cage.ItemResourceId, out var pet))
            {
                continue;
            }

            var cageHandle = (uint)cage.Id;
            PetRecord record;
            try
            {
                record = await _characterService.GetOrCreatePetAsync(info.CharacterName, info.CharacterHandle,
                    info.AccountId, cageHandle, pet.PetId, pet.Name);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not read the pet of cage {cageHandle} for {clientTag}", cageHandle,
                    client.ClientTag);
                record = new PetRecord(pet.Name, true);
            }

            lock (info.PetLock)
            {
                if (info.PetHandles.ContainsKey(cageHandle))
                {
                    continue;
                }

                var handle = WorldObjectHandle.Next();
                info.PetHandles[cageHandle] = handle;
                client.Connection.Send(PetWorldService.BuildInfo(cageHandle, handle, record.Name, pet.PetId,
                    PetSummonDefaults.Unknown));
            }
        }
    }

    public bool HasPetOut(GameClient client)
    {
        lock (client.ConnectionInfo.PetLock)
        {
            return client.ConnectionInfo.ActivePet is not null;
        }
    }

    public void OfferRename(GameClient client)
    {
        lock (client.ConnectionInfo.PetLock)
        {
            if (client.ConnectionInfo.ActivePet is { } active)
            {
                OfferRenameLocked(client, active);
            }
        }
    }

    /// <summary>
    /// <c>onSetPetName</c> and <c>StructPet::ChangeName</c>: only a pet a 353 offered, a name of 4 to 18 letters and
    /// digits without banned words; the refusals answer the official lines and result, the change goes out as
    /// <c>TS_SC_CHANGE_NAME</c> (30) to the master and, for a pet out, to the players who see it.
    /// </summary>
    public async Task RenameAsync(GameClient client, uint handle, string name)
    {
        var info = client.ConnectionInfo;
        uint cageHandle;
        lock (info.PetLock)
        {
            if (!info.PetNameOffers.TryGetValue(handle, out cageHandle))
            {
                _logger.Warning("{clientTag} sent a pet name for handle {handle} that no 353 offered",
                    client.ClientTag, handle);
                return;
            }
        }

        var trimmed = name?.Trim() ?? string.Empty;
        var refusal = trimmed.Length < 4 ? ResultCode.LimitMin
            : !PetSummonRules.IsValidName(trimmed) || _bannedWords.ContainsBannedWord(trimmed) ? ResultCode.AccessDenied
            : ResultCode.Success;
        if (refusal != ResultCode.Success)
        {
            client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Notice,
                refusal == ResultCode.LimitMin ? "@1105" : "@1106"));
            client.SendResult(SetPetNameRequestId, (ushort)refusal, unchecked((int)handle));
            return;
        }

        try
        {
            if (!await _characterService.RenamePetAsync(info.CharacterName, cageHandle, trimmed))
            {
                _logger.Warning("{clientTag} renamed cage {cageHandle}, which has no pet row", client.ClientTag, cageHandle);
                client.SendResult(SetPetNameRequestId, (ushort)ResultCode.NotExist, unchecked((int)handle));
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not rename the pet of cage {cageHandle} for {clientTag}", cageHandle,
                client.ClientTag);
            client.SendResult(SetPetNameRequestId, (ushort)ResultCode.DBError, unchecked((int)handle));
            return;
        }

        var change = GamePetPackets.BuildChangeName(handle, trimmed);
        lock (info.PetLock)
        {
            info.PetNameOffers.Remove(handle);
            client.Connection.Send(change);
            if (info.ActivePet is { } active && active.Handle == handle)
            {
                active.Entry.Name = trimmed;
                _world.Broadcast(client, change);
            }
        }

        client.SendResult(SetPetNameRequestId, (ushort)ResultCode.Success, unchecked((int)handle));
    }

    private const ushort SetPetNameRequestId = (ushort)GamePackets.TM_CS_SET_PET_NAME;

    public void SetPickupFilter(GameClient client, uint handle, uint filter)
    {
        client.ConnectionInfo.PetPickupFilter = filter;
        _logger.Debug("{clientTag} set the pet pickup filter to {filter} (handle {handle}); not applied",
            client.ClientTag, filter, handle);
    }

    public void FollowWarp(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (info.PetLock)
        {
            if (info.ActivePet is { } active)
            {
                Replace(client, active, info.X, info.Y, active.Entry.Name);
            }
        }
    }

    public void Recall(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (info.PetLock)
        {
            if (info.ActivePet is { } active)
            {
                // Beside where its master is, not where it is going: the destination is ahead of it.
                var (x, y) = info.PositionAt(ServerClock.Now);
                Replace(client, active, x, y, active.Entry.Name);
            }
        }
    }

    /// <summary>Takes the pet out and brings it in again at (x, y); must hold the pet lock.</summary>
    private void Replace(GameClient client, ActivePet active, float x, float y, string name)
    {
        var info = client.ConnectionInfo;
        _world.Leave(info, client.ClientTag, client.Connection, active.Handle, client);

        var entry = PetSummonRules.BuildEntry(
            new PetDefinition((int)active.Entry.PetCode, 0, name), active.CageHandle,
            x, y, info.Z, info.Layer, isFirstEnter: false, name: name);
        // The same pet, already registered: back in the world under its handle, no new 351.
        var handle = _world.Enter(info, client.ClientTag, client.Connection, entry, client, handle: active.Handle,
            sendInfo: false);
        info.ActivePet = handle == 0 ? null : new ActivePet(handle, active.CageHandle, entry, active.CollectRange);
    }

    private void OfferRenameLocked(GameClient client, ActivePet active)
    {
        active.RenameOffered = true;
        client.ConnectionInfo.PetNameOffers[active.Handle] = active.CageHandle;
        client.Connection.Send(GamePetPackets.BuildShowSetPetName(active.Handle));
        _logger.Debug("{clientTag} was offered a name for pet {handle} (353)", client.ClientTag, active.Handle);
    }
}
