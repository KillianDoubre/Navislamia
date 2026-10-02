using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services.Weight;

/// <summary>
/// Announces that a character's items changed. <see cref="CharacterService"/> publishes after every
/// operation that can change what is carried, and the storage after each move, so the carried weight
/// follows the bag without every service having to maintain it.
/// </summary>
public interface IInventoryChangeFeed
{
    event Action<string> Changed;

    void Publish(string characterName);
}

public sealed class InventoryChangeFeed : IInventoryChangeFeed
{
    public event Action<string> Changed;

    public void Publish(string characterName)
    {
        if (!string.IsNullOrEmpty(characterName))
        {
            Changed?.Invoke(characterName);
        }
    }
}

/// <summary>Each item resource's <c>weight</c>, and a bag's capacity (its <c>CarryWeight</c> effects).</summary>
public interface IItemWeightCatalog
{
    float WeightOf(int itemResourceId);

    float BagCapacity(int itemResourceId);
}

public sealed class ItemWeightCatalog : IItemWeightCatalog
{
    private readonly FrozenDictionary<int, float> _weights;
    private readonly IItemStatCatalog _effects;

    public ItemWeightCatalog(IItemResourceRepository repository, IItemStatCatalog effects)
    {
        _effects = effects;
        _weights = repository.GetWeightFields()
            .Where(field => field.Weight != 0)
            .ToFrozenDictionary(field => field.Id, field => (float)field.Weight);
        Log.ForContext<ItemWeightCatalog>().Debug("Loaded the weight of {count} item resources", _weights.Count);
    }

    public float WeightOf(int itemResourceId) => _weights.GetValueOrDefault(itemResourceId);

    public float BagCapacity(int itemResourceId) => _effects?.GetEffects(itemResourceId)
        .Where(effect => effect.Target == StatTarget.MaxWeight && !effect.IsPercent)
        .Sum(effect => effect.Value) ?? 0f;
}

public interface ICarriedWeightService
{
    /// <summary>At world entry, from the items already loaded.</summary>
    void Seed(ConnectionInfo info, IEnumerable<ItemEntity> items);

    float MaxWeight(ConnectionInfo info);

    float WeightOf(int itemResourceId, long count);

    bool CanCarry(ConnectionInfo info, float added);

    bool CanCarry(ConnectionInfo info, int itemResourceId, long count);

    bool CanTakeOffBag(ConnectionInfo info, int bagResourceId);

    /// <summary>The speed a move of this character goes at, its load applied to <paramref name="speed"/>.</summary>
    byte MoveSpeed(ConnectionInfo info, byte speed);

    /// <summary>The character's <c>TS_SC_MOVE</c> speed: its stat move speed, its load, divided by 7.</summary>
    byte RealMoveSpeed(ConnectionInfo info);

    Task RefreshAsync(GameClient client);
}

/// <summary>
/// Keeps <see cref="ConnectionInfo.CarriedWeight"/> in step with the bag: seeded at world entry, re-read
/// from the database after every announced change (<see cref="IInventoryChangeFeed"/>), one refresh at a
/// time per session with a rerun when a change arrives during one.
/// </summary>
public sealed class CarriedWeightService : ICarriedWeightService
{
    private readonly ILogger _logger = Log.ForContext<CarriedWeightService>();
    private readonly IItemWeightCatalog _catalog;
    private readonly IStatService _stats;
    private readonly ICharacterService _characters;
    private readonly IPlayerVisibilityService _players;

    public CarriedWeightService(IItemWeightCatalog catalog, IStatService stats, ICharacterService characters,
        IPlayerVisibilityService players, IInventoryChangeFeed feed)
    {
        _catalog = catalog;
        _stats = stats;
        _characters = characters;
        _players = players;
        if (feed is not null)
        {
            feed.Changed += OnChanged;
        }
    }

    public void Seed(ConnectionInfo info, IEnumerable<ItemEntity> items)
    {
        info.CarriedWeight = WeightRules.Carried(items ?? Array.Empty<ItemEntity>(), _catalog.WeightOf);
    }

    /// <summary>The stat block's maximum; unknown (no stats) means no limit rather than none at all.</summary>
    public float MaxWeight(ConnectionInfo info)
    {
        var total = _stats is null ? null : _stats.Compute(info).Total;
        return total is null ? float.PositiveInfinity : total.MaxWeight;
    }

    public float WeightOf(int itemResourceId, long count) => _catalog.WeightOf(itemResourceId) * count;

    public bool CanCarry(ConnectionInfo info, float added) =>
        WeightRules.Fits(info.CarriedWeight, MaxWeight(info), added);

    public bool CanCarry(ConnectionInfo info, int itemResourceId, long count) =>
        CanCarry(info, WeightOf(itemResourceId, count));

    public bool CanTakeOffBag(ConnectionInfo info, int bagResourceId) =>
        WeightRules.CanTakeOffBag(info.CarriedWeight, MaxWeight(info), _catalog.BagCapacity(bagResourceId));

    public byte MoveSpeed(ConnectionInfo info, byte speed) =>
        WeightRules.MoveSpeed(speed, info.CarriedWeight, MaxWeight(info));

    public byte RealMoveSpeed(ConnectionInfo info)
    {
        var total = _stats?.Compute(info).Total;
        if (total is null)
        {
            return ConnectionInfo.EchoedMoveSpeed;
        }

        return WeightRules.RealMoveSpeed(total.MoveSpeed, info.CarriedWeight, total.MaxWeight);
    }

    public async Task RefreshAsync(GameClient client)
    {
        var info = client.ConnectionInfo;

        // 0 idle, 1 running, 2 running and asked again: a change during a read reruns it once more.
        if (Interlocked.Exchange(ref info.WeightRefreshState, 2) != 0)
        {
            return;
        }

        try
        {
            do
            {
                Volatile.Write(ref info.WeightRefreshState, 1);
                var name = info.CharacterName;
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                var items = await _characters.GetCarriedItemsAsync(name);
                if (info.CharacterName == name)
                {
                    info.CarriedWeight = WeightRules.Carried(items, _catalog.WeightOf);
                }
            }
            while (Interlocked.CompareExchange(ref info.WeightRefreshState, 0, 1) != 1);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref info.WeightRefreshState, 0);
            _logger.Error(exception, "Could not weigh the bag of {clientTag}", client.ClientTag);
        }
    }

    private void OnChanged(string characterName)
    {
        var client = _players.Registry.Clients.FirstOrDefault(candidate =>
            string.Equals(candidate.ConnectionInfo.CharacterName, characterName, StringComparison.Ordinal));
        if (client is not null)
        {
            _ = RefreshAsync(client);
        }
    }
}
