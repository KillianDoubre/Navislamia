using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Maps;
using Navislamia.Game.Scripting;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The world's location, script and event area files, read from the client's extracted maps (DevConsole/Maps,
/// git-ignored: the test is inconclusive without them). MapService used to spend about 30 of the server's 40 startup
/// seconds inserting the .nfa obstacles into two quadtrees nothing read; WorldCollision loads them on its own.
/// </summary>
[TestFixture]
public class MapLoadingTests
{
    private static string MapsDirectory()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            var maps = Path.Combine(dir.FullName, "DevConsole", "Maps");
            if (File.Exists(Path.Combine(maps, "TerrainSeamlessWorld.cfg"))) return maps;
        }

        return null;
    }

    [Test]
    public void The_maps_load_quickly_and_place_hidden_village_in_its_location()
    {
        var directory = MapsDirectory();
        if (directory is null) Assert.Inconclusive("DevConsole/Maps is not extracted on this machine");

        var service = new MapService(Options.Create(new MapOptions { Width = 700000, Height = 1000000 }),
            NullLogger<MapService>.Instance, new ScriptService(NullLogger<ScriptService>.Instance));
        var started = Stopwatch.GetTimestamp();
        service.Start(directory);
        var elapsed = Stopwatch.GetElapsedTime(started);

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        service.MapCount.CX.Should().Be(14);
        // Yurie, the Hidden Village teleporter (222198, 20274): WorldLocation 130101, the village itself (string
        // 70130101 "Hidden Village", inside the 130100 tile area of the same name).
        service.GetLocationId(222198, 20274).Should().Be(130101);
        service.GetEventAreas().Should().NotBeEmpty();
    }
}
