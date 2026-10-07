using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The creature farm's rules and 7.3 constants (docs/packet-specs/socle-ferme-creatures-officielle.md
/// §5.2-§5.4 and §6.2). Every number below is measured on the 2012-11 server binary
/// (<c>epic7part4/server/2012-11/CaptainHerlockServer.exe</c>) unless the comment says otherwise; the
/// 2015 / Epic 9.x values are quoted beside them because they must <b>not</b> be ported (§6.2).
/// <para>
/// The 06:00 nursing reset and the daily <c>refresh_time</c> are wall-clock, not
/// <see cref="ServerClock"/> ticks: the reference builds them from the local server hour
/// (<c>StructPlayer.cpp:11439-11456</c>).
/// </para>
/// </summary>
public static class CreatureFarmRules
{
    /// <summary>
    /// <c>FARM_MAX_COUNT</c>: three farm slots (measured 7.3, <c>cmp $0x3</c> at <c>1400d5938</c>; the
    /// 2015 source says 3 too).
    /// </summary>
    public const int MaxCount = 3;

    /// <summary>
    /// <c>FARM_NON_CASH_MAX_COUNT</c>: one slot for the ordinary tickets, measured 7.3
    /// (<c>cmp $0x1</c> at <c>1400d5965</c>), i.e. slots 1 and 2 are reserved for the premium ones
    /// (<c>GameMessage.cpp:11833-11834</c>).
    /// </summary>
    public const int NonCashMaxCount = 1;

    /// <summary>
    /// <c>FARM_MAX_LEVEL</c> of the 7.3 era: <b>100</b> (<c>0x1404096c0</c>, and the retail texts
    /// <c>smsg_creaturefarm*</c>). The 2015 source says 150 — do not port it (§6.2, A VERIFIER 2).
    /// </summary>
    public const int MaxLevel = 100;

    /// <summary>The level ceiling of the first summon form: <b>60</b> (<c>0x1404096b8</c>), identical in 2015.</summary>
    public const int NormalFormLevelCap = 60;

    /// <summary>The level ceiling of the second summon form: <b>115</b> (<c>0x1404096bc</c>), identical in 2015.</summary>
    public const int GrowthFormLevelCap = 115;

    /// <summary>
    /// The cracker's multiplier on the farmed experience: two doubles, <c>1.5</c> (<c>0x14040a2c8</c>)
    /// and <c>1.0</c> (<c>0x140400730</c>), loaded with <c>movsd</c> (<c>1400d6016</c>, <c>1400d6020</c>).
    /// </summary>
    public const double CrackerRate = 1.5;

    /// <summary>
    /// Farmed experience per whole hour for the first summon form: <b>137 700</b> (<c>0x1404f7e68</c>,
    /// <c>nFarmNormalSummonEXP</c>). The 2015 source says 145 763 — do not port it (§6.2).
    /// </summary>
    public const long NormalExpPerHour = 137_700;

    /// <summary>
    /// Farmed experience per whole hour for the second summon form: <b>347 264</b> (<c>0x1404f7e6c</c>,
    /// <c>nFarmGrowthSummonEXP</c>). The 2015 source says 1 118 029 — do not port it (§6.2).
    /// </summary>
    public const long GrowthExpPerHour = 347_264;

    /// <summary>
    /// <c>ITEM_FLAG_FARMED_SUMMON</c>: bit <b>27</b> of the card's flag (<c>ItemInstance.h:90</c>,
    /// <c>orl $0x8000000</c> at <c>1400d5a1e</c>). Set at deposition, cleared at retrieval.
    /// </summary>
    public const uint FarmedSummonMask = 1u << (int)ItemFlag.FarmedSummon;

    /// <summary>
    /// <c>ITEM_FLAG_NURSED_SUMMON</c>: bit <b>28</b> of the card's flag (<c>ItemInstance.h:91</c>). The mask
    /// is declared because the bit's index is established, but the 7.3 <c>NurseSummon</c> sets no flag at
    /// all (<c>StructPlayer.cpp:11423-11467</c>): whether the nursing lot should use it is open.
    /// </summary>
    public const uint NursedSummonMask = 1u << (int)ItemFlag.NursedSummon;

    /// <summary>The local server hour the nursing day starts at (<c>6</c> at <c>1400d6446</c>).</summary>
    public const int NursingResetHour = 6;

    private static uint Raw(ItemFlag flag) => flag == ItemFlag.None ? 0u : unchecked((uint)flag);

    private static ItemFlag FromRaw(uint raw) => raw == 0 ? ItemFlag.None : unchecked((ItemFlag)raw);

    /// <summary>The card carries <c>ITEM_FLAG_FARMED_SUMMON</c> — it is deposited in the farm.</summary>
    public static bool IsFarmed(ItemFlag flag) => flag != ItemFlag.None && (Raw(flag) & FarmedSummonMask) != 0;

