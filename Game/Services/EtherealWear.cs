using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>The official damage types that matter to the wear: the swing, the left hand, a skill, a damage over time.</summary>
public enum EtherealHit { Normal, LeftHand, Skill, Periodic }

/// <summary>
/// An item the wear can reach, read from the session rather than the database: a main-slot item of the player with a
/// grade and an ethereal maximum, or a belt equipment card (<c>StructItem::ProcEtherealDurabilityConsumption</c>).
/// </summary>
public readonly record struct EtherealCandidate(long ItemId, int Rank, int Grade, ItemWearType Wear, bool Weapon, bool Belt);

/// <summary>The official ethereal durability rules (<c>GameRule.cpp</c>, <c>StructPlayer/StructSummon/StructItem.cpp</c>).</summary>
public static class EtherealWearRules
{
    /// <summary>
    /// <c>(GetEtherealDurabilityConsumeRate + GetEtherealDurabilityConsumeRateByItem) × base × environment</c>, the rates
    /// in 1/10000 like <c>c_fixed10</c>, with the official integer roundings.
    /// </summary>
    public static int Consumption(int level, int jobClass, bool attacker, int damage, int rank, int grade,
        EtherealHit hit, decimal environment = 1m)
    {
        var rate = ((Math.Max(0, level) * 10L / 3 + 5) / 10 * 100)
            + ((Math.Max(0, damage) * (attacker ? 1L : 20L) + 90) / 100 * 100);
        rate += jobClass switch { 1 => attacker ? 4500 : 500, 2 => attacker ? 2000 : 1500,
            3 => attacker ? 1000 : 5000, 4 => 2500, _ => 0 };
        rate += ((Math.Max(0, rank) * (long)Math.Max(0, rank) * 10 / 2 + 9) / 10 * 500) + Math.Max(0, grade) * 5000L;
        var basis = !attacker ? 98 : hit is EtherealHit.Normal or EtherealHit.LeftHand ? 31 : 51;
        return (int)Math.Clamp(decimal.Truncate(rate * basis / 10000m * environment), 0, int.MaxValue);
    }

    /// <summary>An item whose maximum is not zero and whose durability is: it gives no stats (<c>CalculateStat</c>).</summary>
    public static bool Exhausted(ItemEntity item, IItemMatchCatalog catalog) => item.EtherealDurability <= 0
        && catalog?.TryGetFields(item.ItemResourceId, out var row) == true && row.Mix?.MaxEtherealDurability > 0;

    /// <summary><c>StructItem::AllocItem</c>: a new item starts at its resource's maximum durability and endurance.</summary>
    public static void Initialize(ItemEntity item, IItemMatchCatalog catalog)
    {
        if (catalog?.TryGetFields(item.ItemResourceId, out var row) != true || row.Mix is null) return;
        item.EtherealDurability = Math.Max(0, row.Mix.MaxEtherealDurability);
        item.Endurance = Math.Max(0, row.Mix.Endurance);
    }

    /// <summary>
    /// The player's items the wear can reach: <c>aeRightHand</c>/<c>aeLeftHand</c>/<c>aeArmors</c> (slots 0-11) with a
    /// grade, and the belt's <c>GROUP_EQUIPMENT_ON_BELT</c> cards, grade or not. An item already at 0 is left out.
    /// </summary>
    public static EtherealCandidate[] PlayerCandidates(IEnumerable<ItemEntity> items, IReadOnlyCollection<long> belt,
        IItemMatchCatalog catalog)
    {
        if (catalog is null || items is null) return Array.Empty<EtherealCandidate>();
        var result = new List<EtherealCandidate>();
        foreach (var item in items)
        {
            if (item.EtherealDurability <= 0 || !catalog.TryGetFields(item.ItemResourceId, out var fields)
                || fields.Mix is not { MaxEtherealDurability: > 0 } mix) continue;
            var onBelt = belt?.Contains(item.Id) == true && fields.Group == ItemGroup.EquipmentOnBelt;
            if (onBelt || mix.Grade > 0 && ItemWearRules.IsWornByPlayer(item) && (int)item.WearInfo is >= 0 and <= 11)
                result.Add(new EtherealCandidate(item.Id, fields.Rank, mix.Grade, item.WearInfo,
                    SkillWeaponGate.IsWeapon(fields.Class), onBelt));
        }

        return result.ToArray();
    }

