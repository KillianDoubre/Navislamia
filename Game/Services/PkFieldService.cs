using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Maps;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IPkFieldService
{
    bool IsPkField(ConnectionInfo info);
}

public sealed class PkFieldService : IPkFieldService
{
    private readonly IMapService maps;
    private readonly IWorldLocationService locations;
    private readonly IOptionsMonitor<GameRuleOptions> rules;
    public PkFieldService(IMapService maps, IWorldLocationService locations, IOptionsMonitor<GameRuleOptions> rules)
    {
        this.maps = maps;
        this.locations = locations;
        this.rules = rules;
    }
    public static bool AllowsPk(int id, short type) => id != 110900
        && type is not (1 or 3 or 5 or 7 or 8 or 9 or 10 or 11 or 15 or 16);

    public bool IsPkField(ConnectionInfo info)
    {
        if (rules.CurrentValue.PkFieldsEverywhere) return true;
        var (x, y) = Buffs.SkillCastRangeRules.PlayerPosition(info, ServerClock.Now);
        var id = maps.GetLocationId(x, y);
        // Missing terrain/data must not turn a town into a combat field.
        return id != 0 && locations.TryGet(id, out var location) && AllowsPk(id, location.LocationType);
    }
}
