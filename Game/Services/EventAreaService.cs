using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Maps;
using Navislamia.Game.Maps.Entities;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Event areas (<c>TM_CS_ENTER_EVENT_AREA</c> 15 / <c>TM_CS_LEAVE_EVENT_AREA</c> 16).
///
/// The retail client loads the <c>.nfe</c> polygons itself, but nothing proves that the Epic 7.3
/// client actually emits these packets, and neither rzu nor NGemity has any server packet for event
/// areas. The design is therefore: the packet is a trigger, the polygon is the authority. Every claim
/// is checked against a loaded area and against the session position, and the same check also runs on
/// its own at each position change. EventAreaResource supplies the activation conditions and handlers;
/// verified transitions execute them in the same isolated player sandbox as NPC scripts.
/// </summary>
public class EventAreaService : IEventAreaService
{
    private readonly ILogger _logger = Log.ForContext<EventAreaService>();
    private readonly IMapService _mapService;
    private readonly EventAreaCatalog _catalog;
    private readonly INpcScriptService _scripts;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameClient, TransitionQueue> _queues = new();
    private sealed class TransitionQueue
    {
        public Task Tail = Task.CompletedTask;
        public HashSet<int> Active = new();
        public HashSet<int> Present = new();
        public bool Closing;
        public byte Layer;
        public readonly object Gate = new();
    }

    /// <summary>Side of a grid cell, in world units. A map is 16 128 units wide.</summary>
    private const float CellSize = 2048f;

    /// <summary>An area spanning more cells than this is checked everywhere rather than indexed.</summary>
    private const int MaxIndexedCells = 256;

    /// <summary>The grid built for the snapshot it was built from; rebuilt when the map service's changes.</summary>
    private AreaGrid _grid;

    public EventAreaService(IMapService mapService, EventAreaCatalog catalog = null, INpcScriptService scripts = null,
        EventAreaTransitions transitions = null)
    {
        _mapService = mapService;
        _catalog = catalog; _scripts = scripts;
        if (transitions is not null) transitions.Changed = client => Refresh(client);
    }

    public bool HandlePacket(GameClient client, ReadOnlySpan<byte> packet, bool isEnter)
    {
        var queue = _queues.GetOrCreateValue(client);
        lock (queue.Gate)
        {
            if (queue.Closing) return false;
            if (client.ConnectionInfo.CharacterHandle == 0 || string.IsNullOrEmpty(client.ConnectionInfo.CharacterName)) return false;
            if (_catalog is not null && _scripts is not null)
            {
                if (!GameEventAreaPackets.TryReadEventAreaRequest(packet, out var request)
                    || !_mapService.TryGetEventArea(request.EventAreaId, request.AreaIndex, out var claimed)
                    || IsInside(claimed, client.ConnectionInfo) != isEnter) return false;
                return SyncAreas(client, queue);
            }
            var before = client.ConnectionInfo.CurrentEventAreaId;
            var changed = HandlePacket(client.ConnectionInfo, client.ClientTag, packet, isEnter);
            if (changed) Enqueue(client, queue, before, client.ConnectionInfo.CurrentEventAreaId);
            return changed;
        }
    }

    public bool Refresh(GameClient client)
    {
        var queue = _queues.GetOrCreateValue(client);
        lock (queue.Gate)
        {
            if (queue.Closing) return false;
            if (client.ConnectionInfo.CharacterHandle == 0 || string.IsNullOrEmpty(client.ConnectionInfo.CharacterName)) return false;
            if (_catalog is not null && _scripts is not null) return SyncAreas(client, queue);
            var before = client.ConnectionInfo.CurrentEventAreaId;
            var changed = Refresh(client.ConnectionInfo, client.ClientTag);
            if (changed) Enqueue(client, queue, before, client.ConnectionInfo.CurrentEventAreaId);
            return changed;
        }
    }

    private bool SyncAreas(GameClient client, TransitionQueue queue)
    {
        var desired = new HashSet<int>();
        foreach (var area in ContainingAreas(client.ConnectionInfo)) desired.Add(area.Id);
        var layerChanged = queue.Layer != client.ConnectionInfo.Layer;
        var changed = layerChanged;
        foreach (var before in queue.Present)
            if (layerChanged || !desired.Contains(before)) { Enqueue(client, queue, before, 0); changed = true; }
        foreach (var after in desired)
            if (layerChanged || !queue.Present.Contains(after)) { Enqueue(client, queue, 0, after); changed = true; }
        queue.Present = desired; queue.Layer = client.ConnectionInfo.Layer;
        client.ConnectionInfo.CurrentEventAreaId = desired.Count > 0 ? System.Linq.Enumerable.First(desired) : 0;
        return changed;
    }

    public void EnterWorld(GameClient client)
    {
        var queue = _queues.GetOrCreateValue(client);
        lock (queue.Gate) queue.Closing = false;
        Refresh(client);
    }

