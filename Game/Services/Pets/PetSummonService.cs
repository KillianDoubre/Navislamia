using Navislamia.Game.Network.Clients;
using Serilog;

namespace Navislamia.Game.Services.Pets;

public interface IPetSummonService
{
    /// <summary>
    /// Called on a successful use of an item: when it is a known cage, calls its pet, puts it away or swaps
    /// it for the one already out. Returns false, doing nothing, for any other item.
    /// </summary>
    bool TryUseCage(GameClient client, long itemResourceId, uint itemHandle);

    /// <summary>After a warp: the pet out is taken out of the old place and brought in at the master's new one.</summary>
    void FollowWarp(GameClient client);
}

/// <summary>
/// Calling a familier (pet) with its cage, the first caller of <see cref="PetWorldService"/>. The 7.3 cage
/// item carries the effect <c>SummonPet</c> (90) with no pet in its values: the pet is the client table's
/// row whose <c>cage_id</c> is the item (<see cref="IPetCatalog"/>). One pet at a time, toggled by its cage
/// (<see cref="PetSummonRules.Decide"/>). NGemity has no pet logic, so the trigger, the toggle and the
/// placement at the master's feet are this repository's; see docs/packet-specs/socle-familier-pet.md §15.
/// </summary>
/// <remarks>
/// The pet does not follow its master while walking — no pet movement exists — and it is seen by its master
/// only, like every object here (no player-to-player visibility). Nothing is persisted: the pet is put away
/// by leaving the world, and <c>PetEntity</c> is still read and written by no one.
/// </remarks>
public class PetSummonService : IPetSummonService
{
    private readonly ILogger _logger = Log.ForContext<PetSummonService>();
    private readonly IPetCatalog _catalog;
    private readonly PetWorldService _world;

    public PetSummonService(IPetCatalog catalog, PetWorldService world)
    {
        _catalog = catalog;
        _world = world;
    }

    public bool TryUseCage(GameClient client, long itemResourceId, uint itemHandle)
    {
        if (!_catalog.TryGetByCage(itemResourceId, out var pet))
        {
            return false;
        }

        var info = client.ConnectionInfo;
        lock (info.PetLock)
        {
            var active = info.ActivePet;
            var action = PetSummonRules.Decide(active, itemHandle);

            if (action is PetCageAction.Dismiss or PetCageAction.Swap)
            {
                _world.Leave(info, client.ClientTag, client.Connection, active!.Handle);
                info.ActivePet = null;
            }

            if (action is PetCageAction.Summon or PetCageAction.Swap)
            {
                var entry = PetSummonRules.BuildEntry(pet, itemHandle, info.X, info.Y, info.Z, info.Layer,
                    isFirstEnter: true);
                var handle = _world.Enter(info, client.ClientTag, client.Connection, entry);
                info.ActivePet = handle == 0 ? null : new ActivePet(handle, itemHandle, entry);
            }

            _logger.Debug("{clientTag} used cage {cageHandle} (pet {petId}): {action}", client.ClientTag,
                itemHandle, pet.PetId, action);
        }

        return true;
    }

    public void FollowWarp(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (info.PetLock)
        {
            var active = info.ActivePet;
            if (active is null)
            {
                return;
            }

            _world.Leave(info, client.ClientTag, client.Connection, active.Handle);

            var entry = PetSummonRules.BuildEntry(
                new PetDefinition((int)active.Entry.PetCode, 0, active.Entry.Name), active.CageHandle,
                info.X, info.Y, info.Z, info.Layer, isFirstEnter: false);
            var handle = _world.Enter(info, client.ClientTag, client.Connection, entry);
            info.ActivePet = handle == 0 ? null : new ActivePet(handle, active.CageHandle, entry);
        }
    }
}
