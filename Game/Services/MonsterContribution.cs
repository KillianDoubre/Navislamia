using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public sealed record MonsterDamageLedger(IReadOnlyList<(GameClient Player, long Damage)> Damage,
    GameClient FirstAttacker, uint FirstTick);

public sealed record MonsterRewardGroup(GameClient Representative, double Factor);

public static class MonsterContribution
{
    private static (long? Party, uint Solo) Group(GameClient player) =>
        (player.ConnectionInfo.PartyId, player.ConnectionInfo.PartyId.HasValue ? 0 : player.ConnectionInfo.CharacterHandle);

    // Retail calcPartyContribute: 50% damage, 30% first hit, 10% largest share, 10% last hit.
    // After one minute the first-hit bonus moves to the largest damage group.
    public static IReadOnlyList<MonsterRewardGroup> Resolve(MonsterDamageLedger ledger, GameClient killer, uint now)
    {
        if (ledger is null || ledger.Damage.Count == 0)
            return new[] { new MonsterRewardGroup(killer, 1) };
        var total = ledger.Damage.Sum(d => (decimal)d.Damage);
        if (total <= 0) return Array.Empty<MonsterRewardGroup>();
        var groups = ledger.Damage.Where(d => d.Player.ConnectionInfo.CharacterHandle != 0 && d.Damage > 0)
            .GroupBy(d => Group(d.Player)).Select(g => new
            {
                Key = g.Key, Player = g.First().Player, Damage = g.Sum(d => (decimal)d.Damage)
            }).OrderByDescending(g => g.Damage).ToArray();
        var firstExpired = unchecked((int)(now - ledger.FirstTick)) > 6000;
        var first = Group(ledger.FirstAttacker);
        var last = Group(killer);
        return groups.Select((g, index) => new MonsterRewardGroup(g.Player,
            (double)(g.Damage / total) * .5 + (index == 0 ? firstExpired ? .4 : .1 : 0)
            + (!firstExpired && g.Key == first ? .3 : 0) + (g.Key == last ? .1 : 0))).ToArray();
    }

    public static long Scale(long amount, double factor) => amount <= 0 || !double.IsFinite(factor) || factor <= 0
        ? 0 : (long)Math.Min(long.MaxValue, Math.Floor((decimal)amount * (decimal)factor));
}
