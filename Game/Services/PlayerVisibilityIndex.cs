using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services;

/// <summary>
/// The presence index of the player visibility socle: a <b>mutable</b> grid of characters, queried by
/// neighbourhood and filtered by euclidean distance — the same model as
/// <see cref="SpatialIndex{T}"/> (cells of <see cref="WorldVisibility.ViewRange"/> = 540 units), with
/// the three operations the spawn indexes never needed: <see cref="Add"/>, <see cref="Move"/> and
/// <see cref="Remove"/>.
/// </summary>
/// <remarks>
/// <para>
/// It is a <b>separate</b> structure on purpose: <see cref="SpatialIndex{T}"/> is built once from an
/// enumeration and is used by the NPC, field prop and monster services that must not be disturbed
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.2 point 1). <see cref="WorldVisibility"/> is not
/// modified either: the window values are read from it, not redefined here.
/// </para>
/// <para>
/// The <b>layer is part of the cell key</b>: two characters on different layers are never in each
/// other's view, exactly as the reference indexes its regions by <c>(rx, ry, layer)</c>
/// (NGemity <c>Map/RegionContainer.h:53-54</c>), and both <c>TS_SC_ENTER</c> and <c>TS_SC_MOVE</c>
/// carry the layer. <see cref="PeersInViewOf"/> therefore queries the whole neighbourhood of one
/// layer only.
/// </para>
/// <para>
/// Every operation takes the index's own lock: the receiving thread of one client moves its character
/// while the receiving thread of another queries the window it is in. The index holds no other lock
/// and never calls out, so it can never be part of a lock cycle
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.4 rule 1).
/// </para>
/// </remarks>
public sealed class PlayerVisibilityIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Y, byte Layer), List<PlayerPresence>> _cells = new();
    private readonly Dictionary<uint, PlayerPresence> _byHandle = new();
    private readonly object _lock = new();

    /// <param name="cellSize">
    /// The side of a cell. Defaults to <see cref="WorldVisibility.ViewRange"/> (540): one query then
    /// walks at most three by three cells, and every presence within range is in them.
    /// </param>
    public PlayerVisibilityIndex(float cellSize = WorldVisibility.ViewRange)
    {
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        _cellSize = cellSize;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byHandle.Count;
            }
        }
    }

    /// <summary>
    /// Indexes <paramref name="presence"/>. Returns false when the handle was already indexed — the
    /// newer presence replaces the older one, which is what a re-entry of the same character (a warp
    /// that did not leave, a login over a stale entry) must do rather than leave two entries.
    /// </summary>
    public bool Add(PlayerPresence presence)
    {
        if (presence is null)
        {
            throw new ArgumentNullException(nameof(presence));
        }

        lock (_lock)
        {
            var replaced = false;

            if (_byHandle.TryGetValue(presence.Handle, out var previous))
            {
                Detach(previous);
                replaced = true;
            }

            Attach(presence);
            _byHandle[presence.Handle] = presence;

            return !replaced;
        }
    }

    /// <summary>Forgets <paramref name="handle"/>. Returns the presence that was indexed, or null.</summary>
    public PlayerPresence Remove(uint handle)
    {
        lock (_lock)
        {
            if (!_byHandle.TryGetValue(handle, out var presence))
            {
                return null;
            }

            Detach(presence);
            _byHandle.Remove(handle);

            return presence;
        }
    }

    public bool TryGet(uint handle, out PlayerPresence presence)
    {
        lock (_lock)
        {
            return _byHandle.TryGetValue(handle, out presence);
        }
    }

    /// <summary>
    /// Re-indexes the presence of <paramref name="handle"/> at its new place. A no-op when the handle
    /// is not indexed: a character that is not in the world has no place to move.
    /// </summary>
    /// <param name="layer">
    /// The layer of the new place. A layer change is not a move but a leave and an entry
    /// (<c>WarpService</c> does exactly that), so callers keep the indexed one by default.
    /// </param>
    public void Move(uint handle, float x, float y, byte? layer = null)
    {
        lock (_lock)
        {
            if (!_byHandle.TryGetValue(handle, out var presence))
            {
                return;
            }

            var oldCell = GetCell(presence.X, presence.Y, presence.Layer);
            var newLayer = layer ?? presence.Layer;
            var newCell = GetCell(x, y, newLayer);

            // Detach before mutating: the cell of a presence is derived from its place.
            if (!oldCell.Equals(newCell))
            {
                Detach(presence);
                presence.X = x;
                presence.Y = y;
                presence.Layer = newLayer;
                Attach(presence);
                return;
            }

            presence.X = x;
            presence.Y = y;
            presence.Layer = newLayer;
        }
    }

    /// <summary>
    /// The peers <paramref name="handle"/> can see: same layer, inside <see cref="WorldVisibility.ViewRange"/>,
    /// itself excluded. Empty when the handle is not indexed.
    /// </summary>
    public IReadOnlyList<PlayerPresence> PeersInViewOf(uint handle)
    {
        lock (_lock)
        {
            if (!_byHandle.TryGetValue(handle, out var self))
            {
                return Array.Empty<PlayerPresence>();
            }

            return Query(self.X, self.Y, self.Layer, _cellSize, self.Handle);
        }
    }

    /// <summary>
    /// The presences of <paramref name="layer"/> within <paramref name="range"/> of a point, the
    /// presence of <paramref name="excludeHandle"/> excepted. The raw query the streaming service uses
    /// to find the observers of a character that has just left the world.
    /// </summary>
    public IReadOnlyList<PlayerPresence> WithinRange(float x, float y, byte layer, float range,
        uint excludeHandle = 0)
    {
        if (range < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        lock (_lock)
        {
            return Query(x, y, layer, range, excludeHandle);
        }
    }

    /// <summary>Caller holds <see cref="_lock"/>.</summary>
    private IReadOnlyList<PlayerPresence> Query(float x, float y, byte layer, float range, uint excludeHandle)
    {
        var result = new List<PlayerPresence>();
        var rangeSquared = range * range;
        var min = (int)MathF.Floor((x - range) / _cellSize);
        var max = (int)MathF.Floor((x + range) / _cellSize);
        var minY = (int)MathF.Floor((y - range) / _cellSize);
        var maxY = (int)MathF.Floor((y + range) / _cellSize);

        for (var cellX = min; cellX <= max; cellX++)
        {
            for (var cellY = minY; cellY <= maxY; cellY++)
            {
                if (!_cells.TryGetValue((cellX, cellY, layer), out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry.Handle == excludeHandle && excludeHandle != 0)
                    {
                        continue;
                    }

                    var deltaX = entry.X - x;
                    var deltaY = entry.Y - y;

                    if (deltaX * deltaX + deltaY * deltaY <= rangeSquared)
                    {
                        result.Add(entry);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Caller holds <see cref="_lock"/>.</summary>
    private void Attach(PlayerPresence presence)
    {
        var key = GetCell(presence.X, presence.Y, presence.Layer);

        if (!_cells.TryGetValue(key, out var entries))
        {
            entries = new List<PlayerPresence>();
            _cells.Add(key, entries);
        }

        entries.Add(presence);
    }

    /// <summary>Caller holds <see cref="_lock"/>.</summary>
    private void Detach(PlayerPresence presence)
    {
        var key = GetCell(presence.X, presence.Y, presence.Layer);

        if (!_cells.TryGetValue(key, out var entries))
        {
            return;
        }

        entries.Remove(presence);

        // An empty cell is dropped: a grid that keeps every cell a character ever walked through
        // would grow with the session and never shrink.
        if (entries.Count == 0)
        {
            _cells.Remove(key);
        }
    }

    private (int X, int Y, byte Layer) GetCell(float x, float y, byte layer)
    {
        return ((int)MathF.Floor(x / _cellSize), (int)MathF.Floor(y / _cellSize), layer);
    }
}