    /// <summary>The card carries <c>ITEM_FLAG_NURSED_SUMMON</c>.</summary>
    public static bool IsNursed(ItemFlag flag) => flag != ItemFlag.None && (Raw(flag) & NursedSummonMask) != 0;

    /// <summary>Poses <c>ITEM_FLAG_FARMED_SUMMON</c> (deposition) without touching the other bits.</summary>
    public static ItemFlag WithFarmedSummon(ItemFlag flag) => FromRaw(Raw(flag) | FarmedSummonMask);

    /// <summary>Clears <c>ITEM_FLAG_FARMED_SUMMON</c> (retrieval) without touching the other bits.</summary>
    public static ItemFlag WithoutFarmedSummon(ItemFlag flag) => FromRaw(Raw(flag) & ~FarmedSummonMask);

    /// <summary>Poses <c>ITEM_FLAG_NURSED_SUMMON</c> without touching the other bits.</summary>
    public static ItemFlag WithNursedSummon(ItemFlag flag) => FromRaw(Raw(flag) | NursedSummonMask);

    /// <summary>Clears <c>ITEM_FLAG_NURSED_SUMMON</c> without touching the other bits.</summary>
    public static ItemFlag WithoutNursedSummon(ItemFlag flag) => FromRaw(Raw(flag) & ~NursedSummonMask);

    /// <summary>
    /// <c>FindSuitableFarmSlot</c> (<c>:11830-11845</c>): slot 0 is reserved for the ordinary tickets and
    /// slots 1..<see cref="MaxCount"/>−1 for the premium ones, so the searched range is
    /// <c>0..NonCashMaxCount</c> or <c>NonCashMaxCount..MaxCount</c>. <paramref name="takenSlots"/> holds the
    /// slots already occupied by the same character; false means the farm is full for that ticket kind.
    /// </summary>
    public static bool TryFindSlot(bool isCash, IReadOnlyCollection<int> takenSlots, out int slot)
    {
        var first = isCash ? NonCashMaxCount : 0;
        var last = isCash ? MaxCount : NonCashMaxCount;
        for (slot = first; slot < last; slot++)
        {
            if (takenSlots is null || !takenSlots.Contains(slot))
            {
                return true;
            }
        }

        slot = -1;
        return false;
    }

    /// <summary>
    /// The whole hours farmed, capped by the ticket's duration (<c>StructPlayer.cpp:11367</c>): the
    /// reference counts from the deposition and never past <c>registration_time + duration</c>. Neither the
    /// <c>6001</c>'s <c>elasped_time</c> nor the nursing time takes part in the experience (§5.3).
    /// </summary>
    public static long FarmedHours(DateTime registrationTime, int durationSeconds, DateTime now)
    {
        var start = Utc(registrationTime);
        var end = start.AddSeconds(durationSeconds > 0 ? durationSeconds : 0);
        var current = Utc(now);
        var counted = current < end ? current : end;
        var hours = (counted - start).TotalHours;
        return hours <= 0 ? 0 : (long)hours;
    }

    /// <summary>
    /// An instant on the UTC line. The farm's times come back from PostgreSQL as UTC (<c>timestamptz</c>) while the
    /// server clock is local: subtracting the two as they are would be wrong by the zone's offset. An unspecified
    /// kind is read as local, the server's own clock.
    /// </summary>
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    /// <summary>The same instant on the server's local clock, where the official 06:00 reset lives (<c>localtime_s</c>).</summary>
    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

    /// <summary>
    /// The farm experience the reference computes at retrieval
    /// (<c>nExp = tHour x EXP_par_heure x dRate</c>, <c>:11373-11398</c>): whole hours, the form's hourly
    /// rate supplied by the caller, and <see cref="CrackerRate"/> when a cracker was consumed. The caller
    /// still has to cap it with the summon's own curve (<c>GetNeedSummonExp</c>) and the level limit —
    /// that step needs the curve's source of truth, which is still open (A VERIFIER 6).
    /// </summary>
    public static long GainedExp(long farmedHours, long expPerHour, bool isUsingCracker) =>
        farmedHours <= 0 ? 0 : (long)(farmedHours * (double)expPerHour * (isUsingCracker ? CrackerRate : 1.0));

    /// <summary>
    /// The level ceiling of a farmed summon: the frozen <c>max_level</c> of the row, never above
    /// <see cref="MaxLevel"/>, then never above the form's ceiling (<c>:11371</c>, <c>:11380-11394</c>).
    /// </summary>
    public static int LevelLimit(int maxLevel, int formLevelCap) =>
        Math.Min(Math.Min(maxLevel, MaxLevel), formLevelCap);

