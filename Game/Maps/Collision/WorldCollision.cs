using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Maps.Collision;

public interface IWorldCollision
{
    /// <summary>The loaded obstacles; <see cref="CollisionMap.Empty"/> when there are none.</summary>
    CollisionMap Map { get; }
}

/// <summary>
/// Loads the world's blocking polygons once, from the <c>Maps</c> directory next to the server (the client
/// files written by <c>tools/Export-FieldProps --extract-maps</c>). <c>Map:NoCollisionCheck</c> — the
/// official <c>GameRule::bIsNoCollisionCheck</c> — leaves it empty, and so does a missing directory: every
/// collision test then answers "free", which is the behaviour before this existed.
/// </summary>
public sealed class WorldCollision : IWorldCollision
{
    public WorldCollision(IOptions<MapOptions> options)
    {
        var logger = Log.ForContext<WorldCollision>();
        if (options.Value.NoCollisionCheck)
        {
            Map = CollisionMap.Empty;
            logger.Warning("Map:NoCollisionCheck is set: monsters walk through obstacles");
            return;
        }

        var directory = Path.Combine(Directory.GetCurrentDirectory(), "Maps");
        var watch = Stopwatch.StartNew();
        Map = CollisionMap.Load(directory);
        if (Map.IsEmpty)
        {
            logger.Warning("No obstacle loaded from {directory}: extract the client maps with tools/Export-FieldProps --extract-maps",
                directory);
            return;
        }

        logger.Information("Loaded {polygons} obstacles from {directory} in {ms} ms", Map.Polygons.Count, directory,
            watch.ElapsedMilliseconds);
    }

    private WorldCollision(CollisionMap map)
    {
        Map = map;
    }

    /// <summary>A collision over given obstacles, for the tests (a second public constructor would confuse DI).</summary>
    public static WorldCollision From(CollisionMap map) => new(map);

    public CollisionMap Map { get; }
}
