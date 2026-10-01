using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The pure time-based movement math, ported from the reference's <c>ArMoveVector::SetMove</c> and
/// <c>Step</c>: a move takes <c>length / (speed / 30)</c> ar_time ticks and the position interpolates
/// linearly from start to destination over that span. The server runs this with the same start tick and
/// speed it broadcasts, so its notion of a monster's position matches the animation the client plays.
/// </summary>
public static class MonsterMovement
{
    private const float TicksScale = 30f;

    public static uint EndTick(uint startTick, float length, byte speed)
    {
        if (speed == 0 || length < 0.5f)
        {
            return startTick;
        }

        return unchecked(startTick + (uint)MathF.Round(length * TicksScale / speed));
    }

    public static (float X, float Y) PositionAt(
        float startX, float startY, float destX, float destY, uint startTick, uint endTick, uint nowTick)
    {
        var total = unchecked((int)(endTick - startTick));
        if (total <= 0)
        {
            return (destX, destY);
        }

        var elapsed = unchecked((int)(nowTick - startTick));
        if (elapsed <= 0)
        {
            return (startX, startY);
        }

        if (elapsed >= total)
        {
            return (destX, destY);
        }

        var f = (float)elapsed / total;
        return (startX + (destX - startX) * f, startY + (destY - startY) * f);
    }

    /// <summary>
    /// The speed byte of <c>TS_SC_MOVE</c> for a unit's move speed: <c>moveSpeed / 7</c>, at least 1
    /// (<c>StructCreature::GetRealMoveSpeed</c>, the division by 7 at <c>0x140080840</c>; NGemity
    /// <c>Unit::GetRealMoveSpeed</c>). A monster's move speed is its <c>run_speed</c>.
    /// </summary>
    public static byte SpeedByte(float moveSpeed) => (byte)Math.Clamp((int)(moveSpeed / 7f), 1, byte.MaxValue);

    /// <summary>
    /// The end tick of every leg of a path walked at one speed from (startX, startY): leg <c>i</c> ends at
    /// <c>ends[i]</c>, each leg timed like a single move.
    /// </summary>
    public static uint[] PathEndTicks(float startX, float startY, IReadOnlyList<(float X, float Y)> path,
        uint startTick, byte speed)
    {
        var ends = new uint[path.Count];
        var (x, y, tick) = (startX, startY, startTick);
        for (var i = 0; i < path.Count; i++)
        {
            var length = MathF.Sqrt((path[i].X - x) * (path[i].X - x) + (path[i].Y - y) * (path[i].Y - y));
            tick = EndTick(tick, length, speed);
            ends[i] = tick;
            (x, y) = path[i];
        }

        return ends;
    }

    /// <summary>The position along a path at <paramref name="nowTick"/> (see <see cref="PathEndTicks"/>).</summary>
    public static (float X, float Y) PositionAlong(float startX, float startY, IReadOnlyList<(float X, float Y)> path,
        uint[] ends, uint startTick, uint nowTick)
    {
        var (x, y, tick) = (startX, startY, startTick);
        for (var i = 0; i < path.Count; i++)
        {
            if (unchecked((int)(nowTick - ends[i])) < 0)
            {
                return PositionAt(x, y, path[i].X, path[i].Y, tick, ends[i], nowTick);
            }

            (x, y, tick) = (path[i].X, path[i].Y, ends[i]);
        }

        return (x, y);
    }

    /// <summary>
    /// The <c>TS_SC_MOVE</c> of an order for one client (its start time already offset to that client's
    /// clock): every waypoint of a path, or the single destination of a straight move.
    /// </summary>
    public static byte[] Frame(uint handle, uint startTime, byte layer, MoveOrder order)
    {
        if (order.Path is not { Count: > 1 } path)
        {
            return GameMovePackets.BuildMove(handle, startTime, layer, order.Speed, order.DestX, order.DestY);
        }

        var waypoints = new byte[path.Count * 8];
        for (var i = 0; i < path.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(i * 8, 4), path[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(i * 8 + 4, 4), path[i].Y);
        }

        return GameMovePackets.BuildMove(handle, startTime, layer, order.Speed, waypoints);
    }
}
