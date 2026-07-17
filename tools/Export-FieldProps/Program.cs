using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataCore;
using DataCore.Functions;
using Microsoft.Data.SqlClient;

namespace Navislamia.Tools.ExportFieldProps;

internal static class Program
{
    private const float MapLengthExpected = 16128f;

    private const int ExpectedSpawnCount = 3189;
    private const int ExpectedDistinctPropIds = 454;

    private static readonly byte[] ClientKeyOverrides = { 40, 80, 87, 163, 236 };
    private static readonly byte[] ClientKeyValues = { 0x4a, 0x9d, 0x2d, 0x21, 0xa9 };

    private static int Main(string[] args)
    {
        var clientDirectory = Argument(args, "--client") ?? @"C:\Users\Killian\Desktop\Epic_7_3";
        var sqlServer = Argument(args, "--server") ?? @"localhost\SQLEXPRESS";
        var database = Argument(args, "--database") ?? "Arcadia";
        var output = Argument(args, "--output") ?? "DevConsole/field-props.73.json";

        try
        {
            var core = OpenClientArchive(clientDirectory);
            var world = TerrainSeamlessWorld.Read(ReadText(core, "terrainseamlessworld.cfg"));
            Console.WriteLine($"maps declared: {world.Maps.Count}, map length: {world.MapLength}");

            var spawns = FieldPropFile.ReadAll(core, world);
            Console.WriteLine($"prop spawns: {spawns.Count}");

            var templates = FieldPropResource.Read(sqlServer, database);
            Console.WriteLine($"prop templates: {templates.Count}");

            var dungeons = DungeonResource.Read(sqlServer, database);
            Console.WriteLine($"dungeon start positions: {dungeons.Count}");

            Validate(spawns, templates);

            Write(output, spawns, templates, dungeons);
            Console.WriteLine($"wrote {output}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"export failed: {exception.Message}");
            return 1;
        }
    }

    private static Core OpenClientArchive(string clientDirectory)
    {
        var index = Path.Combine(clientDirectory, "data.000");
        if (!File.Exists(index))
            throw new FileNotFoundException($"client index not found: {index}");

        var core = new Core(false, Encoding.Default);

        var key = (byte[])XOR.DefaultKey.Clone();
        for (var i = 0; i < ClientKeyOverrides.Length; i++)
            key[ClientKeyOverrides[i]] = ClientKeyValues[i];

        // The key must be set after constructing Core, which resets it to the vanilla one, and
        // before Load, because the index in data.000 is itself ciphered: loading it with the wrong
        // key corrupts entry offsets, which then read as garbage rather than failing.
        core.SetXORKey(key);
        core.Load(index);
        core.SetXORKey(key);
        return core;
    }

    private static byte[] ReadFile(Core core, string name)
    {
        foreach (var entry in core.Index)
        {
            string entryName;
            try { entryName = entry.Name; }
            catch { continue; }

            if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                return core.GetFileBytes(entry);
        }

        return null;
    }

    private static string ReadText(Core core, string name)
    {
        var bytes = ReadFile(core, name)
            ?? throw new InvalidOperationException($"'{name}' is not in the client archive");
        return Encoding.ASCII.GetString(bytes);
    }

    private static void Validate(
        List<PropSpawn> spawns,
        Dictionary<int, PropTemplate> templates)
    {
        var distinct = spawns.Select(spawn => spawn.PropId).Distinct().ToList();
        var unresolved = distinct.Where(id => !templates.ContainsKey(id)).ToList();
        var malformed = spawns.Count(spawn =>
            float.IsNaN(spawn.X) || float.IsNaN(spawn.Y) ||
            spawn.X < 0 || spawn.X > 700000 || spawn.Y < 0 || spawn.Y > 1000000);

        Console.WriteLine(
            $"distinct prop ids: {distinct.Count}, unresolved: {unresolved.Count}, malformed: {malformed}");

        if (malformed != 0)
            throw new InvalidOperationException(
                $"{malformed} spawns have NaN or out-of-world coordinates: the .qpf record stride is wrong");

        if (unresolved.Count != 0)
            throw new InvalidOperationException(
                $"{unresolved.Count} prop ids have no FieldPropResource row, first: {unresolved[0]}");

        if (spawns.Count != ExpectedSpawnCount || distinct.Count != ExpectedDistinctPropIds)
            throw new InvalidOperationException(
                $"expected {ExpectedSpawnCount} spawns over {ExpectedDistinctPropIds} prop ids, " +
                $"got {spawns.Count} over {distinct.Count}");
    }

    private static void Write(
        string output,
        List<PropSpawn> spawns,
        Dictionary<int, PropTemplate> templates,
        Dictionary<int, DungeonStart> dungeons)
    {
        var used = spawns.Select(spawn => spawn.PropId).Distinct().ToHashSet();

        var catalog = new FieldPropCatalogFile
        {
            Metadata = new CatalogMetadata
            {
                ClientEpic = "7.3",
                Source = "Epic 7.3 client Resource/NewMap/*.qpf positions joined with the 9.4 " +
                         "FieldPropResource and DungeonResource",
                SpawnCount = spawns.Count,
                TemplateCount = used.Count,
                DungeonCount = dungeons.Count
            },
            FieldPropCatalog = new FieldPropCatalogBody
            {
                Templates = templates.Values.Where(template => used.Contains(template.Id))
                    .OrderBy(template => template.Id).ToList(),
                Spawns = spawns,
                Dungeons = dungeons.Values.OrderBy(dungeon => dungeon.Id).ToList()
            }
        };

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(output, JsonSerializer.Serialize(catalog, options), new UTF8Encoding(false));
    }

    private static string Argument(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