    /// <summary>A summon's worn items the wear can reach (<c>StructSummon::m_anWear</c>), any slot.</summary>
    public static EtherealCandidate[] SummonCandidates(CreatureCard card, IItemMatchCatalog catalog)
    {
        if (catalog is null || card is null) return Array.Empty<EtherealCandidate>();
        var result = new List<EtherealCandidate>();
        foreach (var worn in card.Equipment)
        {
            if (worn.Exhausted || !catalog.TryGetFields(worn.ResourceId, out var fields)
                || fields.Mix is not { MaxEtherealDurability: > 0, Grade: > 0 } mix) continue;
            result.Add(new EtherealCandidate(worn.ItemId, fields.Rank, mix.Grade, (ItemWearType)worn.Slot,
                SkillWeaponGate.IsWeapon(fields.Class), false));
        }

        return result.ToArray();
    }

    /// <summary>
    /// What one hit takes from each candidate. The attacker wears its right hand (the left on a left-hand swing) and a
    /// summon its weapons; the target wears what is not a weapon, plus the belt cards.
    /// </summary>
    public static IEnumerable<(long ItemId, int Loss)> Losses(IEnumerable<EtherealCandidate> gear, bool attacker,
        EtherealHit hit, bool summon, int level, int jobClass, int damage, decimal environment)
    {
        foreach (var item in gear)
        {
            if (attacker)
            {
                if (item.Belt || !item.Weapon) continue;
                if (!summon && item.Wear != (hit == EtherealHit.LeftHand ? ItemWearType.Shield : ItemWearType.Weapon)) continue;
            }
            else if (!item.Belt && (item.Weapon || !summon && (int)item.Wear is < 1 or > 11))
            {
                continue;
            }

            var loss = Consumption(level, jobClass, attacker, damage, item.Rank, item.Grade, hit, environment);
            if (loss > 0) yield return (item.ItemId, loss);
        }
    }
}

/// <summary>Worn items changed outside an equip: effects, weapon and passives again, both stat packets and the maxima.</summary>
public static class EquipmentStatRefresh
{
    public static void Send(GameClient client, IStatService stats, IReadOnlyList<ItemEntity> carried)
    {
        if (stats is null) return;
        var info = client.ConnectionInfo;
        stats.RefreshEquipment(info, carried);
        var result = stats.Compute(info);
        if (result.Total is null) return;
        client.Connection.Send(GameStatPackets.BuildStatInfo(info.CharacterHandle, result.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(info.CharacterHandle, result.ByItem, StatInfoType.ByItem));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "max_hp", (int)result.Total.MaxHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "max_mp", (int)result.Total.MaxMp));
    }
}

public interface IEtherealWear
{
    void Hit(GameClient player, bool attacker, int damage, EtherealHit hit = EtherealHit.Normal, CreatureCard summon = null,
        bool competing = false);

    /// <summary>Completes when the losses pending for this player are written (before a save).</summary>
    Task FlushAsync(GameClient player);
}

/// <summary>
/// The wear runs on every hit, so it is judged in memory (<see cref="ConnectionInfo.EtherealGear"/>, a summon's card) and
/// the losses are summed per item; one write per player is in flight at a time, under the inventory gate shared with
/// crafts, equipment changes and repairs, and it takes everything summed meanwhile.
/// </summary>
public sealed class EtherealWear : IEtherealWear
{
    private readonly ICharacterService _characters;
    private readonly IItemMatchCatalog _items;
    private readonly IStatService _stats;
    private readonly ICreatureEvents _creatures;
    private readonly Dictionary<int, JobWearFields> _jobs;
    private readonly Dictionary<GameClient, Pending> _pending = new();
    private readonly object _lock = new();

    private sealed class Pending
    {
        public readonly Dictionary<long, int> Losses = new();
        public string Character;
        public Task Flush = Task.CompletedTask;
        public bool Running;
    }