    /// <summary>
    /// The <c>6001</c>'s <c>elasped_time</c> — the reference's own spelling: now − deposition, in seconds
    /// (<c>fillFarmedSummonTimeInfo</c>, <c>GameMessage.cpp:11683-11684</c>). Never negative, and no cap: the frame
    /// carries the ticket's duration separately (§3.2).
    /// </summary>
    public static int ElapsedSeconds(DateTime registrationTime, DateTime now)
    {
        var seconds = (Utc(now) - Utc(registrationTime)).TotalSeconds;
        return seconds <= 0 ? 0 : seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
    }

    /// <summary>
    /// The last nursing reset at or before <paramref name="now"/>: today 06:00 local server time, or
    /// yesterday's when the clock is still before it (<c>StructPlayer.cpp:11439-11456</c>).
    /// </summary>
    public static DateTime LastNursingReset(DateTime now)
    {
        var local = Local(now);
        return local.TimeOfDay >= TimeSpan.FromHours(NursingResetHour)
            ? local.Date.AddHours(NursingResetHour)
            : local.Date.AddDays(-1).AddHours(NursingResetHour);
    }

    /// <summary>
    /// The <c>6001</c>'s <c>refresh_time</c>, <c>fillFarmedSummonTimeInfo</c> (<c>GameMessage.cpp:11686-11706</c>):
    /// 0 for an entry never nursed; otherwise the first 06:00 after the last nursing — the nursing day's 06:00, or the
    /// next day's when the nursing came at or after it — minus now, never negative. An entry nursed since the last
    /// 06:00 therefore waits for the next one, and one nursed before it reads 0: it can be nursed again.
    /// </summary>
    public static int RefreshSeconds(DateTime? nursingTime, DateTime now)
    {
        if (nursingTime is null)
        {
            return 0;
        }

        var nursed = Local(nursingTime.Value);
        var refresh = nursed.Date.AddHours(NursingResetHour);
        if (nursed >= refresh)
        {
            refresh = refresh.AddDays(1);
        }

        var seconds = (refresh - Local(now)).TotalSeconds;
        return seconds <= 0 ? 0 : seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
    }

    /// <summary>
    /// One <c>SUMMON_INFO</c> of the <c>6001</c> (§3.2): the slot as <c>index</c>, the summon's own
    /// experience, its name, the ticket's duration, the elapsed and refresh seconds, the two deposit
    /// bytes, and the card's 75-byte motif. Nothing is defaulted here — every value comes from the farm
    /// row or the card it names.
    /// </summary>
    public static GameFarmPackets.FarmSummonInfo SummonInfo(int slot, long experience, string name, int durationSeconds,
        DateTime registrationTime, DateTime? nursingTime, bool isCash, bool isUsingCracker,
        ItemFixedInfo cardInfo, DateTime now) =>
        new(slot, experience, name, durationSeconds,
            ElapsedSeconds(registrationTime, now), RefreshSeconds(nursingTime, now),
            (byte)(isCash ? 1 : 0), (byte)(isUsingCracker ? 1 : 0), cardInfo);

    /// <summary>
    /// The global Lua function the nursing gesture calls, and the only place the gift lives: the 7.3 server
    /// runs the chunk <c>return NPC_Creature_Farm_nurse_handler()</c> (string at <c>0x140412128</c>) and
    /// inserts the object from inside that script (officiel <c>NPC_Creature_farm.lua:52-59</c>). Its name is
    /// 7.3; its body is not in this repository (A VERIFIER 1).
    /// </summary>
    public const string NurseHandlerFunction = "NPC_Creature_Farm_nurse_handler";

    /// <summary>
    /// Whether <c>NurseSummon</c> accepts a nursing (<c>StructPlayer.cpp:11423-11467</c>, 7.3
    /// <c>0x1400d63a0</c>): the handle must have resolved a card, that card must carry
    /// <c>ITEM_FLAG_FARMED_SUMMON</c> (bit 27), it must be one of the character's farm rows, and that entry
    /// must not have been nursed since the last 06:00. The window is the very clock the <c>6001</c>'s
    /// <c>refresh_time</c> reads: no refresh left (<see cref="RefreshSeconds"/> == 0) is the same statement
    /// as the reference's <c>nursing_time &lt; refresh</c>.
    /// </summary>
    public static bool CanNurse(FarmNursingTarget target, DateTime now) =>
        target is not null
        && IsFarmed(target.Flag)
        && target.IsInFarm
        && RefreshSeconds(target.NursingTime, now) == 0;

    /// <summary>
    /// The <c>6007</c>'s <c>result</c> once the nursing went through: the script's own answer, compared to
    /// the string <c>"1"</c> (7.3 constant at <c>0x140412120</c>; officiel <c>GameMessage.cpp:11931-11938</c>).
    /// A missing script, or any other return, is therefore a <c>NO_REWARD</c> — never a <c>FAILED</c>, which
    /// the reference reserves for the <c>false</c> of <c>NurseSummon</c>.
    /// </summary>
    public static NurseResult NurseVerdict(string nurseHandlerResult) =>
        nurseHandlerResult == "1" ? NurseResult.Rewarded : NurseResult.NoReward;
}
