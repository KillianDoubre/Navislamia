namespace Navislamia.Tools.ExportFieldProps;

internal sealed class MapFile
{
    public int X { get; init; }
    public int Y { get; init; }
    public string Name { get; init; }
}

/// <summary>
/// Reads terrainseamlessworld.cfg the way the reference server's TerrainSeamlessWorldInfo does.
/// </summary>
internal sealed class TerrainSeamlessWorld
{
    public float MapLength { get; private init; }
    public List<MapFile> Maps { get; private init; }

    public static TerrainSeamlessWorld Read(string text)
    {
        float tileLength = 0;
        int tileCountPerSegment = 0;
        int segmentCountPerMap = 0;
        var maps = new List<MapFile>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (line.Length == 0 || line[0] == ';' || separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (name.ToUpperInvariant())
            {
                case "TILE_LENGTH":
                    float.TryParse(value, out tileLength);
                    break;
                case "TILECOUNT_PER_SEGMENT":
                    int.TryParse(value, out tileCountPerSegment);
                    break;
                case "SEGMENTCOUNT_PER_MAP":
                    int.TryParse(value, out segmentCountPerMap);
                    break;
                case "MAPFILE":
                    var fields = value.Split(',');
                    if (fields.Length == 5 &&
                        int.TryParse(fields[0], out var mapX) &&
                        int.TryParse(fields[1], out var mapY))
                    {
                        maps.Add(new MapFile { X = mapX, Y = mapY, Name = fields[3].Trim() });
                    }

                    break;
            }
        }

        var mapLength = segmentCountPerMap * tileLength * tileCountPerSegment;

        if (maps.Count == 0)
            throw new InvalidOperationException("terrainseamlessworld.cfg declares no MAPFILE");

        // The client's XOR key differs from the vanilla one at five indices, and decoding with the
        // wrong key yields plausible text: TILE_LENGTH reads 41 instead of 42, which silently shifts
        // every prop in the world. Assert the geometry rather than trust the parse.
        if (Math.Abs(mapLength - 16128f) > 0.01f)
            throw new InvalidOperationException(
                $"map length is {mapLength} (tile {tileLength} x {tileCountPerSegment} x " +
                $"{segmentCountPerMap}), expected 16128: the archive is decoding with the wrong XOR key");

        return new TerrainSeamlessWorld { MapLength = mapLength, Maps = maps };
    }
}
