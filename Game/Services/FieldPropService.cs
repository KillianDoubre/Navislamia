using System;
using System.Collections.Generic;
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

    public FieldPropService(IFieldPropCatalog catalog, IDynamicFieldProps dynamic = null)
    {
        _catalog = catalog;
        _dynamic = dynamic;
        _index = new SpatialIndex<FieldPropInstance>(catalog.Instances,
            prop => prop.X, prop => prop.Y, WorldVisibility.ViewRange);

        _logger.Information("Indexed {count} field props", _index.Count);
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            var inRange = _index.WithinRange(info.X, info.Y, WorldVisibility.ViewRange);

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
