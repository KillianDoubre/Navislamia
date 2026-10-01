using System;
using System.Collections.Generic;

namespace Navislamia.Game.Maps.Collision;

/// <summary>
/// Shortest walk around the blocking polygons: A* over a visibility graph whose nodes are the start, the
/// goal and the vertices of the nearby obstacles pushed slightly outward (docs/packet-specs/
/// socle-deplacement-monstres.md). The official server finds its paths over the same polygons
/// (<c>GameContent::GetCollisionPolygons</c> then <c>X2D::PathFinder</c> in <c>AI_processAttack</c>); its
/// algorithm is not ported, only the result is the same in kind: a list of waypoints that never enters an
/// obstacle.
/// </summary>
/// <remarks>
/// An obstacle that contains the start or the goal is ignored: spawn areas overlap obstacles, and a unit
/// standing inside one must still be able to walk out. The search is bounded — by the number of nodes and
/// by the number of segment tests — and gives up (null) rather than stall a tick.
/// </remarks>
public static class PathFinder
{
    public const float Clearance = 6f;
    public const int MaxNodes = 240;
    public const int MaxSegmentTests = 400_000;

    /// <summary>
    /// The waypoints from (sx, sy) to (gx, gy), the goal last and the start excluded; the goal alone when the
    /// straight line is free; null when no path is found within the bounds.
    /// </summary>
    public static List<(float X, float Y)> Find(CollisionMap map, float sx, float sy, float gx, float gy)
    {
        var length = MathF.Sqrt((gx - sx) * (gx - sx) + (gy - sy) * (gy - sy));
        var margin = Math.Max(240f, length * 0.75f);
        var obstacles = map.Near(Math.Min(sx, gx) - margin, Math.Min(sy, gy) - margin,
            Math.Max(sx, gx) + margin, Math.Max(sy, gy) + margin);
        obstacles.RemoveAll(polygon => polygon.Contains(sx, sy) || polygon.Contains(gx, gy));

        var budget = new Budget();
        if (!Blocked(obstacles, sx, sy, gx, gy, budget))
        {
            return new List<(float, float)> { (gx, gy) };
        }

        var xs = new List<float> { sx, gx };
        var ys = new List<float> { sy, gy };
        foreach (var polygon in obstacles)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                if (!TryOffsetVertex(polygon, i, out var vx, out var vy) || InsideAny(obstacles, vx, vy))
                {
                    continue;
                }

                if (xs.Count >= MaxNodes)
                {
                    return null;
                }

                xs.Add(vx);
                ys.Add(vy);
            }
        }

        return Search(obstacles, xs, ys, budget);
    }

    private static List<(float X, float Y)> Search(List<BlockPolygon> obstacles, List<float> xs, List<float> ys,
        Budget budget)
    {
        const int start = 0, goal = 1;
        var count = xs.Count;
        var g = new float[count];
        var from = new int[count];
        var closed = new bool[count];
        Array.Fill(g, float.PositiveInfinity);
        Array.Fill(from, -1);
        g[start] = 0;

        var open = new PriorityQueue<int, float>();
        open.Enqueue(start, Distance(xs, ys, start, goal));
        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (current == goal)
            {
                var path = new List<(float, float)>();
                for (var node = goal; node != start; node = from[node])
                {
                    path.Add((xs[node], ys[node]));
                }

                path.Reverse();
                return path;
            }

            closed[current] = true;
            for (var next = 0; next < count; next++)
            {
                if (closed[next] || next == current)
                {
                    continue;
                }

                var cost = g[current] + Distance(xs, ys, current, next);
                if (cost >= g[next])
                {
                    continue;
                }

                if (Blocked(obstacles, xs[current], ys[current], xs[next], ys[next], budget))
                {
                    if (budget.Exhausted)
                    {
                        return null;
                    }

                    continue;
                }

                g[next] = cost;
                from[next] = current;
                open.Enqueue(next, cost + Distance(xs, ys, next, goal));
            }
        }

        return null;
    }

    /// <summary>
    /// The vertex pushed <see cref="Clearance"/> units outward along the bisector of its two edges; the
    /// side is the one that leaves the polygon. False for a degenerate vertex.
    /// </summary>
    private static bool TryOffsetVertex(BlockPolygon polygon, int i, out float x, out float y)
    {
        var n = polygon.Count;
        var px = polygon.Xs[(i + n - 1) % n];
        var py = polygon.Ys[(i + n - 1) % n];
        var cx = polygon.Xs[i];
        var cy = polygon.Ys[i];
        var nx = polygon.Xs[(i + 1) % n];
        var ny = polygon.Ys[(i + 1) % n];

        var (ax, ay) = Normalize(px - cx, py - cy);
        var (bx, by) = Normalize(nx - cx, ny - cy);
        var (dx, dy) = Normalize(-(ax + bx), -(ay + by));
        if (dx == 0 && dy == 0)
        {
            // A straight vertex: push along the edge normal.
            (dx, dy) = (-ay, ax);
        }

        x = cx + dx * Clearance;
        y = cy + dy * Clearance;
        if (polygon.Contains(x, y))
        {
            x = cx - dx * Clearance;
            y = cy - dy * Clearance;
        }

        return !polygon.Contains(x, y);
    }

    private static (float, float) Normalize(float x, float y)
    {
        var length = MathF.Sqrt(x * x + y * y);
        return length < 1e-6f ? (0f, 0f) : (x / length, y / length);
    }

    private static bool InsideAny(List<BlockPolygon> obstacles, float x, float y)
    {
        foreach (var polygon in obstacles)
        {
            if (polygon.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Blocked(List<BlockPolygon> obstacles, float ax, float ay, float bx, float by, Budget budget)
    {
        foreach (var polygon in obstacles)
        {
            if (++budget.Tests > MaxSegmentTests)
            {
                budget.Exhausted = true;
                return true;
            }

            if (polygon.Blocks(ax, ay, bx, by))
            {
                return true;
            }
        }

        return false;
    }

    private static float Distance(List<float> xs, List<float> ys, int a, int b) =>
        MathF.Sqrt((xs[a] - xs[b]) * (xs[a] - xs[b]) + (ys[a] - ys[b]) * (ys[a] - ys[b]));

    private sealed class Budget
    {
        public int Tests;
        public bool Exhausted;
    }
}
