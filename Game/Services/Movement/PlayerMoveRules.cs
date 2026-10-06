using System;
using System.Collections.Generic;
using Navislamia.Game.Maps.Collision;

namespace Navislamia.Game.Services.Movement;

/// <summary>What the server does with a player's <c>TM_CS_MOVE_REQUEST</c>.</summary>
public enum MoveVerdict
{
    /// <summary>The walk is taken and echoed.</summary>
    Accept,

    /// <summary>Dropped without an answer (<c>return</c> in the official handler).</summary>
    Ignore,

    /// <summary><c>ACCESS_DENIED</c>, nothing moves.</summary>
    Refuse,

    /// <summary><c>ACCESS_DENIED</c>, and the player is walked back to where the server has it.</summary>
    Correct
}

/// <summary>
/// The official <c>onMoveRequest</c> / <c>GetValidWayPoint</c> (<c>GameMessage.cpp:261-563</c>): the server judges a
/// player's walk against where <b>it</b> has the player — its own interpolation of the last accepted walk, at the
/// speed the server echoed —, which is what catches a teleport and a speed hack, and against the world's obstacles.
/// docs/packet-specs/socle-anti-triche-deplacement.md.
/// </summary>
public static class PlayerMoveRules
{
    /// <summary><c>GameRule::VISIBLE_RANGE</c> (<c>GameRule.h:22</c>): the most the client's position may differ from the server's.</summary>
    public const float VisibleRange = 525f;

    /// <summary><c>g_nMapWidth</c>/<c>g_nMapHeight</c> (<c>Extern.cpp:1</c>): a position outside is refused.</summary>
    public const float MapWidth = 700000f;

    /// <summary><c>g_fMapLength</c>: a target farther than one map tile from the player is dropped.</summary>
    public const float MapLength = 16128f;

    /// <summary>
    /// Judges a walk from the client's position (<c>x</c>, <c>y</c> of the request) along <paramref name="path"/>,
    /// the server having the player at (<paramref name="serverX"/>, <paramref name="serverY"/>), the way the 7.3-era
    /// server does (<c>onMoveRequest</c> of the 2012-11 <c>CaptainHerlockServer.exe</c>, <c>0x140132f90</c>):
    /// <list type="number">
    /// <item>the client's position outside the map, or farther than <see cref="VisibleRange"/> → refused;</item>
    /// <item><b>in a dungeon only</b> (<paramref name="inDungeon"/>: <c>IsInDungeon</c>, <c>IsInSecretDungeon</c>,
    /// <c>IsInInstanceDungeon</c>), an obstacle between two waypoints → corrected back to the server's position, or
    /// dropped when the server's position itself is inside an obstacle;</item>
    /// <item>a waypoint outside the map → refused;</item>
    /// <item>a destination inside an obstacle (<c>GameContent::IsBlocked</c> on the last waypoint) → corrected;</item>
    /// <item>a last waypoint farther than <see cref="MapLength"/> → dropped.</item>
    /// </list>
    /// Out of the dungeons the client's own detours are trusted: its path finder walks around the same <c>.nfa</c>
    /// polygons, and testing each leg again refused honest walks that brushed a polygon. The 2015 source checks every
    /// leg everywhere and the server-to-client segment too — "2014-01-02, extended from the dungeons to every field"
    /// (<c>GameMessage.cpp:321</c>): a later rule, not the 7.3 one.
    /// </summary>
    public static MoveVerdict Judge(float serverX, float serverY, float clientX, float clientY,
        IReadOnlyList<(float X, float Y)> path, CollisionMap map, bool inDungeon = false)
    {
        if (!InMap(clientX, clientY) || Distance(serverX, serverY, clientX, clientY) > VisibleRange)
        {
            return MoveVerdict.Refuse;
        }

        map ??= CollisionMap.Empty;
        var (fromX, fromY) = (clientX, clientY);
        foreach (var (x, y) in path)
        {
            if (!InMap(x, y))
            {
                return MoveVerdict.Refuse;
            }

            if (inDungeon && map.IsSegmentBlocked(fromX, fromY, x, y))
            {
                return Blocked(map, serverX, serverY);
            }

            (fromX, fromY) = (x, y);
        }

        if (path.Count > 0 && map.IsBlocked(path[^1].X, path[^1].Y))
        {
            return Blocked(map, serverX, serverY);
        }

        if (path.Count > 0 && Distance(serverX, serverY, path[^1].X, path[^1].Y) > MapLength)
        {
            return MoveVerdict.Ignore;
        }

        return MoveVerdict.Accept;
    }

    /// <summary>
    /// The location types where the 7.3-era server checks each leg of a walk (<c>StructWorldLocation</c>):
    /// dungeon (4), secret dungeon (12), instance dungeon (14).
    /// </summary>
    public static bool IsDungeonLocation(short locationType) => locationType is 4 or 12 or 14;

    /// <summary><c>GameRule::CHANGE_LOCATION_ERROR_RANGE</c> (10 × <c>DEFAULT_UNIT_SIZE</c>).</summary>
    public const float ErrorRange = 120f;

    /// <summary>
    /// A position the client reports outside a walk (region update, 900): kept within <see cref="ErrorRange"/> of the
    /// server's estimate, the estimate otherwise. The official <c>onRegionUpdate</c> ignores the client's position
    /// altogether; this keeps an honest client's own position, which absorbs the estimate's drift.
    /// </summary>
    public static (float X, float Y) Trusted(float serverX, float serverY, float x, float y) =>
        InMap(x, y) && Distance(serverX, serverY, x, y) < ErrorRange ? (x, y) : (serverX, serverY);

    private static MoveVerdict Blocked(CollisionMap map, float serverX, float serverY) =>
        map.IsBlocked(serverX, serverY) ? MoveVerdict.Ignore : MoveVerdict.Correct;

    public static bool InMap(float x, float y) => x >= 0 && x <= MapWidth && y >= 0 && y <= MapWidth;

    public static float Distance(float ax, float ay, float bx, float by) =>
        MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}
