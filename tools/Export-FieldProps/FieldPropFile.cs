using DataCore;

namespace Navislamia.Tools.ExportFieldProps;

/// <summary>
/// Reads the per-map .qpf field prop files out of the client archive.
/// </summary>
internal static class FieldPropFile
{
    private const int SignLength = 18;
    private const int HeaderLength = SignLength + 4 + 4;

    /// <summary>
    /// Bytes trailing each record. The reference server only knows version 2; this client ships
    /// version 3, whose records are 49 bytes: the 40 read below plus a 9-byte tail. Reading a
    /// 7-byte tail here desynchronises the stream and produces NaN coordinates.
    /// </summary>
    private static int TailLength(int version) => version switch
    {
        3 => 9,
        2 => 7,
        _ => 2
    };

    public static List<PropSpawn> ReadAll(Core core, TerrainSeamlessWorld world)
    {
        var spawns = new List<PropSpawn>();

        foreach (var map in world.Maps)
        {
            var name = map.Name.ToLowerInvariant() + ".qpf";
            var bytes = Find(core, name);
            if (bytes is null || bytes.Length < HeaderLength)
                continue;

            spawns.AddRange(Read(bytes, map.X * world.MapLength, map.Y * world.MapLength, name));
        }

        return spawns;
    }

    private static byte[] Find(Core core, string name)
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

    private static IEnumerable<PropSpawn> Read(byte[] bytes, float originX, float originY, string name)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.ReadBytes(SignLength);

        var version = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (count <= 0)
            yield break;

        var tail = TailLength(version);
        var stride = 40 + tail;
        var body = bytes.Length - HeaderLength;

        if (body != count * stride)
            throw new InvalidOperationException(
                $"{name}: {count} records over {body} bytes is a stride of " +
                $"{(double)body / count:F3}, expected {stride} for version {version}");

        for (var i = 0; i < count; i++)
        {
            var spawn = new PropSpawn
            {
                PropId = reader.ReadInt32(),
                X = reader.ReadSingle() + originX,
                Y = reader.ReadSingle() + originY,
                ZOffset = reader.ReadSingle(),
                RotateX = reader.ReadSingle(),
                RotateY = reader.ReadSingle(),
                RotateZ = reader.ReadSingle(),
                ScaleX = reader.ReadSingle(),
                ScaleY = reader.ReadSingle(),
                ScaleZ = reader.ReadSingle()
            };

            reader.ReadBytes(tail);
            yield return spawn;
        }
    }
}
