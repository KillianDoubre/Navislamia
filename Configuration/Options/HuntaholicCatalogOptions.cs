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

/// <summary>One <c>HuntaholicHealingpropResource</c> row (exported; not spawned, see socle-huntaholic.md).</summary>
public class HuntaholicHealingPropRow
{
    public int Id { get; set; }
    public int PropId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// The auction house catalogue (<c>DevConsole/auction-catalog.73.json</c>, <c>tools/export_auction_catalog.py</c>):
/// the category rows and, for each item the 7.3 client knows, its name id, English name, group and class.
/// </summary>
public class AuctionCatalogOptions
{
    public List<AuctionCategoryRow> Categories { get; set; } = new();
    public List<AuctionItemRow> Items { get; set; } = new();
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
