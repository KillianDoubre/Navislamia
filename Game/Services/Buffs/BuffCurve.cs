using System;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Buffs;

/// <summary>
/// Every buff formula, in one pure place.
/// </summary>
/// <remarks>
/// <b>Every duration column in this table is in seconds and needs converting to ar_time ticks.</b> The
/// reference emulator's loader is explicit about it: <c>delay_cast</c>, <c>delay_cast_per_skl</c>,
/// <c>delay_common</c>, <c>delay_cooltime</c> and <c>delay_cooltime_mode</c> are each read as
/// <c>GetFloat() * 100</c>, and <c>state_second</c> gets the same factor where it is consumed. There is no
/// asymmetry between <c>delay_*</c> and <c>state_second</c>, contrary to what the column names suggest.
/// </remarks>
public static class BuffCurve
{
    public static uint DurationTicks(CastableBuffFields fields, int skillLevel)
    {
        var seconds = fields.StateSecond + fields.StateSecondPerLevel * skillLevel;
        return ServerClock.FromSeconds(seconds);
    }

    public static int StateLevel(CastableBuffFields fields, int skillLevel)
    {
        var level = fields.StateLevelBase + fields.StateLevelPerSkill * skillLevel;
        return level <= 0m ? 0 : (int)level;
    }

    public static int MpCost(CastableBuffFields fields, int skillLevel)
    {
        return Math.Max(0, fields.CostMp + fields.CostMpPerSkl * skillLevel);
    }

    public static uint CastDelayTicks(CastableBuffFields fields, int skillLevel)
    {
        return ServerClock.FromSeconds(fields.DelayCast + fields.DelayCastPerSkl * skillLevel);
    }

    public static uint CooldownTicks(CastableBuffFields fields, int skillLevel)
    {
        return ServerClock.FromSeconds(fields.DelayCooltime + fields.DelayCooltimePerSkl * skillLevel);
    }

    public static uint CommonDelayTicks(CastableBuffFields fields)
    {
        return ServerClock.FromSeconds(fields.DelayCommon);
    }
}
