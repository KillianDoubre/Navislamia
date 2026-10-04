using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Huntaholic;

/// <summary>
/// The loaded HuntaHolic bases (<c>HuntaholicManager::m_vHuntaholicInfo</c>) and the area tests the official
/// manager answers: which HuntaHolic a position belongs to, lobby or dungeon, and the lobby layer of a level.
/// </summary>
public interface IHuntaholicCatalog
{
    IReadOnlyList<HuntaholicRow> All { get; }
    bool TryGet(int huntaholicId, out HuntaholicRow huntaholic);

    /// <summary><c>GetHuntaholicID</c>: the HuntaHolic whose lobby or dungeon holds the position, 0 for none.</summary>
    int GetHuntaholicId(float x, float y);
    bool IsLobby(float x, float y);
    bool IsDungeon(float x, float y);
}

public sealed class HuntaholicCatalog : IHuntaholicCatalog
{
    private readonly Dictionary<int, HuntaholicRow> _byId;

    public HuntaholicCatalog(IOptions<HuntaholicCatalogOptions> options)
    {
        options ??= Options.Create(new HuntaholicCatalogOptions());
        All = (options.Value?.Huntaholics ?? new List<HuntaholicRow>()).Where(h => h.Id > 0).ToArray();
        _byId = All.ToDictionary(h => h.Id);
    }

    public IReadOnlyList<HuntaholicRow> All { get; }

    public bool TryGet(int huntaholicId, out HuntaholicRow huntaholic) => _byId.TryGetValue(huntaholicId, out huntaholic);

    public int GetHuntaholicId(float x, float y)
    {
        foreach (var h in All)
            if (HuntaholicRules.InArea(h.DungeonArea, x, y) || HuntaholicRules.InArea(h.LobbyArea, x, y))
                return h.Id;
        return 0;
    }

    public bool IsLobby(float x, float y) => All.Any(h => HuntaholicRules.InArea(h.LobbyArea, x, y));

    public bool IsDungeon(float x, float y) => All.Any(h => HuntaholicRules.InArea(h.DungeonArea, x, y));
}

/// <summary>The pure HuntaHolic rules of the official <c>HuntaholicManager</c> and <c>GameRule</c>.</summary>
public static class HuntaholicRules
{
    /// <summary><c>HUNTAHOLIC_MAX_INSTANCE_COUNT</c>.</summary>
    public const int MaxInstanceCount = 200;

    /// <summary><c>HUNTAHOLIC_MAX_INSTANCE_COUNT_PER_PAGE</c>.</summary>
    public const int InstancesPerPage = 16;

    public const int MaxNameLength = 30;
    public const int MaxPasswordLength = 16;

    /// <summary><c>HUNTAHOLIC_UNUSABLE_LOBBY_LAYER</c>.</summary>
    public const byte UnusableLobbyLayer = 0xFF;

    /// <summary><c>HUNTAHOLIC_DUNGEON_BEGIN_COUNTDOWN_TIME</c>, ar_time ticks (10 s).</summary>
    public const uint BeginCountdownTicks = 1000;

    /// <summary><c>HUNTAHOLIC_DUNGEON_END_NOTICE_TIME</c>, ar_time ticks (1 min before the end).</summary>
    public const uint EndNoticeTicks = 6000;

    /// <summary><c>HUNTAHOLIC_QUITTING_PENALTY_DEBUFF_TIME</c>, ar_time ticks.</summary>
    public const uint QuittingPenaltyTicks = 6000;

    /// <summary><c>StructState::MOVE_SPEED_SLOWDOWN</c>, the state the quitting penalty applies.</summary>
    public const int MoveSpeedSlowdownState = 313205;

    /// <summary>The fee <c>go_to_huntaholic</c> takes (<c>NPC_huntaholic.lua</c>).</summary>
    public const long LobbyWarpFee = 1000;

    /// <summary>The only HuntaHolic <c>StructSkill::GetTargetHuntaholicID</c> sends anyone to: Bear Road.</summary>
    public const int BearRoadId = 10000;

    /// <summary>The region size the official loader divides the channel boxes by (the one sent at login).</summary>
    public const int RegionSize = WorldVisibility.RegionSize;

