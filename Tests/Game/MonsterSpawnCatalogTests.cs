using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Navislamia.Configuration.Options;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The shipped monster spawn catalog (tools/Import-MonsterSpawns.ps1). The official MapLoader::LoadRegionInfo places
/// an .nfs box at raw x TILE_LENGTH (42) + map index x 16128; the importer once used raw x 48 over 336 raw units,
/// which stretched every box by 8/7 from its map's corner and put monsters in the trainee island's camp.
/// </summary>
[TestFixture]
public class MonsterSpawnCatalogTests
{
    private const int MapLength = 16128;
    private const int TileLength = 42;

    private static MonsterSpawnOptions Catalog()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "monster-spawns.73.json")));
        return document.RootElement.GetProperty("MonsterSpawnCatalog").Deserialize<MonsterSpawnOptions>();
    }

    [Test]
    public void Every_area_is_a_tile_aligned_box_inside_its_own_map()
    {
        var areas = Catalog().Areas;
        areas.Should().NotBeEmpty();

        foreach (var area in areas)
        {
            var map = Regex.Match(area.Map, @"(?i)^m(\d{3})_(\d{3})\.nfs$");
            map.Success.Should().BeTrue(area.Map);
            var originX = int.Parse(map.Groups[1].Value) * MapLength;
            var originY = int.Parse(map.Groups[2].Value) * MapLength;

            foreach (var (value, origin) in new[]
                     { (area.Left, originX), (area.Right, originX), (area.Top, originY), (area.Bottom, originY) })
            {
                (value - origin).Should().BeInRange(0, MapLength, $"{area.Map} group {area.SpawnGroupId}");
                ((value - origin) % TileLength).Should().Be(0, $"{area.Map} group {area.SpawnGroupId}");
            }
        }
    }

    [Test]
    public void The_deva_start_area_sits_where_the_official_loader_puts_it()
    {
        // m010_003.nfs, box 1: raw (62, 74, 80, 93), mob( 12, #box) — "Deva start point 1" of monster_respawn.lua.
        Catalog().Areas.Should().Contain(area => area.SpawnGroupId == 12
            && area.Left == 10 * MapLength + 62 * TileLength && area.Top == 3 * MapLength + 74 * TileLength
            && area.Right == 10 * MapLength + 80 * TileLength && area.Bottom == 3 * MapLength + 93 * TileLength);
    }

    [TestCase(164474, 52932, TestName = "No monster spawns on the Deva start point")]
    [TestCase(168356, 55399, TestName = "No monster spawns on the Asura start point")]
    [TestCase(164335, 49510, TestName = "No monster spawns on the Gaia start point")]
    [TestCase(172543, 51847, TestName = "No monster spawns in the trainee island camp")]
    public void No_monster_spawns_where_a_new_character_stands(int x, int y)
    {
        // ReturnPointRules: the race start points of the trainee island and its camp. The stretched boxes put the
        // Deva and Asura start points inside spawn areas of groups 12, 13 and 1002.
        const int margin = 200;
        Catalog().Areas.Should().NotContain(area =>
            area.Left <= x + margin && area.Right >= x - margin && area.Top <= y + margin && area.Bottom >= y - margin);
    }
}
