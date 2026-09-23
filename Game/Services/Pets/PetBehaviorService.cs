using System;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Pets;

/// <summary>
/// What a pet does once it is out, every <see cref="TickIntervalMs"/>: it goes for its master's loot within
/// its collect range, and otherwise trails its master. It holds <see cref="NetworkService"/> only for the
/// client list, like <c>MonsterAiService</c>; nothing injects it, so there is no DI cycle.
/// <para>
/// Movement is <c>TS_SC_MOVE</c> with the start tick and speed the server interpolates with
/// (<see cref="ActivePet.MoveTo"/>, the monsters' math), so the server's notion of where the pet is matches
/// the animation. A pet farther than the client view from its master is brought back beside it
/// (<see cref="IPetSummonService.Recall"/>) rather than walked there. See socle-familier-pet.md §17.
/// </para>
/// </summary>
public class PetBehaviorService
{
    public const int TickIntervalMs = 250;

    private readonly ILogger _logger = Log.ForContext<PetBehaviorService>();
    private readonly NetworkService _networkService;
    private readonly PetBehavior _behavior;

    public PetBehaviorService(NetworkService networkService, IGroundItemService groundItems,
        IPetSummonService petSummon)
    {
        _networkService = networkService;
        _behavior = new PetBehavior(groundItems, petSummon);
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        while (await timer.WaitForNextTickAsync())
        {
            foreach (var client in _networkService.AuthorizedGameClients.Values)
            {
                try
                {
                    _behavior.Step(client, ServerClock.Now);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "The pet tick failed for {clientTag}", client.ClientTag);
                }
            }
        }
    }
}

/// <summary>
/// One decision per tick for one client's pet — collect, else follow, else stay — without the timer, so the
/// tests drive it directly.
/// </summary>
public class PetBehavior
{
    private readonly IGroundItemService _groundItems;
    private readonly IPetSummonService _petSummon;

    public PetBehavior(IGroundItemService groundItems, IPetSummonService petSummon)
    {
        _groundItems = groundItems;
        _petSummon = petSummon;
    }

    public void Step(GameClient client, uint now)
    {
        var info = client.ConnectionInfo;
        uint pickup = 0;
        uint petHandle = 0;

        lock (info.PetLock)
        {
            var pet = info.ActivePet;
            if (pet is null)
            {
                return;
            }

            var (petX, petY) = pet.PositionAt(now);
            var (masterX, masterY) = info.PositionAt(now);
            if (PetSummonRules.IsTooFarToWalk(petX, petY, masterX, masterY))
            {
                _petSummon.Recall(client);
                return;
            }

            if (pet.PickupTarget != 0)
            {
                if (!pet.HasArrived(now))
                {
                    return;
                }

                pickup = pet.PickupTarget;
                petHandle = pet.Handle;
                pet.PickupTarget = 0;
            }
            else if (pet.CollectRange > 0 &&
                     _groundItems.TryFindNearest(client, petX, petY, info.Layer, pet.CollectRange, out var spot))
            {
                pet.PickupTarget = spot.Handle;
                Move(client, pet, spot.X, spot.Y, now);
                return;
            }
            else if (PetSummonRules.FollowTarget(petX, petY, masterX, masterY,
                         PetSummonRules.Heading(masterX, masterY, info.DestinationX, info.DestinationY).X,
                         PetSummonRules.Heading(masterX, masterY, info.DestinationX, info.DestinationY).Y)
                     is { } follow &&
                     (pet.HasArrived(now) ||
                      PetSummonRules.Distance(pet.DestX, pet.DestY, follow.X, follow.Y) > PetSummonDefaults.FollowDistance))
            {
                // A new move only when the pet has stopped or its master's destination drifted: a fresh
                // TS_SC_MOVE every tick would make it stutter, exactly like the monsters' chase.
                Move(client, pet, follow.X, follow.Y, now);
                return;
            }
        }

        if (pickup != 0)
        {
            // Outside the lock: the take waits on the database. The item is claimed there, so the master and
            // the pet cannot both take it.
            _ = _groundItems.TakeForPetAsync(client, pickup, petHandle);
        }
    }

    private static void Move(GameClient client, ActivePet pet, float x, float y, uint now)
    {
        var info = client.ConnectionInfo;
        pet.MoveTo(x, y, PetSummonDefaults.MoveSpeed, now);
        client.Connection.Send(GameMovePackets.BuildMove(pet.Handle, unchecked(now + info.ClientClockOffset),
            info.Layer, PetSummonDefaults.MoveSpeed, x, y));
    }
}
