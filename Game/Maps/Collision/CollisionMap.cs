using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Navislamia.Game.Maps.Collision;

/// <summary>One blocking polygon of an <c>.nfa</c> file, in world coordinates, with its bounding box.</summary>
public sealed class BlockPolygon
{
    public BlockPolygon(float[] xs, float[] ys)
    {
        Xs = xs;
        Ys = ys;
        MinX = MaxX = xs[0];
        MinY = MaxY = ys[0];
        for (var i = 1; i < xs.Length; i++)
        {
            MinX = Math.Min(MinX, xs[i]);
            MaxX = Math.Max(MaxX, xs[i]);
            MinY = Math.Min(MinY, ys[i]);
            MaxY = Math.Max(MaxY, ys[i]);
        }
    }

    public float[] Xs { get; }
    public float[] Ys { get; }
    public float MinX { get; }
    public float MinY { get; }
    public float MaxX { get; }
    public float MaxY { get; }
    public int Count => Xs.Length;

    /// <summary>Even-odd ray casting: true strictly inside (a point on an edge may answer either way).</summary>
    public bool Contains(float x, float y)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = Xs.Length - 1; i < Xs.Length; j = i++)
        {
            if ((Ys[i] > y) != (Ys[j] > y)
                && x < (Xs[j] - Xs[i]) * (y - Ys[i]) / (Ys[j] - Ys[i]) + Xs[i])
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Whether the segment enters the polygon: it crosses an edge, or one of its ends lies inside.
    /// </summary>
    public bool Blocks(float ax, float ay, float bx, float by)
    {
        if (Math.Max(ax, bx) < MinX || Math.Min(ax, bx) > MaxX || Math.Max(ay, by) < MinY || Math.Min(ay, by) > MaxY)
        {
            return false;
        }

        if (Contains(ax, ay) || Contains(bx, by))
        {
            return true;
        }

        for (int i = 0, j = Xs.Length - 1; i < Xs.Length; j = i++)
        {
            if (Geometry.SegmentsCross(ax, ay, bx, by, Xs[j], Ys[j], Xs[i], Ys[i]))
            {
                return true;
            }
        }

        // Both ends outside and no edge crossed can still hide a segment running through the inside
        // between two vertices: test its middle.
        return Contains((ax + bx) / 2f, (ay + by) / 2f);
    }
}