    public EtherealWear(ICharacterService characters, IItemMatchCatalog items, IJobResourceRepository jobs,
        IStatService stats = null, ICreatureEvents creatures = null)
    {
        _characters = characters; _items = items; _stats = stats; _creatures = creatures;
        _jobs = (jobs.GetWearFields() ?? Array.Empty<JobWearFields>()).GroupBy(j => j.Job).ToDictionary(g => g.Key, g => g.First());
    }

    public void Hit(GameClient player, bool attacker, int damage, EtherealHit hit = EtherealHit.Normal, CreatureCard summon = null,
        bool competing = false)
    {
        var info = player.ConnectionInfo;
        if (damage <= 0 || info.CharacterHandle == 0) return;

        EtherealCandidate[] gear;
        if (summon is null)
        {
            gear = info.EtherealGear as EtherealCandidate[] ?? info.EtherealGear.ToArray();
        }
        else
        {
            lock (info.SummonLock) gear = EtherealWearRules.SummonCandidates(summon, _items);
        }

        if (gear.Length == 0) return;

        // The summon follows its master's situation (StructSummon::ProcEtherealDurabilityConsumption).
        var environment = competing ? 0.05m : info.PkMode || info.TurnOffPkAt != 0 ? 2m : 1m;
        var level = summon?.Level ?? info.CharacterLevel;
        var jobClass = summon is null && _jobs.TryGetValue(info.CharacterJob, out var job) && job.JobDepth != 0 ? job.JobClass : 0;
        var losses = EtherealWearRules.Losses(gear, attacker, hit, summon is not null, level, jobClass, damage, environment).ToArray();
        if (losses.Length == 0) return;

        lock (_lock)
        {
            if (!_pending.TryGetValue(player, out var pending) || pending.Character != info.CharacterName)
            {
                if (pending is { Running: true }) return; // the previous character's write still runs: drop this hit
                _pending[player] = pending = new Pending { Character = info.CharacterName };
            }

            foreach (var (id, loss) in losses)
                pending.Losses[id] = (int)Math.Min(int.MaxValue, (long)pending.Losses.GetValueOrDefault(id) + loss);
            if (!pending.Running)
            {
                pending.Running = true;
                pending.Flush = FlushLoopAsync(player, pending);
            }
        }
    }

    public Task FlushAsync(GameClient player)
    {
        lock (_lock) return _pending.TryGetValue(player, out var pending) ? pending.Flush : Task.CompletedTask;
    }

    private async Task FlushLoopAsync(GameClient player, Pending pending)
    {
        await Task.Yield();
        while (true)
        {
            Dictionary<long, int> batch;
            lock (_lock)
            {
                if (pending.Losses.Count == 0)
                {
                    pending.Running = false;
                    if (_pending.TryGetValue(player, out var current) && current == pending) _pending.Remove(player);
                    return;
                }

                batch = new Dictionary<long, int>(pending.Losses);
                pending.Losses.Clear();
            }

            await WriteAsync(player, pending.Character, batch);
        }
    }

    private async Task WriteAsync(GameClient player, string name, IReadOnlyDictionary<long, int> batch)
    {
        try
        {
            var belt = player.ConnectionInfo.BeltItemIds;
            // An item taken off between the hit and the write is no longer worn: it keeps its durability.
            var changed = await _characters.ConsumeEtherealAsync(name, item =>
                item.WearInfo != ItemWearType.None || belt.Contains(item.Id) ? batch.GetValueOrDefault(item.Id) : 0);
            if (changed.Count == 0 || player.ConnectionInfo.CharacterName != name) return;

            // SendItemMessage for each item worn down.
            foreach (var frame in GameCharacterPackets.BuildInventory(changed.ToArray())) player.Connection.Send(frame);
            if (changed.Any(item => item.EtherealDurability == 0))
            {
                // A newly exhausted item gives nothing any more: CalculateStat.
                _creatures?.EquipmentDurabilityChanged(player, changed);
                EquipmentStatRefresh.Send(player, _stats, await _characters.GetCarriedItemsAsync(name));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not persist ethereal wear for {Character}", name);
        }
    }
}
