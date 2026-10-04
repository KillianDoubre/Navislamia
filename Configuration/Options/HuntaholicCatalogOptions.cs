using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The HuntaHolic (Bear Road) catalogue, exported by <c>tools/export_huntaholic_catalog.py</c> into
/// <c>DevConsole/huntaholic-catalog.73.json</c> from the 9.4 Huntaholic*/Channel tables. Read with System.Text.Json
/// at startup like the other catalogues. Without it there is no HuntaHolic: every request answers like the
/// official server does outside its area.
/// </summary>
public class HuntaholicCatalogOptions
{
    public List<HuntaholicRow> Huntaholics { get; set; } = new();
}

/// <summary>One <c>HuntaholicResource</c> row with its channels and tiers (<c>GameContent::HUNTAHOLIC_BASE</c>).</summary>
public class HuntaholicRow
{
    public int Id { get; set; }
    public int NameId { get; set; }
    public int HuntingPeriodSeconds { get; set; }
    public int ObjectivePoint { get; set; }
    public int MaxPoint { get; set; }
    public int LobbyX { get; set; }
    public int LobbyY { get; set; }
    public HuntaholicArea LobbyArea { get; set; } = new();
    public int DungeonX { get; set; }
    public int DungeonY { get; set; }
    public HuntaholicArea DungeonArea { get; set; } = new();
    public List<HuntaholicTierRow> Tiers { get; set; } = new();
    public List<HuntaholicHealingPropTemplateRow> HealingPropTemplates { get; set; } = new();
}

/// <summary>A channel box in world units, normalised so that Left &lt;= Right and Top &lt;= Bottom.</summary>
public class HuntaholicArea
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
}

/// <summary>
/// One <c>HuntaholicInstanceResource</c> row (<c>HUNTAHOLIC_INSTANCE_BASE</c>): its id is also the lobby layer of
/// the players whose level is in <c>[MinLevel, MaxLevel)</c>.
/// </summary>
public class HuntaholicTierRow
{
    public int Id { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public double PointAdvantage { get; set; }
    public long RewardExp { get; set; }
    public int RewardJp { get; set; }
    public int SuccessItemId { get; set; }
    public int SuccessItemCount { get; set; }
    public int FailItemId { get; set; }
    public int FailItemCount { get; set; }
    public List<HuntaholicRespawnRow> Respawns { get; set; } = new();
    public List<HuntaholicHealingPropRow> HealingProps { get; set; } = new();
}

/// <summary>One <c>HuntaholicMonsterRespawnResource</c> row; the period is in seconds (the loader's × 100 is the server's).</summary>
public class HuntaholicRespawnRow
{
    public int Id { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public int MonsterId { get; set; }
    public int Count { get; set; }
    public int PeriodSeconds { get; set; }
    public bool IsWandering { get; set; }
}

/// <summary>
/// One <c>HuntaholicHealingpropResource</c> row, the <c>FIELD_PROP_RESPAWN_INFO</c> the loader builds from it
/// (<c>offset_z</c>, <c>around_*</c> rotation, <c>scale_*</c>, height lock). Posed on each room's layer when its hunt
/// begins (socle-huntaholic.md §8).
/// </summary>
public class HuntaholicHealingPropRow
{
    public int Id { get; set; }
    public int PropId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public float ZOffset { get; set; }
    public float RotateX { get; set; }
    public float RotateY { get; set; }
    public float RotateZ { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float ScaleZ { get; set; } = 1;
    public bool LockHeight { get; set; }
    public float LockHeightValue { get; set; }
}

/// <summary>
/// The <c>FieldPropResource</c> columns a healing prop needs: the skill a double-click casts, how many uses it takes
/// (<c>use_count</c>), when it comes back (<c>regen_time</c>, seconds; the loader's × 100 is the server's), its range
/// and level limits.
/// </summary>
public class HuntaholicHealingPropTemplateRow
{
    public int Id { get; set; }
    public int ActivateSkillId { get; set; }
    public int UseCount { get; set; }
    public int RegenSeconds { get; set; }
    public float CastingRange { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
}

/// <summary>
/// The auction house catalogue (<c>DevConsole/auction-catalog.73.json</c>, <c>tools/export_auction_catalog.py</c>):
/// the category rows and, for each item the 7.3 client knows, its name id, English name, group and class.
/// </summary>
public class AuctionCatalogOptions
{
    public List<AuctionCategoryRow> Categories { get; set; } = new();
    public List<AuctionItemRow> Items { get; set; } = new();
    public List<AutoAuctionRow> AutomaticAuctions { get; set; } = new();
    public int LocalFlag { get; set; } = 1;
    public string TimeZone { get; set; } = "Europe/Paris";
}

public class AutoAuctionRow
{
    public int Id { get; set; }
    public int ItemCode { get; set; }
    public string SellerName { get; set; } = "@AUCTION";
    public long Price { get; set; }
    public bool SecrouteOnly { get; set; }
    public int LocalFlag { get; set; }
    public System.DateTime EnrollmentTime { get; set; }
    public bool Repeat { get; set; }
    public int RepeatDays { get; set; }
    public byte DurationType { get; set; }
}

public class AuctionCategoryRow
{
    public int CategoryId { get; set; }
    public int SubCategoryId { get; set; }
    public int NameId { get; set; }
    public int ItemGroup { get; set; }
    public int ItemClass { get; set; }
}

public class AuctionItemRow
{
    public int Code { get; set; }
    public int NameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Group { get; set; }
    public int Class { get; set; }
}
