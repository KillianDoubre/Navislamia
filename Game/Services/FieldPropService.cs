using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Streams the world's field props to each client, mirroring <see cref="NpcSpawnService"/>. Props are
/// immutable, so the index is built once and never mutated.
/// </summary>
public class FieldPropService : IFieldPropService
{
    private readonly ILogger _logger = Log.ForContext<FieldPropService>();
    private readonly IFieldPropCatalog _catalog;
    private readonly SpatialIndex<FieldPropInstance> _index;
    private readonly IDynamicFieldProps _dynamic;
    private readonly IFieldPropStates _states;
    private readonly IPlayerVisibilityService _players;

    public FieldPropService(IFieldPropCatalog catalog, IDynamicFieldProps dynamic = null, IFieldPropStates states = null,
        IPlayerVisibilityService players = null, bool runTicks = true)
    {
        _catalog = catalog;
        _dynamic = dynamic;
        _states = states;
        _players = players;
        _index = new SpatialIndex<FieldPropInstance>(catalog.Instances,
            prop => prop.X, prop => prop.Y, WorldVisibility.ViewRange);

        _logger.Information("Indexed {count} field props", _index.Count);
        if (runTicks && _states is not null)
        {
            _ = RunAsync();
        }
    }

    /// <summary>FieldPropManager::onProcess, once a second: the props that came back or expired, streamed again.</summary>
    public void Tick(uint now)
    {
        foreach (var prop in _states?.Tick(now) ?? Array.Empty<FieldPropInstance>())
        {
            Refresh(prop.X, prop.Y);
        }
    }

    /// <summary>The players who see a spot get what changed there (a used-up prop leaves, a returning one enters).</summary>
    public void Refresh(float x, float y)
    {
        foreach (var client in _players?.Registry.Clients ?? Array.Empty<GameClient>())
        {
            var info = client.ConnectionInfo;
            if (info.CharacterHandle != 0 && CombatRange.Distance(x, y, info.X, info.Y) <= WorldVisibility.ViewRange)
            {
                Sync(client);
            }
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Tick(ServerClock.Now);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Field prop tick failed");
            }
        }
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            IReadOnlyList<FieldPropInstance> inRange = _index.WithinRange(info.X, info.Y, WorldVisibility.ViewRange);
            if (_states is not null)
            {
                // A prop used up or expired is out of the world until it returns: the streamer sends its LEAVE.
                inRange = inRange.Where(prop => _states.IsPresent(prop.InstanceId)).ToArray();
            }

            // The props created on this layer (HuntaHolic healing props) come and go: a prop that was used is no
            // longer here, so the streamer sends its LEAVE.
            var dynamic = _dynamic?.Within(info.X, info.Y, info.Layer, WorldVisibility.ViewRange);
            if (dynamic is { Count: > 0 })
            {
                var combined = new List<FieldPropInstance>(inRange.Count + dynamic.Count);
                combined.AddRange(inRange);
                combined.AddRange(dynamic);
                inRange = combined;
            }

            WorldObjectStreamer.Stream(client, info.PropVisibilityLock, inRange,
                prop => prop.InstanceId,
                (prop, handle) => GameSpawnPackets.BuildEnterFieldProp(handle, prop.X, prop.Y, 0f,
                    info.Layer, prop.PropId, prop.ZOffset, prop.RotateX, prop.RotateY, prop.RotateZ,
                    prop.ScaleX, prop.ScaleY, prop.ScaleZ, false, 0f),
                info.SpawnedProps,
                info.SpawnedPropInstancesByHandle);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} field prop sync failed", client.ClientTag);
        }
    }
}