    private void Enqueue(GameClient client, TransitionQueue queue, int before, int after)
    {
        var name = client.ConnectionInfo.CharacterName; var handle = client.ConnectionInfo.CharacterHandle;
        var preceding = queue.Tail;
        queue.Tail = RunAsync();
        async Task RunAsync()
        {
            // Never run a script synchronously while the queue gate or the world visibility locks are held.
            await Task.Yield(); await preceding;
            if (client.ConnectionInfo.CharacterName != name || client.ConnectionInfo.CharacterHandle != handle) return;
            try
            {
                if (_scripts is null || _catalog is null) return;
                if (before != 0 && queue.Active.Remove(before) && _catalog.Rows.TryGetValue(before, out var leave))
                { await _scripts.RunEventAreaAsync(client, leave, false); }
                if (after != 0 && _catalog.Rows.TryGetValue(after, out var enter)
                    && await _scripts.RunEventAreaAsync(client, enter, true)) queue.Active.Add(after);
            }
            catch (Exception ex) { _logger.Error(ex, "Event area {Area} failed for {Character}", after, name); }
        }
    }

    public Task FlushAsync(GameClient client)
    { var queue = _queues.GetOrCreateValue(client); lock (queue.Gate) return queue.Tail; }

    public Task LeaveWorldAsync(GameClient client)
    {
        var queue = _queues.GetOrCreateValue(client);
        lock (queue.Gate)
        {
            queue.Closing = true;
            var before = client.ConnectionInfo.CurrentEventAreaId;
            client.ConnectionInfo.CurrentEventAreaId = 0;
            if (_catalog is not null && _scripts is not null)
            {
                foreach (var area in queue.Present) Enqueue(client, queue, area, 0);
                queue.Present.Clear();
            }
            else if (before != 0) Enqueue(client, queue, before, 0);
            return queue.Tail;
        }
    }

    /// <summary>
    /// Session level entry point. It works on <see cref="ConnectionInfo"/> rather than on the client
    /// because the area state is session state and nothing else is needed, which keeps the whole
    /// decision path testable without a live socket.
    /// </summary>
    public bool HandlePacket(ConnectionInfo session, string clientTag, ReadOnlySpan<byte> packet, bool isEnter)
    {
        if (session == null)
        {
            return false;
        }

        if (!GameEventAreaPackets.TryReadEventAreaRequest(packet, out var request))
        {
            _logger.Debug("{clientTag} sent a malformed event area packet ({length} bytes), ignored",
                clientTag, packet.Length);
            return false;
        }

        if (!_mapService.TryGetEventArea(request.EventAreaId, out var area))
        {
            _logger.Debug(
                "{clientTag} claimed {verb} event area {eventAreaId} (area_index {areaIndex}) which is not loaded, ignored",
                clientTag, isEnter ? "enter" : "leave", request.EventAreaId, request.AreaIndex);
            return false;
        }

        var inside = IsInside(area, session);
        var transition = EventAreaRules.Resolve(isEnter, inside, session.CurrentEventAreaId == request.EventAreaId);

        switch (transition)
        {
            case EventAreaTransition.Entered:
                session.CurrentEventAreaId = request.EventAreaId;
                _logger.Debug("{clientTag} entered event area {eventAreaId} (area_index {areaIndex}) at ({x}, {y})",
                    clientTag, request.EventAreaId, request.AreaIndex, session.X, session.Y);
                return true;

            case EventAreaTransition.Left:
                session.CurrentEventAreaId = 0;
                _logger.Debug("{clientTag} left event area {eventAreaId} (area_index {areaIndex}) at ({x}, {y})",
                    clientTag, request.EventAreaId, request.AreaIndex, session.X, session.Y);
                return true;

            case EventAreaTransition.Ignored:
                _logger.Debug(
                    "{clientTag} claimed {verb} event area {eventAreaId} but the server position ({x}, {y}) says otherwise, ignored",
                    clientTag, isEnter ? "enter" : "leave", request.EventAreaId, session.X, session.Y);
                return false;

            default:
                return false;
        }
    }

    public bool Refresh(ConnectionInfo session, string clientTag)
    {
        if (session == null)
        {
            return false;
        }

        var current = session.CurrentEventAreaId;

        if (current != 0 && _mapService.TryGetEventArea(current, out var currentArea) && IsInside(currentArea, session))
        {
            return false;
        }

        if (!TryFindContainingArea(session, out var area))
        {
            if (current == 0)
            {
                return false;
            }

            session.CurrentEventAreaId = 0;
            _logger.Debug("{clientTag} left event area {eventAreaId} at ({x}, {y}) without a client packet",
                clientTag, current, session.X, session.Y);
            return true;
        }

        if (area.Id == current)
        {
            return false;
        }

        session.CurrentEventAreaId = area.Id;
        _logger.Debug("{clientTag} entered event area {eventAreaId} at ({x}, {y}) without a client packet",
            clientTag, area.Id, session.X, session.Y);
        return true;
    }

