using System;

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
}
