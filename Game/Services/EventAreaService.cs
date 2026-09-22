using System;
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
/// its own at each position change, so the feature works even if the client stays silent. Nothing is
/// activated and nothing is sent back: no reference describes an activation rule (the data lives in
/// an unimported table) nor any answer packet.
/// </summary>
public class EventAreaService : IEventAreaService
{
    private readonly ILogger _logger = Log.ForContext<EventAreaService>();
    private readonly IMapService _mapService;

    public EventAreaService(IMapService mapService)
    {
        _mapService = mapService;
    }

    public bool HandlePacket(GameClient client, ReadOnlySpan<byte> packet, bool isEnter) =>
        HandlePacket(client.ConnectionInfo, client.ClientTag, packet, isEnter);

    public bool Refresh(GameClient client) => Refresh(client.ConnectionInfo, client.ClientTag);

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
    private bool TryFindContainingArea(ConnectionInfo session, out EventAreaInfo found)
    {
        foreach (var area in _mapService.GetEventAreas())
        {
            if (!IsInside(area, session))
            {
                continue;
            }

            found = area;
            return true;
        }

        found = null;
        return false;
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

        return area.Area.IsIncluded(session.X, session.Y);
    }
}
