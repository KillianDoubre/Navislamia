using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Where a hit or a death tells the cast in progress (StructSkill::onDamage, CancelSkill).</summary>
    private readonly Casting.ICastInterrupts _casts;

    /// <summary>The PK server's items lost on death (procDecreaseEXPAndDropItem).</summary>
    private readonly Death.IDeathDropService _deathDrops;
    private readonly IStateCatalog _states;

    /// <summary>Where an arrow is spent.</summary>
    private readonly ICharacterService _characters;

    /// <summary>The duel, which makes two players enemies (StructPlayer::IsEnemy).</summary>
    private readonly Compete.ICompeteService _compete;

    /// <summary>The taming window and draw (docs/packet-specs/socle-apprivoisement-invocation.md §15).</summary>
    private readonly Creatures.ICreatureEvents _creatures;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Navislamia.Configuration.Options.GameRuleOptions> _rules;
    private readonly ICombatRandom _random;
    private readonly IPlayerVisibilityService _players;
    private readonly IPkFieldService _pkFields;
    private readonly Progression.ITitleService _titles;
    private readonly Guilds.GuildRuntime _guilds;
    private readonly Guilds.GuildCombatEvents _guildEvents;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();
    private readonly List<PendingLeave> _pendingLeaves = new();
    private readonly Huntaholic.IHuntaholicEvents _huntaholic;
    private readonly Dungeons.DungeonEvents _dungeons;
    private readonly IEtherealWear _ethereal;

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService,
        ILevelingService levelingService, IGroundItemService groundItemService, IRateService rates,
        IStatService stats, IStateCatalog states, IPartyService parties, IQuestService quests = null,
        ICombatRandom random = null, IPlayerVisibilityService players = null,
        Casting.ICastInterrupts casts = null, Death.IDeathDropService deathDrops = null,
        ICharacterService characters = null, Compete.ICompeteService compete = null,
        Microsoft.Extensions.Options.IOptionsMonitor<Navislamia.Configuration.Options.GameRuleOptions> rules = null,
        bool runTicks = true, IPkFieldService pkFields = null, Progression.ITitleService titles = null,
        Creatures.ICreatureEvents creatures = null, Guilds.GuildRuntime guilds = null, Guilds.GuildCombatEvents guildEvents = null,
        Huntaholic.IHuntaholicEvents huntaholic = null, Dungeons.DungeonEvents dungeons = null, IEtherealWear ethereal = null)
    {
        _ethereal = ethereal;
        _dungeons = dungeons;
        _huntaholic = huntaholic;
        _guilds = guilds; _guildEvents = guildEvents;
        _creatures = creatures;
        _pkFields = pkFields;
        _titles = titles;
        _compete = compete;
        _rules = rules;
        _characters = characters;
        _deathDrops = deathDrops;
        _casts = casts;
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
        if (runTicks) _ = RunAsync();
    }

    public void StartAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;

        // A character at 0 HP is dead (this version has no death packet, the hp value is the whole state):
        // it must not start swinging, exactly as SkillCastService refuses a cast at 0 HP.
        if (!MonsterAiRules.IsAlive(info.CharacterHp) || Creatures.SummonFall.IsActive(info, ServerClock.Now))
        {
            return;
        }

        if (!info.TryResolveMonster(targetHandle, out var targetInstanceId))
        {
            StartPlayerAttack(client, targetHandle);
            return;
        }

        if (!_worldState.IsAlive(targetInstanceId))
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
        if (session.TargetPlayer is not null)
        {
            ProcessPlayerSwing(session, now);
            return;
        }

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
        if (!visible || !MonsterAiRules.IsAlive(info.CharacterHp) || Creatures.SummonFall.IsActive(info, ServerClock.Now)
            || !_worldState.IsAlive(session.TargetInstanceId)
            || !_worldState.TryGetInstance(session.TargetInstanceId, out var instance))
        {
            StopAttack(client);
            return;
        }

        var stats = _stats.Compute(info).Total;

        // processAttack measures the player's own reach, its weapon's range plus both bodies (×1.2, ×1.5 on a
        // walking target). Out of reach, hold and re-check soon — the client is walking the player in.
        var (monsterX, monsterY) = _worldState.GetPosition(session.TargetInstanceId);
        var (playerX, playerY) = info.PositionAt(ServerClock.Now);
        if (!Casting.CastRules.InRange(Casting.CastRules.WeaponRange, stats.AttackRange, playerX, playerY,
                CombatRange.PlayerUnitSize, monsterX, monsterY, CombatRange.UnitSize(instance.Size, instance.Scale),
                _worldState.IsMoving(session.TargetInstanceId)))
        {
            session.NextSwingAt = now.AddMilliseconds(RangeRetryMs);
            return;
        }

        var weapon = info.EquippedWeapon;
        var ranged = Combat.AttackMechanics.IsRanged(weapon);
        var intervalTicks = CombatFormulas.AttackIntervalTicks(stats.AttackSpeed);

        // A bow shoots in two halves (processAttack): it aims for 0.8 of the interval, then shoots for the
        // remaining 0.2, one arrow per shot from the shield slot. No arrow, no attack.
        if (ranged)
        {
            if (info.LeftHand is not { WeaponType: null } arrows || arrows.Amount < 1)
            {
                StopAttack(client);
                return;
            }

            if (!session.Aimed)
            {
                session.Aimed = true;
                var aimMs = IntervalMs((uint)(intervalTicks * Combat.AttackMechanics.AimShare));
                var aimFlag = Combat.AttackMechanics.AttackFlag(false, false, weapon);
                client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle,
                    session.TargetHandle, aimMs, aimMs, Combat.AttackMechanics.ActionAiming, aimFlag,
                    Array.Empty<AttackHit>(), info.CharacterHp));
                ObserverFrames.SendMonsterFrame(_players, client, session.TargetInstanceId,
                    (_, handle) => GameAttackPackets.BuildAttackEvent(session.AttackerHandle, handle, aimMs, aimMs,
                        Combat.AttackMechanics.ActionAiming, aimFlag, Array.Empty<AttackHit>(), info.CharacterHp));
                session.NextSwingAt = now.AddMilliseconds(aimMs);
                return;
            }

            session.Aimed = false;
            ConsumeArrow(client, arrows);
        }

        var states = PlayerStates(info);
        var dualWield = Combat.AttackMechanics.IsDualWield(weapon, info.LeftHand?.WeaponType);
        var doubleAttack = _random.Next(100) + 1 < Combat.AttackMechanics.DoubleAttackRatio(states, weapon);
        var count = Combat.AttackMechanics.HitCount(dualWield, doubleAttack);
        var extras = Combat.AttackMechanics.AdditionalDamages(states, ranged);
        var (leftAttack, leftAccuracy) = dualWield
            ? Combat.AttackMechanics.LeftHand(stats, info.RightWeaponEffects, info.LeftHand?.Effects)
            : (0f, 0f);

        var monster = MonsterCombatant(session.TargetInstanceId, instance);
        var hits = new List<AttackHit>(count);
        var targetHp = 1;
        for (var i = 0; i < count && targetHp > 0; i++)
        {
            // The odd hits of a dual wielder are the left hand's (Unit::Attack: bLeftHandAttack = i % 2).
            var left = dualWield && i % 2 == 1;
            var attacker = Combatant.From(stats, info.CharacterLevel);
            if (left)
            {
                attacker = attacker with { AttackPoint = leftAttack, Accuracy = leftAccuracy };
            }

            var hit = CombatFormulas.Resolve(attacker, monster, attacker.AttackPoint, DamageKind.Physical, 0, 0,
                _random);

            // DealPhysicalNormalDamage: every additional damage rolls on a hit that landed, adding to the hit and to
            // its element's share.
            var damage = hit.Damage;
            int[] elemental = null;
            if ((hit.Flags & (HitFlags.Miss | HitFlags.PerfectBlock)) == 0)
            {
                foreach (var extra in extras)
                {
                    if (extra.Ratio > _random.Next(100) + 1)
                    {
                        var amount = CombatFormulas.ResistedDamage(Combat.AttackMechanics.AdditionalAmount(extra, hit.Damage),
                            monster.Resistances?.GetResistance(extra.Element) ?? 0f);
                        damage += amount;
                        (elemental ??= new int[Combat.AttackMechanics.Elements])[extra.Element] += amount;
                    }
                }
            }

            // A miss still lands as an attack: the monster turns on the player either way.
            targetHp = ApplyDamage(client, session.TargetInstanceId, session.TargetHandle, damage);
            _ethereal?.Hit(client, true, hit.Damage, left ? EtherealHit.LeftHand : EtherealHit.Normal);
            hits.Add(new AttackHit(damage, (byte)hit.Flags, targetHp, elemental));
        }

        var swingTicks = ranged ? (uint)(intervalTicks * (1f - Combat.AttackMechanics.AimShare)) : intervalTicks;
        var intervalMs = IntervalMs(swingTicks);
        var flag = Combat.AttackMechanics.AttackFlag(doubleAttack, dualWield, weapon);

        // Plant the player during the swing, the same rule the monster follows: a unit stands still to
        // attack. Only ever sent in reach, where the client has already stopped the player at the
        // target, so it reinforces rather than fights the client.
        client.Connection.Send(GameMovePackets.BuildStopMove(session.AttackerHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            intervalMs, intervalMs, GameAttackPackets.ActionAttack, flag, hits, info.CharacterHp));

        // The players around see the swing too, each under its own handle for the monster: the killing one
        // carries target_hp = 0, which is what plays the monster's death on their screen.
        ObserverFrames.SendMonsterFrame(_players, client, session.TargetInstanceId,
            (_, handle) => GameAttackPackets.BuildAttackEvent(session.AttackerHandle, handle, intervalMs, intervalMs,
                GameAttackPackets.ActionAttack, flag, hits, info.CharacterHp));

        if (targetHp <= 0)
        {
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(intervalMs);
    }

    /// <summary>The player's active states with their rules, as the attack reads them.</summary>
    private List<Combat.ActiveStateRule> PlayerStates(ConnectionInfo info)
    {
        Buffs.ActiveBuff[] active;
        lock (info.BuffLock)
        {
            active = info.ActiveBuffs.ToArray();
        }

        var states = new List<Combat.ActiveStateRule>(active.Length);
        foreach (var buff in active)
        {
            states.Add(new Combat.ActiveStateRule(_states.GetRule(buff.StateId), buff.StateLevel));
        }

        return states;
    }

    /// <summary>One arrow per shot (Player::EraseBullet): the stack update follows, the slot empties at the last one.</summary>
    private void ConsumeArrow(GameClient client, Combat.LeftHandItem arrows)
    {
        arrows.Amount = Math.Max(0, arrows.Amount - 1);
        if (arrows.Amount == 0)
        {
            client.ConnectionInfo.LeftHand = null;
        }

        if (_characters is null)
        {
            return;
        }

        var info = client.ConnectionInfo;
        _ = Task.Run(async () =>
        {
            try
            {
                var remaining = await _characters.ConsumeItemAsync(info.CharacterName, arrows.Handle, 1);
                if (remaining is { } left)
                {
                    client.Connection.Send(left == 0
                        ? GameCharacterPackets.BuildDestroyItem(arrows.Handle)
                        : GameCharacterPackets.BuildUpdateItemCount(arrows.Handle, left));
                }
            }
            catch (Exception exception)
            {
                Serilog.Log.ForContext<CombatService>().Error(exception, "Could not spend an arrow of {clientTag}",
                    client.ClientTag);
            }
        });
    }

    public HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus) => RollHit(client, instanceId, baseDamage, kind, accuracyBonus, criticalBonus, 0);

    public HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus, int element)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var info = client.ConnectionInfo;
        var stats = _stats.Compute(info).Total;
        return CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            MonsterCombatant(instanceId, instance), baseDamage, kind, accuracyBonus, criticalBonus, _random, element);
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

    public HitResult RollMonsterHitOn(long instanceId, StatBlock defender, int defenderLevel, out uint intervalTicks)
    {
        intervalTicks = CombatFormulas.AttackIntervalTicks(100f);
        if (defender is null || !_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var monster = MonsterStats(instanceId, instance);
        intervalTicks = CombatFormulas.AttackIntervalTicks(monster.AttackSpeed);
        return CombatFormulas.Resolve(Combatant.From(monster, instance.Level),
            Combatant.From(defender, Math.Max(1, defenderLevel)), monster.AttackPointRight, DamageKind.Physical, 0, 0,
            _random);
    }

    public HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus) => RollMonsterHit(instanceId, target, baseDamage, kind, accuracyBonus, criticalBonus, 0);

    public HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus, int element)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        return RollMonsterHit(instance, MonsterStats(instanceId, instance), target, baseDamage, kind,
            accuracyBonus, criticalBonus, element);
    }

    public int DamagePlayer(GameClient target, int damage)
    {
        var info = target.ConnectionInfo;
        var wasAlive = MonsterAiRules.IsAlive(info.CharacterHp);
        if (wasAlive) _ethereal?.Hit(target, false, damage);
        info.CharacterHp = MonsterAiRules.PlayerHpAfterDamage(info.CharacterHp, damage);
        target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
        if (wasAlive)
        {
            // A rider hit, or killed, can fall off its summon (StructPlayer::onDamage / onDead).
            _creatures?.PlayerDamaged(target, damage, !MonsterAiRules.IsAlive(info.CharacterHp));
        }

        if (wasAlive && !MonsterAiRules.IsAlive(info.CharacterHp))
        {
            // A dead character swings no more (the reference's onDead ends the attack), and a monster kill
            // costs experience (StructPlayer::procDecreaseEXPAndDropItem). No death packet exists here.
            StopAttack(target);
            _casts?.Interrupt(target);
            _levelingService.ApplyDeathPenalty(target);
            if (info.ImmoralPoint > 0m) MoralityRules.Set(target, MoralityRules.AfterDeath(info.ImmoralPoint, info.PkCount));
            if (_deathDrops is not null)
            {
                _ = _deathDrops.DropOnDeathAsync(target);
            }
        }
        else if (wasAlive && damage > 0)
        {
            _casts?.Damaged(target, damage);
        }

        return info.CharacterHp;
    }

    /// <summary>
    /// <c>Unit::DealDamage</c>'s mana shield (the MP take their share of the hit, never more than they hold) and
    /// <c>ReflectDamage</c>'s reflections (each rolls its chance and sends a flat amount or a share of the hit
    /// back to the monster), around the ordinary damage (docs/packet-specs/socle-mecaniques-combat.md §4).
    /// </summary>
    public int DamagePlayer(GameClient target, int damage, long attackerInstanceId, bool magical)
    {
        var info = target.ConnectionInfo;
        var states = PlayerStates(info);

        var shield = Combat.AttackMechanics.ManaShieldRatio(states, magical);
        if (shield > 0f && damage > 0 && MonsterAiRules.IsAlive(info.CharacterHp))
        {
            var absorbed = Combat.AttackMechanics.ManaShieldAbsorb(damage, shield, info.CharacterMp);
            if (absorbed > 0)
            {
                damage -= absorbed;
                info.CharacterMp -= absorbed;
                target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
            }
        }

        var hp = DamagePlayer(target, damage);
        if (damage > 0)
        {
            _compete?.OnDamagedByOther(target, null);
        }

        if (hp <= 0 || damage <= 0 || attackerInstanceId < 0)
        {
            return hp;
        }

        foreach (var reflect in Combat.AttackMechanics.Reflects(states))
        {
            if (reflect.Ratio <= _random.Next(100) + 1)
            {
                continue;
            }

            var amount = CombatFormulas.ResistedDamage(Combat.AttackMechanics.ReflectAmount(reflect, damage, magical),
                GetMonsterStats(attackerInstanceId)?.GetResistance(reflect.Element) ?? 0f);
            var handle = info.GetMonsterHandle(attackerInstanceId);
            if (amount > 0 && handle != 0 && _worldState.IsAlive(attackerInstanceId))
            {
                ApplyDamage(target, attackerInstanceId, handle, amount);
            }
        }

        return hp;
    }

    /// <summary>
    /// <c>StructPlayer::IsEnemy</c> between two players (2012-11 <c>0x1400e3210</c>): the two sides of a started duel
    /// always; otherwise both must be in a PK field, never within a party or a guild. The target must be
    /// PK-on or criminal, or the attacker must be PK-on.
    /// </summary>
    public static bool IsPlayerEnemy(ConnectionInfo attacker, ConnectionInfo target, bool competing, bool pkField)
    {
        if (competing)
        {
            return true;
        }

        if (!pkField)
        {
            return false;
        }

        if (attacker.PartyId is { } party && party == target.PartyId
            || attacker.GuildId is { } guild && guild != 0 && guild == target.GuildId
            || Guilds.GuildRules.SameAlliance(attacker, target))
        {
            return false;
        }

        return target.PkMode || attacker.PkMode || target.ImmoralPoint >= MoralityRules.BloodyLimit;
    }

    public bool ArePlayerEnemies(GameClient attacker, GameClient target) =>
        !ReferenceEquals(attacker, target)
        && attacker.ConnectionInfo.Layer == target.ConnectionInfo.Layer
        && !target.ConnectionInfo.IsImmortal
        && (_guilds?.WarEnemy(attacker.ConnectionInfo, target.ConnectionInfo) == true
            || _guilds?.Siege(attacker.ConnectionInfo) is null && IsPlayerEnemy(attacker.ConnectionInfo, target.ConnectionInfo, _compete?.AreCompeting(attacker, target) == true,
            _pkFields is not null ? _pkFields.IsPkField(attacker.ConnectionInfo) && _pkFields.IsPkField(target.ConnectionInfo)
                : _rules?.CurrentValue?.PkFieldsEverywhere == true));

    public void OnPkEnabled(GameClient client)
    {
        if (_rules?.CurrentValue?.PkServer != true) MoralityRules.Add(client, 5m);
    }

    private int PvpDamage(int damage) => (int)Math.Min(int.MaxValue,
        Math.Max(0m, damage * Math.Clamp(_rules?.CurrentValue?.PvpDamageRate ?? .05m, 0m, 1m)));

    public HitResult RollPlayerHit(GameClient attacker, GameClient target, float damage, DamageKind kind,
        int accuracy, int critical, int element = 0)
    {
        if (!ArePlayerEnemies(attacker, target) || target.ConnectionInfo.CharacterHp <= 0)
            return new HitResult(0, HitFlags.Miss);
        var hit = CombatFormulas.Resolve(Combatant.From(_stats.Compute(attacker.ConnectionInfo).Total, attacker.ConnectionInfo.CharacterLevel),
            Combatant.From(_stats.Compute(target.ConnectionInfo).Total, target.ConnectionInfo.CharacterLevel),
            damage, kind, accuracy, critical, _random, element);
        return hit with { Damage = PvpDamage(hit.Damage) };
    }

    private bool IsEnemy(GameClient attacker, GameClient target) => ArePlayerEnemies(attacker, target);
    public HitResult RollSummonHitOnPlayer(GameClient master, GameClient target, StatBlock stats, int level,
        float damage, DamageKind kind, int accuracy, int critical, int element = 0)
    {
        if (!ArePlayerEnemies(master, target) || target.ConnectionInfo.CharacterHp <= 0)
            return new HitResult(0, HitFlags.Miss);
        var hit = CombatFormulas.Resolve(Combatant.From(stats, level),
            Combatant.From(_stats.Compute(target.ConnectionInfo).Total, target.ConnectionInfo.CharacterLevel),
            damage, kind, accuracy, critical, _random, element);
        return hit with { Damage = PvpDamage(hit.Damage) };
    }

    private void StartPlayerAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;
        bool seen;
        lock (info.PlayerVisibilityLock)
        {
            seen = info.SpawnedPlayers.ContainsKey(targetHandle);
        }

        if (!seen || _players is null || !_players.Registry.TryResolve(targetHandle, out var target)
            || !MonsterAiRules.IsAlive(target.ConnectionInfo.CharacterHp) || !IsEnemy(client, target))
        {
            return;
        }

        lock (_lock)
        {
            _sessions[client] = new AttackSession
            {
                Client = client,
                TargetInstanceId = -1,
                TargetHandle = targetHandle,
                AttackerHandle = info.CharacterHandle,
                NextSwingAt = DateTime.UtcNow,
                TargetPlayer = target
            };
        }
    }

    /// <summary>
    /// A swing at a player: the same official rule as at a monster (<see cref="CombatFormulas.Resolve"/>, the target's
    /// own stats), measured from the attacker's weapon range, sent to the attacker and everyone who sees it.
    /// </summary>
    private void ProcessPlayerSwing(AttackSession session, DateTime now)
    {
        var client = session.Client;
        var target = session.TargetPlayer;
        var info = client.ConnectionInfo;
        var targetInfo = target.ConnectionInfo;

        bool seen;
        lock (info.PlayerVisibilityLock)
        {
            seen = info.SpawnedPlayers.ContainsKey(session.TargetHandle);
        }

        if (!seen || _players?.Registry is not null &&
            (!_players.Registry.TryResolve(session.TargetHandle, out var current) || !ReferenceEquals(current, target))
            || !MonsterAiRules.IsAlive(info.CharacterHp) || !MonsterAiRules.IsAlive(targetInfo.CharacterHp)
            || !IsEnemy(client, target))
        {
            StopAttack(client);
            return;
        }

        var stats = _stats.Compute(info).Total;
        var nowTick = ServerClock.Now;
        var (ax, ay) = Buffs.SkillCastRangeRules.PlayerPosition(info, nowTick);
        var (tx, ty) = Buffs.SkillCastRangeRules.PlayerPosition(targetInfo, nowTick);
        if (!Casting.CastRules.InRange(Casting.CastRules.WeaponRange, stats.AttackRange, ax, ay,
                CombatRange.PlayerUnitSize, tx, ty, CombatRange.PlayerUnitSize, false))
        {
            session.NextSwingAt = now.AddMilliseconds(RangeRetryMs);
            return;
        }

        var defender = _stats.Compute(targetInfo).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            Combatant.From(defender, targetInfo.CharacterLevel), stats.AttackPointRight, DamageKind.Physical, 0, 0, _random);
        var damage = PvpDamage(hit.Damage);
        int[] elemental = null;
        if ((hit.Flags & (HitFlags.Miss | HitFlags.PerfectBlock)) == 0)
            foreach (var extra in Combat.AttackMechanics.AdditionalDamages(PlayerStates(info), Combat.AttackMechanics.IsRanged(info.EquippedWeapon)))
                if (extra.Ratio > _random.Next(100) + 1)
                {
                    var amount = PvpDamage(CombatFormulas.ResistedDamage(Combat.AttackMechanics.AdditionalAmount(extra, hit.Damage),
                        defender.GetResistance(extra.Element)));
                    damage = (int)Math.Min(int.MaxValue, (long)damage + amount);
                    (elemental ??= new int[Combat.AttackMechanics.Elements])[extra.Element] += amount;
                }
        var targetHp = DamagePlayerByPlayer(client, target, damage);
        _ethereal?.Hit(client, true, hit.Damage, competing: _compete?.AreCompeting(client, target) == true);

        var intervalMs = IntervalMs(CombatFormulas.AttackIntervalTicks(stats.AttackSpeed));
        var frame = GameAttackPackets.BuildAttackEvent(info.CharacterHandle, targetInfo.CharacterHandle, intervalMs,
            intervalMs, GameAttackPackets.ActionAttack, Combat.AttackMechanics.AttackFlag(false, false, info.EquippedWeapon),
            new[] { new AttackHit(damage, (byte)hit.Flags, targetHp, elemental) }, info.CharacterHp);
        if (_players is not null)
        {
            _players.SendToObservers(client, frame, includeSelf: true);
        }
        else
        {
            client.Connection.Send(frame);
            target.Connection.Send(frame);
        }

        if (targetHp <= 0)
        {
            StopAttack(client);
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(intervalMs);
    }

    /// <summary>
    /// Damage a player deals to another: HP (509 to observers), the cast it disturbs, and on the killing blow the end
    /// of the duel (the loser keeps its experience) or, outside a duel, the death penalty of a PK server only — a
    /// player kill costs nothing elsewhere (<c>procDecreaseEXPAndDropItem</c>).
    /// </summary>
    public int DamagePlayerByPlayer(GameClient attacker, GameClient target, int damage, bool magical = false) =>
        LandPlayerDamage(attacker, target, damage, magical, true);
    public int DamagePlayerBySummon(GameClient master, GameClient target, uint summonHandle, int damage, bool magical = false)
    {
        var summon = Array.Find(master.ConnectionInfo.Summons, s => s.Handle == summonHandle);
        return summon is not { Hp: > 0 } ? target.ConnectionInfo.CharacterHp
            : LandPlayerDamage(master, target, damage, magical, true, summonHandle);
    }

    private int LandPlayerDamage(GameClient attacker, GameClient target, int damage, bool magical, bool reflect, uint summonHandle = 0)
    {
        var info = target.ConnectionInfo;
        decimal victimImmorality;
        if (!ArePlayerEnemies(attacker, target)) return info.CharacterHp;
        var states = PlayerStates(info);
        bool wasAlive, killed;
        lock (info.ProgressLock)
        {
            wasAlive = info.CharacterHp > 0;
            if (!wasAlive) return info.CharacterHp;
            victimImmorality = info.ImmoralPoint;
            var shield = Combat.AttackMechanics.ManaShieldAbsorb(damage,
                Combat.AttackMechanics.ManaShieldRatio(states, magical), info.CharacterMp);
            info.CharacterMp -= shield;
            damage -= shield;
            if (shield > 0) target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
            info.CharacterHp = MonsterAiRules.PlayerHpAfterDamage(info.CharacterHp, damage);
            killed = !MonsterAiRules.IsAlive(info.CharacterHp);
        }
        target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));

        var competing = _compete?.AreCompeting(attacker, target) == true;
        _ethereal?.Hit(target, false, damage, competing: competing);
        if (!competing && damage > 0)
        {
            _compete?.OnDamagedByOther(target, attacker);
        }

        if (killed)
        {
            StopAttack(target);
            _casts?.Interrupt(target);
            if (competing)
            {
                _compete.OnKilledBy(target, attacker);
            }
            else if (_guilds?.WarEnemy(attacker.ConnectionInfo, target.ConnectionInfo) != true)
            {
                if (_rules?.CurrentValue?.PkServer == true)
                {
                    _levelingService.ApplyDeathPenalty(target);
                    if (_deathDrops is not null) _ = _deathDrops.DropOnDeathAsync(target);
                }
                RecordPlayerKill(attacker, target);
                if (_rules?.CurrentValue?.PkServer == true)
                    MoralityRules.Set(target, MoralityRules.AfterDeath(info.ImmoralPoint, info.PkCount));
            }
            if (!competing && _titles is not null)
                _ = _titles.RecordAsync(attacker, Progression.TitleEvents.PlayerKilled(victimImmorality));
        }

        else if (wasAlive && damage > 0)
        {
            _casts?.Damaged(target, damage);
        }

        if (reflect && info.CharacterHp > 0 && damage > 0 && attacker.ConnectionInfo.CharacterHp > 0)
            foreach (var reflected in Combat.AttackMechanics.Reflects(states))
                if (reflected.Ratio > _random.Next(100) + 1)
                {
                    if (summonHandle != 0)
                    {
                        var summon = Array.Find(attacker.ConnectionInfo.Summons, s => s.Handle == summonHandle);
                        if (summon is not { Hp: > 0 }) break;
                        var reflectedDamage = PvpDamage(CombatFormulas.ResistedDamage(
                            Combat.AttackMechanics.ReflectAmount(reflected, damage, magical), summon.Stats.GetResistance(reflected.Element)));
                        if (reflectedDamage > 0) _creatures?.SummonReflected(attacker, summonHandle, reflectedDamage);
                        continue;
                    }
                    var amount = PvpDamage(CombatFormulas.ResistedDamage(Combat.AttackMechanics.ReflectAmount(reflected, damage, magical),
                        _stats.Compute(attacker.ConnectionInfo).Total.GetResistance(reflected.Element)));
                    if (amount > 0) LandPlayerDamage(target, attacker, amount, magical, false);
                }
        return info.CharacterHp;
    }

    private void RecordPlayerKill(GameClient killer, GameClient victim)
    {
        var k = killer.ConnectionInfo;
        var v = victim.ConnectionInfo;
        if (v.PkMode || v.ImmoralPoint >= MoralityRules.BloodyLimit) return;
        lock (k.ProgressLock)
        {
            var gap = _rules?.CurrentValue?.PkPenaltyLevel ?? 10;
            if (k.CharacterLevel - v.CharacterLevel >= gap && k.DkCount < int.MaxValue) k.DkCount++;
            var amount = MoralityRules.KillIncrease(k.CharacterLevel, v.CharacterLevel, v.PkMode, v.ImmoralPoint,
                k.ImmoralPoint, k.DkCount, gap, _rules?.CurrentValue?.PkServer == true,
                k.PartyId.HasValue ? Math.Max(1, _parties.MemberCount(killer)) : 0);
            if (k.PkCount < int.MaxValue) k.PkCount++;
            MoralityRules.Add(killer, amount);
            killer.Connection.Send(GameStatPackets.BuildProperty(k.CharacterHandle, "pk_count", k.PkCount));
            killer.Connection.Send(GameStatPackets.BuildProperty(k.CharacterHandle, "dk_count", k.DkCount));
        }
        if (_rules?.CurrentValue?.PkServer != true && k.ImmoralPoint >= MoralityRules.BloodyLimit)
        {
            var level = k.ImmoralPoint >= 1000m ? 3 : k.ImmoralPoint >= 500m ? 2 : 1;
            _casts?.ApplyState(killer, MoralityRules.NemesisState, level, (uint)(720000 * (1 << (level - 1))));
        }
    }

    public StatBlock GetPlayerStats(GameClient client) => _stats?.Compute(client.ConnectionInfo).Total;

    public StatBlock GetMonsterStats(long instanceId) =>
        _worldState.TryGetInstance(instanceId, out var instance) ? MonsterStats(instanceId, instance) : null;

    private HitResult RollMonsterHit(MonsterInstance instance, StatBlock monster, GameClient target,
        float baseDamage, DamageKind kind, int accuracyBonus, int criticalBonus, int element = 0)
    {
        var info = target.ConnectionInfo;
        var player = _stats.Compute(info).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(monster, instance.Level),
            Combatant.From(player, info.CharacterLevel), baseDamage, kind, accuracyBonus, criticalBonus, _random, element);

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

    /// <summary>A swing: the monster's hate grows by the damage (<c>StructCreature::Attack</c>).</summary>
    public int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage) =>
        ApplyDamage(client, instanceId, targetHandle, damage, damage);

    public int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage, int hate)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return 0;
        }

        if (_guilds?.CanDamage(client, instanceId) == false) return _worldState.GetHp(instanceId);

        var info = client.ConnectionInfo;
        var targetHp = _worldState.ApplyDamage(instanceId, damage, client, ServerClock.Now);
        _creatures?.MonsterDamaged(client, instanceId);
        if (targetHp > 0)
        {
            // The monster fights back. Every monster retaliates, aggressive or not, toward whoever it hates most;
            // the AI service takes it from here (Kill clears the aggro on death below).
            _worldState.AddHate(instanceId, client, hate);
            return targetHp;
        }

        var now = DateTime.UtcNow;
        if (!_worldState.TryKill(instanceId, now + _rates.MonsterRespawnDelay)) return 0;
        var contribution = MonsterContribution.Resolve(_worldState.TakeDamageContributions(instanceId), client, ServerClock.Now);

        // InstanceDungeon::onMonsterDelete: a HuntaHolic monster scores for its max-damage dealer.
        _huntaholic?.MonsterKilled(instanceId, contribution.FirstOrDefault()?.Representative ?? client);

        // vulcanus_check_respawn_group_clear, the on_dead_script of an instance's respawn rows.
        _dungeons?.MonsterKilled(instanceId);

        // An area can kill a monster outside the caster's view. Each viewer receives its own handle.
        var removedStates = _worldState.ClearStates(instanceId);
        var recipients = new HashSet<GameClient> { client };
        if (_players is not null)
        {
            foreach (var observer in _players.Observers(client)) recipients.Add(observer);
            if (_players.Registry is not null)
                foreach (var observer in _players.Registry.Clients) recipients.Add(observer);
        }
        var leaves = new List<PendingLeave>();
        foreach (var recipient in recipients)
        {
            var handle = ReferenceEquals(recipient, client) ? targetHandle
                : recipient.ConnectionInfo.GetMonsterHandle(instanceId);
            if (handle == 0) continue;
            foreach (var state in removedStates)
                recipient.Connection.Send(GameSkillPackets.BuildStateRemoval(handle, state.StateHandle, (uint)state.StateId));
            recipient.Connection.Send(GameMovePackets.BuildStopMove(handle,
                unchecked(ServerClock.Now + recipient.ConnectionInfo.ClientClockOffset), recipient.ConnectionInfo.Layer));
            recipient.Connection.Send(GameCharacterPackets.BuildStatusChange(handle, ActorStatus.ForMonster(true)));
            leaves.Add(new PendingLeave
            {
                Client = recipient,
                InstanceId = instanceId,
                Handle = handle,
                LeaveAt = now.AddSeconds(DeathAnimationSeconds)
            });
        }
        lock (_lock)
        {
            _lastAttacker[instanceId] = client;
            _sessions.Remove(client);
            _pendingLeaves.AddRange(leaves);
        }

        var (dropX, dropY) = _worldState.GetPosition(instanceId);
        // StructMonster::onDead: one level-gap malus for the gold, the chaos and the loot of this kill.
        var highestLevel = contribution.Select(g => HighestRewardedLevel(g.Representative, dropX, dropY, info.Layer))
            .DefaultIfEmpty(info.CharacterLevel).Max();
        var lootFactor = MonsterRewardRules.LootFactor(instance.Level, highestLevel);

        // ProcTame: a tamed monster leaves no gold, chaos nor item (StructMonster::onDead, if( !m_bTamedSuccess )).
        if (_creatures?.MonsterKilled(instanceId) == true)
        {
            lootFactor = 0;
        }

        var lootOwner = contribution.FirstOrDefault()?.Representative ?? client;
        if (_guilds?.IsObjective(instanceId) != true && (!instance.IsDungeonRaidMonster || instance.MonsterType >= 13))
            _groundItemService.DropForMonster(lootOwner, instance.MonsterId, dropX, dropY, instance.Z, instanceId, lootFactor);
        AwardKill(client, instanceId, targetHandle, instance, lootFactor, dropX, dropY, instance.Z, info.Layer, contribution);
        if (_quests is not null)
            foreach (var member in _parties.RewardMembers(client, dropX, dropY, info.Layer))
                _ = _quests.OnMonsterKilledAsync(member, instance.MonsterId, dropX, dropY, instance.Z);
        return targetHp;
    }

    private int HighestRewardedLevel(GameClient killer, float x, float y, byte layer)
    {
        var highest = killer.ConnectionInfo.CharacterLevel;
        foreach (var member in _parties.RewardMembers(killer, x, y, layer))
        {
            highest = Math.Max(highest, member.ConnectionInfo.CharacterLevel);
        }

        return highest;
    }

    /// <summary>
    /// The kill's rewards from the monster's own resource row (docs/packet-specs/socle-recompenses-monstres.md):
    /// gold and chaos drawn once behind their per-cent chances (scaled by the rates and the level-gap loot
    /// factor), the gold left on the ground, experience, JP and chaos shared by the party. A beneficiary farther
    /// than 500 from the corpse takes nothing but keeps the denominator, and experience and JP lose 5 % per level
    /// the beneficiary has over the monster (<c>StructPlayer::AddExp</c>).
    /// </summary>
    private void AwardKill(GameClient killer, long instanceId, uint corpseHandle, MonsterInstance monster,
        double lootFactor, float x, float y, float z, byte layer, IReadOnlyList<MonsterRewardGroup> contribution)
    {
        var reward = CombatRewards.Roll(monster.Rewards, _rates, _random, lootFactor);
        if (_guildEvents?.Killed is not null)
        {
            _ = AwardGuildKillAsync(killer, instanceId, corpseHandle, monster, reward, x, y, z, layer, contribution);
            return;
        }
        AwardReward(killer, instanceId, corpseHandle, monster, reward, x, y, z, layer, contribution);
    }

    private async Task AwardGuildKillAsync(GameClient killer, long instanceId, uint corpseHandle, MonsterInstance monster,
        MonsterKillReward reward, float x, float y, float z, byte layer, IReadOnlyList<MonsterRewardGroup> contribution)
    {
        try
        {
            reward = await _guildEvents.OnKilledAsync(killer, monster, instanceId, reward);
            AwardReward(killer, instanceId, corpseHandle, monster, reward, x, y, z, layer, contribution);
        }
        catch (Exception ex) { _logger.Error(ex, "Guild kill rewards failed for {Instance}", instanceId); }
    }

    private void AwardReward(GameClient killer, long instanceId, uint corpseHandle, MonsterInstance monster,
        MonsterKillReward reward, float x, float y, float z, byte layer, IReadOnlyList<MonsterRewardGroup> contribution)
    {
        if (reward.Gold > 0 && (!monster.IsDungeonRaidMonster || monster.MonsterType >= 13))
            _groundItemService.DropGoldForMonster(contribution.FirstOrDefault()?.Representative ?? killer,
                reward.Gold, x, y, z, instanceId);
        foreach (var group in contribution)
        {
            var members = _parties.RewardMembers(group.Representative, x, y, layer).Distinct().ToArray();
            if (members.Length == 0) members = new[] { group.Representative };
            for (var i = 0; i < members.Length; i++)
            {
                var client = members[i];
                var info = client.ConnectionInfo;
                lock (info.ProgressLock)
                {
                    var (px, py) = Buffs.SkillCastRangeRules.PlayerPosition(info, ServerClock.Now);
                    if (!MonsterRewardRules.WithinRewardRange(px, py, x, y))
                    {
                        continue;
                    }

                    if (info.CharacterHp > 0 && info.ImmoralPoint > 0m && monster.Level >= info.CharacterLevel)
                        MoralityRules.Set(client, MoralityRules.AfterMonsterKill(info.ImmoralPoint, monster.Level, info.CharacterLevel,
                            info.PartyId.HasValue ? Math.Max(1, _parties.MemberCount(client)) : 0));

                    var exp = MonsterRewardRules.ScaleForLevelGap(CombatRewards.Share(MonsterContribution.Scale(reward.Exp, group.Factor), members.Length, i),
                        monster.Level, info.CharacterLevel);
                    var jp = MonsterRewardRules.ScaleForLevelGap(CombatRewards.Share(MonsterContribution.Scale(reward.Jp, group.Factor), members.Length, i),
                        monster.Level, info.CharacterLevel);
                    var maxChaos = reward.Chaos > 0 ? CombatRewards.ChaosCapacity(_stats.Compute(info).Total.MaxChaos) : 0;
                    exp = MoralityRules.RewardExperience(exp, info.ImmoralPoint);
                    exp = _creatures?.LimitPlayerExperience(client, exp) ?? exp;
                    var pcBangMode = Math.Max(info.PcBangMode, _rules?.CurrentValue?.DefaultPcBangMode ?? 0);
                    var pcBangRate = Progression.MonsterRewardBonuses.PcBangRate(pcBangMode,
                        _rules?.CurrentValue?.AllyPcBangBonusRate ?? 0.1m, _rules?.CurrentValue?.PremiumPcBangBonusRate ?? 1.2m);
                    var bonus = Progression.MonsterRewardBonuses.Apply(exp, jp, info.CharacterStamina, info.CharacterLevel,
                        _rules?.CurrentValue?.StaminaBonusRate ?? 1m, Progression.MonsterRewardBonuses.InDungeon(x, y),
                        _rules?.CurrentValue?.DungeonRewardBonusRate ?? 0m, HasStaminaSaver(info), pcBangRate);
                    exp = bonus.Exp; jp = bonus.Jp;
                    // distributeExpToSummons: after the bonuses, before the player's own exp is applied.
                    _creatures?.ExperienceGained(client, exp);
                    if (info.CharacterStamina != bonus.Stamina)
                    {
                        info.CharacterStamina = bonus.Stamina;
                        client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "stamina", bonus.Stamina));
                    }
                    info.CharacterExp = CombatRewards.AddProgress(info.CharacterExp, exp);
                    info.CharacterJp = CombatRewards.AddProgress(info.CharacterJp, jp);
                    // The PC bang chaos bonus (fAllyPCBangChaosBonusRate / fPremiumPCBangChaosBonusRate).
                    var chaosRate = Progression.MonsterRewardBonuses.PcBangRate(pcBangMode,
                        _rules?.CurrentValue?.AllyPcBangChaosBonusRate ?? 0.1m, _rules?.CurrentValue?.PremiumPcBangChaosBonusRate ?? 0.1m);
                    var chaosShare = CombatRewards.Share(MonsterContribution.Scale(reward.Chaos, group.Factor), members.Length, i);
                    var chaosBonus = (int)Math.Floor(chaosShare * chaosRate);
                    var gainedChaos = (int)Math.Min(chaosShare + chaosBonus, Math.Max(0L, (long)maxChaos - info.CharacterChaos));
                    info.CharacterChaos += gainedChaos;
                    // Keep notifications and level resolution in the same order as concurrent kill credits.
                    client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
                    if (gainedChaos > 0)
                    {
                        // procDropChaos: 213 to the region, then StructPlayer::AddChaos's "chaos" property. The kill
                        // sends no 1001: the gold is on the ground until it is picked up.
                        // procDropChaos: the bonus is in nChaos and detailed in the three bonus fields (none without one).
                        var bonusType = (sbyte)(chaosBonus > 0 ? Math.Clamp((int)pcBangMode, 0, 2) : 0);
                        SendChaos(client, killer, instanceId, corpseHandle, gainedChaos, bonusType,
                            (sbyte)(bonusType == 0 ? 0 : (int)(chaosRate * 100)), bonusType == 0 ? 0 : chaosBonus);
                        client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "chaos", info.CharacterChaos));
                    }

                    _levelingService.ApplyExperience(client);
                    if (_titles is not null) _ = _titles.RecordMonsterKillAsync(client, monster);
                }
            }
        }
    }

    private void SendChaos(GameClient recipient, GameClient killer, long instanceId, uint killerCorpseHandle, int amount,
        sbyte bonusType = 0, sbyte bonusPercent = 0, int bonus = 0)
    {
        var viewers = new HashSet<GameClient> { recipient };
        if (_players is not null && _players.Registry is not null)
            foreach (var viewer in _players.Registry.Clients) viewers.Add(viewer);
        foreach (var viewer in viewers)
        {
            if (!ReferenceEquals(viewer, recipient))
            {
                lock (viewer.ConnectionInfo.PlayerVisibilityLock)
                    if (!viewer.ConnectionInfo.SpawnedPlayers.ContainsKey(recipient.ConnectionInfo.CharacterHandle)) continue;
            }
            var corpse = viewer.ConnectionInfo.GetMonsterHandle(instanceId);
            if (corpse == 0 && ReferenceEquals(viewer, killer)) corpse = killerCorpseHandle;
            if (corpse != 0) viewer.Connection.Send(GameRewardPackets.BuildGetChaos(
                recipient.ConnectionInfo.CharacterHandle, corpse, amount, bonusType, bonusPercent, bonus));
        }
    }

    private static bool HasStaminaSaver(ConnectionInfo info)
    {
        lock (info.BuffLock)
            return info.ActiveBuffs.Any(b => b.StateId == 4003
                && (b.EndTick == 0 || unchecked((int)(b.EndTick - ServerClock.Now)) > 0));
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

        /// <summary>A bow's aiming half is done: the next turn shoots.</summary>
        public bool Aimed;

        /// <summary>The player attacked, in a duel or a PK fight; null for a monster.</summary>
        public GameClient TargetPlayer;
    }

    private sealed class PendingLeave
    {
        public GameClient Client;
        public long InstanceId;
        public uint Handle;
        public DateTime LeaveAt;
    }
}
