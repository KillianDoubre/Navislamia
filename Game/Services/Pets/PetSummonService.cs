using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
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
}

/// <summary>
/// Calling a familier (pet) with its cage, and naming it. The 7.3 cage item carries the effect
/// <c>SummonPet</c> (90) with no pet in its values: the pet is the client table's row whose <c>cage_id</c> is
/// the item (<see cref="IPetCatalog"/>). One pet at a time, toggled by its cage
/// (<see cref="PetSummonRules.Decide"/>). Its name is stored per cage (<c>Pets</c>); an unnamed pet opens the
/// client's name box (353) every time it is called until the master names it (354). NGemity has no pet
/// logic, so trigger, toggle, placement and naming policy are this repository's; see
/// docs/packet-specs/socle-familier-pet.md §15-17.
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

        var info = client.ConnectionInfo;
        bool summon;
        lock (info.PetLock)
        {
            var action = PetSummonRules.Decide(info.ActivePet, itemHandle);
            if (action is PetCageAction.Dismiss or PetCageAction.Swap)
            {
                _world.Leave(info, client.ClientTag, client.Connection, info.ActivePet!.Handle);
                info.ActivePet = null;
            }

            summon = action is PetCageAction.Summon or PetCageAction.Swap;
            _logger.Debug("{clientTag} used cage {cageHandle} (pet {petId}): {action}", client.ClientTag,
                itemHandle, pet.PetId, action);
        }

        if (!summon)
        {
            return true;
        }

        // The cage handle is the item's id: its pet row carries the name, created on the first call.
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
            // Another cage may have been used while the row was read: the last call wins, one pet at a time.
            if (info.ActivePet is { } other)
            {
                _world.Leave(info, client.ClientTag, client.Connection, other.Handle);
                info.ActivePet = null;
            }

            var entry = PetSummonRules.BuildEntry(pet, itemHandle, info.X, info.Y, info.Z, info.Layer,
                isFirstEnter: true, name: record.Name);
            var handle = _world.Enter(info, client.ClientTag, client.Connection, entry);
            if (handle == 0)
            {
                return true;
            }

            info.ActivePet = new ActivePet(handle, itemHandle, entry, pet.CollectRange);
            if (!record.WasNameChanged)
            {
                OfferRenameLocked(client, info.ActivePet);
            }
        }

        return true;
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

    public async Task RenameAsync(GameClient client, uint handle, string name)
    {
        var info = client.ConnectionInfo;
        ActivePet active;
        lock (info.PetLock)
        {
            active = info.ActivePet;
            if (active is null || active.Handle != handle || !active.RenameOffered)
            {
                _logger.Warning("{clientTag} sent a pet name for handle {handle} that no 353 offered",
                    client.ClientTag, handle);
                return;
            }
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (!PetSummonRules.IsValidName(trimmed) || _bannedWords.ContainsBannedWord(trimmed))
        {
            // No reference holds a refusal frame for a pet name: the box is simply offered again.
            _logger.Debug("{clientTag} refused pet name \"{name}\"", client.ClientTag, trimmed);
            OfferRename(client);
            return;
        }

        try
        {
            if (!await _characterService.RenamePetAsync(info.CharacterName, active.CageHandle, trimmed))
            {
                _logger.Warning("{clientTag} renamed cage {cageHandle}, which has no pet row", client.ClientTag,
                    active.CageHandle);
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not rename the pet of cage {cageHandle} for {clientTag}",
                active.CageHandle, client.ClientTag);
            return;
        }

        lock (info.PetLock)
        {
            // The pet may have been put away or swapped while the row was written.
            if (!ReferenceEquals(info.ActivePet, active))
            {
                return;
            }

            // The name travels in the entry frame: the pet is brought back in place under its new name.
            var (x, y) = active.PositionAt(ServerClock.Now);
            Replace(client, active, x, y, trimmed);
        }

        _logger.Debug("{clientTag} named the pet of cage {cageHandle} \"{name}\"", client.ClientTag,
            active.CageHandle, trimmed);
    }

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
                Replace(client, active, info.DestinationX, info.DestinationY, active.Entry.Name);
            }
        }
    }

    /// <summary>Takes the pet out and brings it in again at (x, y); must hold the pet lock.</summary>
    private void Replace(GameClient client, ActivePet active, float x, float y, string name)
    {
        var info = client.ConnectionInfo;
        _world.Leave(info, client.ClientTag, client.Connection, active.Handle);

        var entry = PetSummonRules.BuildEntry(
            new PetDefinition((int)active.Entry.PetCode, 0, name), active.CageHandle,
            x, y, info.Z, info.Layer, isFirstEnter: false, name: name);
        var handle = _world.Enter(info, client.ClientTag, client.Connection, entry);
        info.ActivePet = handle == 0 ? null : new ActivePet(handle, active.CageHandle, entry, active.CollectRange);
    }

    private static void OfferRenameLocked(GameClient client, ActivePet active)
    {
        active.RenameOffered = true;
        client.Connection.Send(GamePetPackets.BuildShowSetPetName(active.Handle));
    }
}
