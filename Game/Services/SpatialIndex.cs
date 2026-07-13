using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services;

public sealed class SpatialIndex<T>
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Y), List<Entry>> _cells = new();

    public int Count { get; }

    public SpatialIndex(IEnumerable<T> items, Func<T, float> getX, Func<T, float> getY, float cellSize)
    {
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;

        foreach (var item in items)
        {
            var x = getX(item);
            var y = getY(item);
            var key = GetCell(x, y);

            if (!_cells.TryGetValue(key, out var entries))
            {
                entries = new List<Entry>();
                _cells.Add(key, entries);
            }

            entries.Add(new Entry(item, x, y));
            Count++;
        }
    }

    public IReadOnlyList<T> WithinRange(float x, float y, float range)
    {
        if (range < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        var result = new List<T>();
        var rangeSquared = range * range;
        var min = GetCell(x - range, y - range);
        var max = GetCell(x + range, y + range);

        for (var cellX = min.X; cellX <= max.X; cellX++)
        {
            for (var cellY = min.Y; cellY <= max.Y; cellY++)
            {
                if (!_cells.TryGetValue((cellX, cellY), out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    var deltaX = entry.X - x;
                    var deltaY = entry.Y - y;

                    if (deltaX * deltaX + deltaY * deltaY <= rangeSquared)
                    {
                        result.Add(entry.Item);
                    }
                }
            }
        }

        return result;
    }

    private (int X, int Y) GetCell(float x, float y)
    {
        return ((int)MathF.Floor(x / _cellSize), (int)MathF.Floor(y / _cellSize));
    }

    private readonly record struct Entry(T Item, float X, float Y);
}