public static class Geometry
{
    /// <summary>True when the two segments properly cross (touching at an end does not count).</summary>
    public static bool SegmentsCross(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
    {
        var d1 = Cross(cx, cy, dx, dy, ax, ay);
        var d2 = Cross(cx, cy, dx, dy, bx, by);
        var d3 = Cross(ax, ay, bx, by, cx, cy);
        var d4 = Cross(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float Cross(float ox, float oy, float ax, float ay, float bx, float by) =>
        (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);
}

/// <summary>
/// The blocking polygons of the world (<c>.nfa</c>, the attribute polygons of each map), indexed on a grid
/// so a point or a segment is tested against the few polygons around it. Immutable once loaded.
/// docs/packet-specs/socle-deplacement-monstres.md.
/// </summary>
public sealed class CollisionMap
{
    public const float CellSize = 512f;

    private readonly Dictionary<long, List<BlockPolygon>> _cells = new();

    public static readonly CollisionMap Empty = new(Array.Empty<BlockPolygon>());

    public CollisionMap(IReadOnlyList<BlockPolygon> polygons)
    {
        Polygons = polygons;
        foreach (var polygon in polygons)
        {
            for (var cx = Cell(polygon.MinX); cx <= Cell(polygon.MaxX); cx++)
            {
                for (var cy = Cell(polygon.MinY); cy <= Cell(polygon.MaxY); cy++)
                {
                    var key = Key(cx, cy);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        _cells[key] = list = new List<BlockPolygon>();
                    }

                    list.Add(polygon);
                }
            }
        }
    }

    public IReadOnlyList<BlockPolygon> Polygons { get; }

    public bool IsEmpty => Polygons.Count == 0;

    public bool IsBlocked(float x, float y)
    {
        if (!_cells.TryGetValue(Key(Cell(x), Cell(y)), out var list))
        {
            return false;
        }

        foreach (var polygon in list)
        {
            if (polygon.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsSegmentBlocked(float ax, float ay, float bx, float by)
    {
        foreach (var polygon in Near(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by)))
        {
            if (polygon.Blocks(ax, ay, bx, by))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether walking straight from (ax, ay) to (bx, by) enters an obstacle, ignoring the obstacle the
    /// walker already stands in: it may always walk out of it.
    /// </summary>
    public bool IsWalkBlocked(float ax, float ay, float bx, float by)
    {
        foreach (var polygon in Near(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by)))
        {
            if (!polygon.Contains(ax, ay) && polygon.Blocks(ax, ay, bx, by))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The polygons whose cells touch the box, each once.</summary>
    public List<BlockPolygon> Near(float minX, float minY, float maxX, float maxY)
    {
        var result = new List<BlockPolygon>();
        var seen = new HashSet<BlockPolygon>();
        for (var cx = Cell(minX); cx <= Cell(maxX); cx++)
        {
            for (var cy = Cell(minY); cy <= Cell(maxY); cy++)
            {
                if (!_cells.TryGetValue(Key(cx, cy), out var list))
                {
                    continue;
                }

                foreach (var polygon in list)
                {
                    if (seen.Add(polygon) && polygon.MaxX >= minX && polygon.MinX <= maxX
                        && polygon.MaxY >= minY && polygon.MinY <= maxY)
                    {
                        result.Add(polygon);
                    }
                }
            }
        }

        return result;
    }

    private static int Cell(float value) => (int)MathF.Floor(value / CellSize);

    private static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    /// <summary>
    /// Reads every map's <c>.nfa</c> named by <c>terrainseamlessworld.cfg</c> in <paramref name="directory"/>.
    /// A file holds an int32 polygon count, then per polygon an int32 point count and that many int32
    /// (x, y) pairs in attribute units: world = map index × map length + value × (tile length / 8), the
    /// transform <c>MapService.LoadAttributeFile</c> applies. Polygons under three points are skipped.
    /// Empty when the directory or the layout is missing.
    /// </summary>
    public static CollisionMap Load(string directory)
    {
        var cfg = Path.Combine(directory, "terrainseamlessworld.cfg");
        if (!File.Exists(cfg))
        {
            return Empty;
        }

        float tileLength = 0;
        int tilesPerSegment = 0, segmentsPerMap = 0;
        var maps = new List<(int X, int Y, string Name)>();
        foreach (var raw in File.ReadAllLines(cfg))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == ';' || eq < 0)
            {
                continue;
            }

            var key = line[..eq].Trim().ToUpperInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "TILE_LENGTH":
                    tileLength = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "TILECOUNT_PER_SEGMENT":
                    tilesPerSegment = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "SEGMENTCOUNT_PER_MAP":
                    segmentsPerMap = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "MAPFILE":
                    var parts = value.Split(',');
                    if (parts.Length >= 4 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    {
                        maps.Add((x, y, parts[3].Trim()));
                    }

                    break;
            }
        }

        if (tileLength <= 0 || tilesPerSegment <= 0 || segmentsPerMap <= 0)
        {
            return Empty;
        }

        var mapLength = tileLength * tilesPerSegment * segmentsPerMap;
        var attributeLength = tileLength / 8f;
        var polygons = new List<BlockPolygon>();
        foreach (var (mapX, mapY, name) in maps)
        {
            var file = Path.Combine(directory, name + ".nfa");
            if (!File.Exists(file))
            {
                continue;
            }

            ReadAttributeFile(file, mapX * mapLength, mapY * mapLength, attributeLength, polygons);
        }

        return new CollisionMap(polygons);
    }

    public static void ReadAttributeFile(string file, float originX, float originY, float attributeLength,
        List<BlockPolygon> polygons)
    {
        using var reader = new BinaryReader(File.OpenRead(file));
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var points = reader.ReadInt32();
            var xs = new float[Math.Max(points, 0)];
            var ys = new float[xs.Length];
            for (var p = 0; p < xs.Length; p++)
            {
                xs[p] = originX + reader.ReadInt32() * attributeLength;
                ys[p] = originY + reader.ReadInt32() * attributeLength;
            }

            if (xs.Length >= 3)
            {
                polygons.Add(new BlockPolygon(xs, ys));
            }
        }
    }
}
