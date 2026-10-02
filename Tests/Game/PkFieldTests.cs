using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Maps;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Scripting;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class PkFieldTests
{
    [TestCase(1)] [TestCase(3)] [TestCase(5)] [TestCase(7)] [TestCase(8)]
    [TestCase(9)] [TestCase(10)] [TestCase(11)] [TestCase(15)] [TestCase(16)]
    public void Protected_location_types_disallow_pk(short type) => PkFieldService.AllowsPk(10, type).Should().BeFalse();

    [TestCase(0)] [TestCase(2)] [TestCase(4)] [TestCase(6)] [TestCase(12)] [TestCase(13)] [TestCase(14)]
    public void Field_and_dungeon_types_allow_pk(short type) => PkFieldService.AllowsPk(10, type).Should().BeTrue();

    [Test]
    public void Abadon_is_protected_even_when_its_type_is_a_dungeon() => PkFieldService.AllowsPk(110900, 4).Should().BeFalse();

    private static void Text(BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0"); writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static void Polygon(BinaryWriter w, int left, int top, int right, int bottom)
    {
        w.Write(4); w.Write(left); w.Write(top); w.Write(right); w.Write(top);
        w.Write(right); w.Write(bottom); w.Write(left); w.Write(bottom);
    }

    private static string LocationsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "navislamia-locations-" + Guid.NewGuid() + ".nfl");
        using var w = new BinaryWriter(File.Create(path));
        w.Write(3);
        // An unassigned location must consume its polygons without ending the whole file.
        w.Write(1); for (var n = 0; n < 4; n++) w.Write(0f);
        Text(w, ""); Text(w, ""); w.Write(1); Polygon(w, 400, 400, 420, 420);
        w.Write(20); for (var n = 0; n < 4; n++) w.Write(0f);
        Text(w, "field"); Text(w, "call_lc_In(101)"); w.Write(2);
        Polygon(w, 0, 0, 150, 150); Polygon(w, 200, 200, 350, 350);
        w.Write(5); for (var n = 0; n < 4; n++) w.Write(0f);
        Text(w, "town"); Text(w, "call_lc_In(102)"); w.Write(1); Polygon(w, 20, 20, 60, 60);
        return path;
    }

    private static void Load(MapService map, string file, int cellX = 0) => typeof(MapService)
        .GetMethod("LoadLocationFile", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(map, new object[] { file, cellX, 0, 1f, 1000f });

    [Test]
    public async Task Real_nfl_loader_keeps_multiple_polygons_and_priorities_and_lua_ids()
    {
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        var map = new MapService(Options.Create(new MapOptions { Width = 3000, Height = 1000 }), NullLogger<MapService>.Instance, scripts);
        var path = LocationsFile();
        try
        {
            await Task.WhenAll(Task.Run(() => Load(map, path)), Task.Run(() => Load(map, path, 1)));
            map.GetLocationId(10, 10).Should().Be(101);
            map.GetLocationId(230, 230).Should().Be(101, "the first polygon was not overwritten by the second");
            map.GetLocationId(40, 40).Should().Be(102, "the lower priority town wins over the field");
            map.GetLocationId(1040, 40).Should().Be(102, "map cells have independent offsets and Lua contexts");
            map.GetLocationId(1230, 230).Should().Be(101);
            map.GetLocationId(410, 410).Should().Be(0);
            map.GetLocationId(float.NaN, 40).Should().Be(0);
            var rows = A.Fake<IWorldLocationRepository>();
            A.CallTo(() => rows.GetAll()).Returns(new List<WorldLocationEntity>
                { new() { Id = 101, LocationType = 2 }, new() { Id = 102, LocationType = 1 } });
            var rules = A.Fake<IOptionsMonitor<GameRuleOptions>>();
            A.CallTo(() => rules.CurrentValue).Returns(new GameRuleOptions());
            var pk = new PkFieldService(map, new WorldLocationService(rows), rules);
            pk.IsPkField(new ConnectionInfo { X = 10, Y = 10 }).Should().BeTrue();
            pk.IsPkField(new ConnectionInfo { X = 40, Y = 40 }).Should().BeFalse();
            pk.IsPkField(new ConnectionInfo { X = 410, Y = 410 }).Should().BeFalse();
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Unknown_location_data_is_protected_and_the_debug_override_is_explicit()
    {
        var map = A.Fake<IMapService>(); var locations = A.Fake<IWorldLocationService>();
        A.CallTo(() => map.GetLocationId(A<float>._, A<float>._)).Returns(99);
        var monitor = A.Fake<IOptionsMonitor<GameRuleOptions>>(); var rules = new GameRuleOptions();
        A.CallTo(() => monitor.CurrentValue).Returns(rules);
        var pk = new PkFieldService(map, locations, monitor);
        pk.IsPkField(new ConnectionInfo { X = 10, Y = 10 }).Should().BeFalse();
        rules.PkFieldsEverywhere = true;
        pk.IsPkField(new ConnectionInfo { X = 10, Y = 10 }).Should().BeTrue();
    }
}