    /// <summary>
    /// <c>RestoreStatesOnLeaveInstanceGame(true)</c>: the HP and MP the player entered with. The HP is bounded by the
    /// current maximum (<c>SetHP</c>) and kept at 1 at least, so a player never leaves dead by this restore.
    /// </summary>
    public static (int Hp, int Mp) EntryVitals(int enterHp, int enterMp, int maxHp) =>
        (Math.Clamp(enterHp, 1, Math.Max(1, maxHp)), Math.Max(0, enterMp));

    /// <summary>
    /// <c>HuntaholicLoader</c> turns a channel box into region indices, the right and bottom ones plus one,
    /// and <c>GetHuntaholicID</c> compares the position's region against them inclusively.
    /// </summary>
    public static bool InArea(HuntaholicArea area, float x, float y)
    {
        if (area is null || (area.Left == 0 && area.Right == 0)) return false;
        var rx = (int)(x / RegionSize);
        var ry = (int)(y / RegionSize);
        return rx >= area.Left / RegionSize && rx <= area.Right / RegionSize + 1
            && ry >= area.Top / RegionSize && ry <= area.Bottom / RegionSize + 1;
    }

    /// <summary><c>HUNTAHOLIC_INSTANCE_BASE::IsProperLevel</c>: <c>[min, max)</c>.</summary>
    public static bool IsProperLevel(HuntaholicTierRow tier, int level) => level >= tier.MinLevel && level < tier.MaxLevel;

    /// <summary><c>GetProperLobbyLayer</c>: the first tier, in table order, that takes the level.</summary>
    public static HuntaholicTierRow ProperTier(HuntaholicRow huntaholic, int level) =>
        huntaholic?.Tiers.FirstOrDefault(t => IsProperLevel(t, level));

    public static byte ProperLobbyLayer(HuntaholicRow huntaholic, int level) =>
        ProperTier(huntaholic, level) is { } tier ? (byte)tier.Id : UnusableLobbyLayer;

    /// <summary>The score of a kill by <c>monster_type</c> (1/5/10/150), 0 for a monster that is not a HuntaHolic one.</summary>
    public static int MonsterScore(int monsterType) => monsterType switch
    {
        1 => 1,
        2 => 5,
        3 => 10,
        4 => 150,
        _ => 0
    };

    /// <summary><c>quitHunting</c>: <c>ceil(fPointAdvantage × room score)</c>, on a success only.</summary>
    public static int GainPoint(double pointAdvantage, int roomScore, bool success) =>
        success ? (int)Math.Ceiling((decimal)pointAdvantage * roomScore) : 0;

    /// <summary>
    /// <c>QuitHunting</c>'s result when the caller does not name one: dead → failed by death, below the objective
    /// → retired, no reward asked → retired, otherwise a success.
    /// </summary>
    public static HuntingResult ResolveResult(bool dead, int roomScore, int objective, bool rewarded) =>
        dead ? HuntingResult.FailedByDeath
        : roomScore < objective ? HuntingResult.Retired
        : !rewarded ? HuntingResult.Retired
        : HuntingResult.Success;

    /// <summary><c>CreateInstanceDungeon</c>: 4, 6 or 8 members.</summary>
    public static bool IsValidMaxMember(int count) => count is 4 or 6 or 8;

    /// <summary>
    /// The room name: 1 to 30 letters or digits (<c>GameRule::IsValidName(…, 1, 30)</c>; its character set is not
    /// read, so this is the safe subset the party names already use).
    /// </summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && name.All(char.IsAsciiLetterOrDigit);

    /// <summary>Total pages for a room count, 0 when there is none (<c>onHuntaholicInstanceList</c>).</summary>
    public static int TotalPages(int count) => count > 0 ? (count - 1) / InstancesPerPage + 1 : 0;
}

/// <summary><c>HuntaholicManager::_HUNTING_RESULT</c>, the <c>result_type</c> of 4006.</summary>
public enum HuntingResult
{
    Unknown = -1,
    Success = 0,
    FailedByDeath = 1,
    Retired = 2,
    NoReward = 3,
    FailedByGameTimeLimit = 4
}
