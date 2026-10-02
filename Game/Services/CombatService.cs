using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services;

public class CombatService : ICombatService
{
    private const int TickIntervalMs = 100;

    /// <summary>How soon an out-of-reach swing re-checks range while the client walks the player in.</summary>
    private const int RangeRetryMs = 200;
    private const int DeathAnimationSeconds = 6;

    private readonly ILogger _logger = Log.ForContext<CombatService>();
    private readonly MonsterWorldState _worldState;
    private readonly IMonsterSpawnService _spawnService;
    private readonly ILevelingService _levelingService;
    private readonly IGroundItemService _groundItemService;
    private readonly IRateService _rates;
    private readonly IPartyService _parties;
    private readonly IQuestService _quests;
    private readonly IStatService _stats;
    private readonly IStateCatalog _states;
    private readonly ICombatRandom _random;
    private readonly IPlayerVisibilityService _players;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();
    private readonly List<PendingLeave> _pendingLeaves = new();
    private readonly object _rewardLock = new();

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService,
        ILevelingService levelingService, IGroundItemService groundItemService, IRateService rates,
        IStatService stats, IStateCatalog states, IPartyService parties, IQuestService quests = null,
        ICombatRandom random = null, IPlayerVisibilityService players = null)
    {
        _parties = parties;
        _quests = quests;
        _players = players;
        _rates = rates;
        _stats = stats;
        _states = states;
        _random = random ?? CombatRandom.Shared;
        _worldState = worldState;
        _spawnService = spawnService;
        _levelingService = levelingService;
        _groundItemService = groundItemService;
        _ = RunAsync();
    }

    public void StartAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;

        // A character at 0 HP is dead (this version has no death packet, the hp value is the whole state):
        // it must not start swinging, exactly as SkillCastService refuses a cast at 0 HP.
        if (!MonsterAiRules.IsAlive(info.CharacterHp)
            || !info.TryResolveMonster(targetHandle, out var targetInstanceId)
            || !_worldState.IsAlive(targetInstanceId))
        {
            return;
        }

        lock (_lock)
        {
            _sessions[client] = new AttackSession
            {
                Client = client,
                TargetInstanceId = targetInstanceId,
                TargetHandle = targetHandle,
                AttackerHandle = info.CharacterHandle,
                NextSwingAt = DateTime.UtcNow
            };
        }
    }

    public void StopAttack(GameClient client)
    {
        AttackSession session;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(client, out session))
            {
                return;
            }

            _sessions.Remove(client);
        }

        client.Connection.Send(GameAttackPackets.BuildEndAttack(session.AttackerHandle, session.TargetHandle));
        ObserverFrames.SendMonsterFrame(_players, client, session.TargetInstanceId,
            (_, handle) => GameAttackPackets.BuildEndAttack(session.AttackerHandle, handle));
    }

    public void DropAggro(GameClient client)
    {
        _worldState.ClearAggroFor(client);
    }

    private async Task RunAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Tick(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Combat tick failed");
            }
        }
    }

    private void Tick(DateTime now)
    {
        List<AttackSession> due = null;

        lock (_lock)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.NextSwingAt <= now)
                {
                    (due ??= new List<AttackSession>()).Add(session);
                }
            }
        }

        if (due != null)
        {
            foreach (var session in due)
            {
                ProcessSwing(session, now);
            }
        }

        ProcessPendingLeaves(now);
        ProcessRespawns(now);
    }

    private void ProcessSwing(AttackSession session, DateTime now)
    {
        var client = session.Client;
        var info = client.ConnectionInfo;

        bool visible;
        lock (info.MonsterVisibilityLock)
        {
            visible = info.SpawnedMonsters.TryGetValue(session.TargetInstanceId, out var handle)
                && handle == session.TargetHandle;
        }

        // The attack session outlives the player's death, so a swing already scheduled when the killing
        // blow landed would keep hitting from a corpse: dead attackers stop here.
        if (!visible || !MonsterAiRules.IsAlive(info.CharacterHp)
            || !_worldState.IsAlive(session.TargetInstanceId)
            || !_worldState.TryGetInstance(session.TargetInstanceId, out var instance))
        {
            StopAttack(client);
            return;
        }

        // Gate the swing on the same real reach the monster attacks at, so a player cannot hit from
        // across the view while the monster cannot hit back. Out of reach, hold and re-check soon — the
        // client is walking the player in — rather than land a hit or stop the attack.
        var (monsterX, monsterY) = _worldState.GetPosition(session.TargetInstanceId);
        var reach = CombatRange.MeleeReach(instance.AttackRange, instance.Size, instance.Scale);
        if (!CombatRange.InReach(info.X, info.Y, monsterX, monsterY, reach))
        {
            session.NextSwingAt = now.AddMilliseconds(RangeRetryMs);
            return;
        }

        var stats = _stats.Compute(info).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            MonsterCombatant(session.TargetInstanceId, instance), stats.AttackPointRight, DamageKind.Physical,
            0, 0, _random);

        // A miss still lands as an attack: the monster turns on the player either way.
        var targetHp = ApplyDamage(client, session.TargetInstanceId, session.TargetHandle, hit.Damage);
        var intervalMs = IntervalMs(CombatFormulas.AttackIntervalTicks(stats.AttackSpeed));

        // Plant the player during the swing, the same rule the monster follows: a unit stands still to
        // attack. Only ever sent in reach, where the client has already stopped the player at the
        // target, so it reinforces rather than fights the client.
        client.Connection.Send(GameMovePackets.BuildStopMove(session.AttackerHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage, targetHp, info.CharacterHp,
            (byte)hit.Flags));

        // The players around see the swing too, each under its own handle for the monster: the killing one
        // carries target_hp = 0, which is what plays the monster's death on their screen.
        ObserverFrames.SendMonsterFrame(_players, client, session.TargetInstanceId,
            (_, handle) => GameAttackPackets.BuildAttackEvent(session.AttackerHandle, handle, intervalMs, intervalMs,
                GameAttackPackets.ActionAttack, hit.Damage, targetHp, info.CharacterHp, (byte)hit.Flags));

        if (targetHp <= 0)
        {
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(intervalMs);
    }

    public HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var info = client.ConnectionInfo;
        var stats = _stats.Compute(info).Total;
        return CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            MonsterCombatant(instanceId, instance), baseDamage, kind, accuracyBonus, criticalBonus, _random);
    }

    public HitResult RollMonsterHit(long instanceId, GameClient target, out uint intervalTicks)
    {
        intervalTicks = CombatFormulas.AttackIntervalTicks(100f);
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var monster = MonsterStats(instanceId, instance);
        intervalTicks = CombatFormulas.AttackIntervalTicks(monster.AttackSpeed);
        return RollMonsterHit(instance, monster, target, monster.AttackPointRight, DamageKind.Physical, 0, 0);
    }

    public HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        return RollMonsterHit(instance, MonsterStats(instanceId, instance), target, baseDamage, kind,
            accuracyBonus, criticalBonus);
    }

    public int DamagePlayer(GameClient target, int damage)
    {
        var info = target.ConnectionInfo;
        var wasAlive = MonsterAiRules.IsAlive(info.CharacterHp);
        info.CharacterHp = MonsterAiRules.PlayerHpAfterDamage(info.CharacterHp, damage);
        target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));

        if (wasAlive && !MonsterAiRules.IsAlive(info.CharacterHp))
        {
            // A dead character swings no more (the reference's onDead ends the attack), and a monster kill
            // costs experience (StructPlayer::procDecreaseEXPAndDropItem). No death packet exists here.
            StopAttack(target);
            _levelingService.ApplyDeathPenalty(target);
        }

        return info.CharacterHp;
    }

    public StatBlock GetMonsterStats(long instanceId) =>
        _worldState.TryGetInstance(instanceId, out var instance) ? MonsterStats(instanceId, instance) : null;

    private HitResult RollMonsterHit(MonsterInstance instance, StatBlock monster, GameClient target,
        float baseDamage, DamageKind kind, int accuracyBonus, int criticalBonus)
    {
        var info = target.ConnectionInfo;
        var player = _stats.Compute(info).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(monster, instance.Level),
            Combatant.From(player, info.CharacterLevel), baseDamage, kind, accuracyBonus, criticalBonus, _random);

        // /immortal: the monster still swings and the dice still roll, but nothing is lost.
        return info.IsImmortal ? hit with { Damage = 0 } : hit;
    }

    /// <summary>The swing interval in milliseconds, what <c>attack_speed</c>/<c>attack_delay</c> carry.</summary>
    internal static ushort IntervalMs(uint intervalTicks) =>
        (ushort)Math.Min(ushort.MaxValue, intervalTicks * (1000 / ServerClock.TicksPerSecond));

    private Combatant MonsterCombatant(long instanceId, MonsterInstance instance) =>
        Combatant.From(MonsterStats(instanceId, instance), instance.Level);

    /// <summary>
    /// The monster's stats with its active states folded in: a debuff now moves what it says it moves.
    /// </summary>
    private StatBlock MonsterStats(long instanceId, MonsterInstance instance)
    {
        if (instance.Combat is null)
        {
            var bare = new StatBlock();
            StatCalculator.SeedFromLevel(Math.Max(1, instance.Level), bare);
            return bare;
        }

        var states = _worldState.GetStates(instanceId);
        if (states.Count == 0)
        {
            return instance.Combat.Plain;
        }

        var effects = new List<StatEffect>();
        foreach (var state in states)
        {
            effects.AddRange(_states.Resolve(state.StateId, state.StateLevel));
        }

        return instance.Combat.Compute(effects);
    }

    public int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return 0;
        }

        var info = client.ConnectionInfo;
        var targetHp = _worldState.ApplyDamage(instanceId, damage);
        if (targetHp > 0)
        {
            // The monster fights back. Every monster retaliates, aggressive or not; the AI service
            // takes it from here (Kill clears the aggro on death below).
            _worldState.SetAggro(instanceId, client);
            return targetHp;
        }

        var now = DateTime.UtcNow;
        if (!_worldState.TryKill(instanceId, now + _rates.MonsterRespawnDelay)) return 0;

        // A corpse keeps no debuff, and a respawn must not inherit one either.
        foreach (var state in _worldState.ClearStates(instanceId))
        {
            client.Connection.Send(GameSkillPackets.BuildStateRemoval(targetHandle, state.StateHandle,
                (uint)state.StateId));
        }

        client.Connection.Send(GameMovePackets.BuildStopMove(targetHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));
        client.Connection.Send(GameCharacterPackets.BuildStatusChange(targetHandle, ActorStatus.ForMonster(true)));

        // The players who watched the kill see the corpse fall and later leave, like the killer: otherwise a
        // dead monster stood on their screen until their next synchronisation.
        var watchers = new List<PendingLeave>();
        ObserverFrames.SendMonsterFrame(_players, client, instanceId, (observer, handle) =>
        {
            watchers.Add(new PendingLeave
            {
                Client = observer,
                InstanceId = instanceId,
                Handle = handle,
                LeaveAt = now.AddSeconds(DeathAnimationSeconds)
            });
            return GameCharacterPackets.BuildStatusChange(handle, ActorStatus.ForMonster(true));
        });

        lock (_lock)
        {
            _lastAttacker[instanceId] = client;
            _sessions.Remove(client);
            _pendingLeaves.Add(new PendingLeave
            {
                Client = client,
                InstanceId = instanceId,
                Handle = targetHandle,
                LeaveAt = now.AddSeconds(DeathAnimationSeconds)
            });
            _pendingLeaves.AddRange(watchers);
        }

        var (dropX, dropY) = _worldState.GetPosition(instanceId);
        _groundItemService.DropForMonster(client, instance.MonsterId, dropX, dropY, instance.Z, instanceId);
        AwardKill(client, instance, instanceId, dropX, dropY, info.Layer);
        if (_quests is not null)
            foreach (var member in _parties.RewardMembers(client, dropX, dropY, info.Layer))
                _ = _quests.OnMonsterKilledAsync(member, instance.MonsterId, dropX, dropY, instance.Z);
        return targetHp;
    }

    /// <summary>
    /// What a kill gives its beneficiaries, taken from the monster's own resource row rather than from a
    /// formula (docs/packet-specs/socle-recompenses-monstres.md §9.1): the row's experience and JP, a gold
    /// roll and a chaos roll, each behind its per-cent chance. Gold and chaos are drawn <b>once</b> for the
    /// kill, the way the official <c>procDropGold</c> / <c>procDropChaos</c> (<c>0x1400b3ac0</c>,
    /// <c>0x1400b7e20</c>) do, and then shared like the rest of the loot; the rates are applied on the way
    /// out, after the sharing, and the two level-gap malus rules of <see cref="MonsterRewardRules"/> after
    /// the per-beneficiary split for experience and JP, before it for gold and chaos.
    /// </summary>
    private void AwardKill(GameClient killer, MonsterInstance monster, long instanceId, float x, float y,
        byte layer)
    {
        var members = _parties.RewardMembers(killer, x, y, layer);
        if (members.Count == 0) members = new[] { killer };

        var highestLevel = 0;
        foreach (var member in members)
        {
            highestLevel = Math.Max(highestLevel, member.ConnectionInfo.CharacterLevel);
        }

        var rewards = monster.Rewards;
        var loot = MonsterRewardRules.LootFactor(monster.Level, highestLevel);
        var goldChance = rewards.GoldDropPercentage * _rates.Get(RateType.Gold) * loot;
        var chaosChance = rewards.ChaosDropPercentage * _rates.Get(RateType.Chaos) * loot;

        long goldRoll;
        long chaosRoll;
        lock (_rewardLock)
        {
            goldRoll = MonsterRewardRules.RollsChance(goldChance, _random)
                ? MonsterRewardRules.RollAmount(rewards.GoldMin, rewards.GoldMax, _random)
                : 0;
            chaosRoll = MonsterRewardRules.RollsChance(chaosChance, _random)
                ? MonsterRewardRules.RollAmount(rewards.ChaosMin, rewards.ChaosMax, _random)
                : 0;
        }

        var gold = _rates.Scale(goldRoll, RateType.Gold);

        for (var i = 0; i < members.Count; i++)
        {
            var client = members[i];
            var info = client.ConnectionInfo;

            // The official grants experience and chaos within 500 units of the corpse only, and the party
            // shares keep their denominator: an out-of-reach member takes nothing, not a bigger slice.
            if (!MonsterRewardRules.WithinRewardRange(info.X, info.Y, x, y))
            {
                continue;
            }

            var expShare = rewards.Exp / members.Count + (i < rewards.Exp % members.Count ? 1 : 0);
            var jpShare = rewards.Jp / members.Count + (i < rewards.Jp % members.Count ? 1 : 0);
            info.CharacterExp += _rates.Scale(
                MonsterRewardRules.ScaleForLevelGap(expShare, monster.Level, info.CharacterLevel),
                RateType.Exp);
            info.CharacterJp += _rates.Scale(
                MonsterRewardRules.ScaleForLevelGap(jpShare, monster.Level, info.CharacterLevel),
                RateType.Jp);
            info.AddGold(gold / members.Count + (i < gold % members.Count ? 1 : 0));
            client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle,
                info.CharacterExp, info.CharacterJp));
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold,
                info.CharacterChaos));

            var chaosShare = (int)(chaosRoll / members.Count + (i < chaosRoll % members.Count ? 1 : 0));
            if (chaosShare > 0)
            {
                info.CharacterChaos += chaosShare;
                client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "chaos",
                    info.CharacterChaos));
                SendChaosGain(killer, client, instanceId, chaosShare);
            }

            _levelingService.ApplyExperience(client);
        }
    }

    /// <summary>
    /// <c>TM_SC_GET_CHAOS</c> (213): the official broadcasts it to the whole region mesh around the corpse
    /// (<c>ArcadiaServer::Broadcast</c> from <c>0x1400b6f1d</c>). Here that set is the clients that stream the
    /// corpse — the killer's witnesses plus the two clients that are always at the corpse, the killer and the
    /// beneficiary — each with its own handle for the monster, since the handle differs per client. A client
    /// that does not stream the corpse cannot place the frame and gets nothing, so every client receives the
    /// gain exactly once.
    /// </summary>
    private void SendChaosGain(GameClient killer, GameClient beneficiary, long instanceId, int chaos)
    {
        var playerHandle = beneficiary.ConnectionInfo.CharacterHandle;
        ObserverFrames.SendMonsterFrame(_players, killer, instanceId,
            (observer, corpseHandle) => ReferenceEquals(observer, beneficiary)
                ? null
                : GameRewardPackets.BuildGetChaos(playerHandle, corpseHandle, chaos));

        var own = beneficiary.ConnectionInfo.GetMonsterHandle(instanceId);
        if (own != 0)
        {
            beneficiary.Connection.Send(GameRewardPackets.BuildGetChaos(playerHandle, own, chaos));
        }
    }

    private void ProcessPendingLeaves(DateTime now)
    {
        List<PendingLeave> due = null;

        lock (_lock)
        {
            for (var i = _pendingLeaves.Count - 1; i >= 0; i--)
            {
                if (_pendingLeaves[i].LeaveAt <= now)
                {
                    (due ??= new List<PendingLeave>()).Add(_pendingLeaves[i]);
                    _pendingLeaves.RemoveAt(i);
                }
            }
        }

        if (due == null)
        {
            return;
        }

        foreach (var leave in due)
        {
            var info = leave.Client.ConnectionInfo;
            lock (info.MonsterVisibilityLock)
            {
                if (info.SpawnedMonsters.TryGetValue(leave.InstanceId, out var handle) && handle == leave.Handle)
                {
                    leave.Client.Connection.Send(GameSpawnPackets.BuildLeave(leave.Handle));
                    info.SpawnedMonsters.Remove(leave.InstanceId);
                }
            }
        }
    }

    private void ProcessRespawns(DateTime now)
    {
        var respawned = _worldState.CollectRespawns(now);

        foreach (var instanceId in respawned)
        {
            GameClient attacker;

            lock (_lock)
            {
                _lastAttacker.TryGetValue(instanceId, out attacker);
                _lastAttacker.Remove(instanceId);
            }

            if (attacker != null)
            {
                _spawnService.Sync(attacker);
            }
        }
    }

    private sealed class AttackSession
    {
        public GameClient Client;
        public long TargetInstanceId;
        public uint TargetHandle;
        public uint AttackerHandle;
        public DateTime NextSwingAt;
    }

    private sealed class PendingLeave
    {
        public GameClient Client;
        public long InstanceId;
        public uint Handle;
        public DateTime LeaveAt;
    }
}