    /// <summary>
    /// The areas carry neither map nor layer, so a position can only be tested against the loaded set
    /// (see the packet spec, "NON ETABLI" 6). The first containing area wins: overlapping areas are
    /// not described by any reference, so the session keeps the one it already has and only switches
    /// when that one is left.
    /// </summary>
    /// <remarks>
    /// This runs on every position change, and it used to test every loaded area in turn. The candidates
    /// now come from a grid over the areas' bounding boxes, visited in snapshot order so "the first
    /// containing area" is still the same area.
    /// </remarks>
    private bool TryFindContainingArea(ConnectionInfo session, out EventAreaInfo found)
    {
        foreach (var area in ContainingAreas(session)) { found = area; return true; }
        found = null; return false;
    }

    private IEnumerable<EventAreaInfo> ContainingAreas(ConnectionInfo session)
    {
        var grid = GridFor(_mapService.GetEventAreas());
        grid.Cells.TryGetValue(CellOf(session.X, session.Y), out var local);
        local ??= Array.Empty<int>();
        var everywhere = grid.Everywhere;

        // Merge the two ascending index lists, so candidates keep the snapshot order.
        int i = 0, j = 0;
        while (i < local.Length || j < everywhere.Length)
        {
            var index = j >= everywhere.Length || (i < local.Length && local[i] < everywhere[j])
                ? local[i++]
                : everywhere[j++];

            var area = grid.Source[index];
            if (IsInside(area, session))
            {
                yield return area;
            }
        }
    }

    private AreaGrid GridFor(EventAreaInfo[] areas)
    {
        var grid = Volatile.Read(ref _grid);
        if (grid is not null && ReferenceEquals(grid.Source, areas))
        {
            return grid;
        }

        grid = AreaGrid.Build(areas);
        Volatile.Write(ref _grid, grid);
        return grid;
    }

    private static (int X, int Y) CellOf(float x, float y) =>
        ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(y / CellSize));

    private sealed class AreaGrid
    {
        public EventAreaInfo[] Source { get; private init; }

        /// <summary>Indices into <see cref="Source"/>, ascending, per cell.</summary>
        public Dictionary<(int X, int Y), int[]> Cells { get; private init; }

        /// <summary>Indices of areas too large to index, ascending.</summary>
        public int[] Everywhere { get; private init; }

        public static AreaGrid Build(EventAreaInfo[] areas)
        {
            var cells = new Dictionary<(int, int), List<int>>();
            var everywhere = new List<int>();

            for (var index = 0; index < areas.Length; index++)
            {
                var polygon = areas[index]?.Area;
                if (ReferenceEquals(polygon, null))
                {
                    continue;
                }

                var box = polygon.GetBoundingBox();
                var (minX, minY) = CellOf(MathF.Min(box.GetLeft(), box.GetRight()),
                    MathF.Min(box.GetTop(), box.GetBottom()));
                var (maxX, maxY) = CellOf(MathF.Max(box.GetLeft(), box.GetRight()),
                    MathF.Max(box.GetTop(), box.GetBottom()));

                if ((long)(maxX - minX + 1) * (maxY - minY + 1) > MaxIndexedCells)
                {
                    everywhere.Add(index);
                    continue;
                }

                for (var x = minX; x <= maxX; x++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        if (!cells.TryGetValue((x, y), out var list))
                        {
                            cells[(x, y)] = list = new List<int>();
                        }

                        list.Add(index);
                    }
                }
            }

            var frozen = new Dictionary<(int X, int Y), int[]>(cells.Count);
            foreach (var (cell, list) in cells)
            {
                frozen[cell] = list.ToArray();
            }

            return new AreaGrid { Source = areas, Cells = frozen, Everywhere = everywhere.ToArray() };
        }
    }

    /// <summary>
    /// Point in polygon. <c>PolygonF.IsIncluded</c> is the containment test (bounding box plus
    /// crossing parity); <c>PolygonF.Contains</c> only compares against the vertices and would answer
    /// "yes" for a point nowhere near the area.
    /// </summary>
    private static bool IsInside(EventAreaInfo area, ConnectionInfo session)
    {
        // ReferenceEquals, not `!= null`: PolygonF overloads == with a body that compares against
        // null through the same operator, so any `polygon != null` recurses until the stack dies.
        if (area == null || ReferenceEquals(area.Area, null))
        {
            return false;
        }

        // The bounding box first, on the floats: IsIncluded(x, y) allocates a PointF (a class) per call.
        return area.Area.GetBoundingBox().IsInclude(session.X, session.Y)
               && area.Area.IsIncluded(session.X, session.Y);
    }
}
