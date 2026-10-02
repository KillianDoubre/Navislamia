using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services.Creatures;

public interface ICreatureService
{
    /// <summary>World entry: the cards, the formation, the 301 of each slotted summon, and the main summon back.</summary>
    Task OnWorldEntryAsync(GameClient client);

    /// <summary>Lobby or disconnect: the taming is abandoned and the summon attacks stop.</summary>
    void OnWorldExit(GameClient client);

    /// <summary><c>StructSkill</c>'s taming checks, in their order; <c>Success</c> when 4003 may be cast.</summary>
    ResultCode CheckTaming(ConnectionInfo info, long instanceId);

    /// <summary><c>CREATURE_TAMING</c> → <c>SetTamer</c>; false when the monster or the card no longer qualifies.</summary>
    bool StartTaming(GameClient client, long instanceId, int skillLevel);

    /// <summary>Whether a card may be summoned (4001) or sent back (4002).</summary>
    ResultCode CheckSummon(ConnectionInfo info, uint cardHandle, bool summon);

    bool Summon(GameClient client, uint cardHandle);

    bool Unsummon(GameClient client, uint cardHandle);

    /// <summary><c>TM_EQUIP_SUMMON</c> (303) from the client: <c>StructPlayer::EquipSummon</c>.</summary>
    Task EquipAsync(GameClient client, IReadOnlyList<uint> cardHandles, bool openDialog);

    /// <summary>Whether a handle is one of the client's summons in the world.</summary>
    bool OwnsSummon(GameClient client, uint handle);

    /// <summary>A move request naming a summon: its position is kept and the walk echoed.</summary>
    void MoveSummon(GameClient client, uint summonHandle, float x, float y, uint clientTime, byte speedSync,
        ReadOnlySpan<byte> waypoints);

    /// <summary>An attack request naming a summon (target 0 stops it).</summary>
    void SummonAttack(GameClient client, uint summonHandle, uint targetHandle);

    /// <summary>After a warp the summons in the world follow their master.</summary>
    void FollowWarp(GameClient client);
}

/// <summary>
/// Taming, formation and summoning (docs/packet-specs/socle-apprivoisement-invocation.md §15), ported from the
/// official server source: <c>GameProc.cpp</c> (<c>SetTamer</c>, <c>ClearTamer</c>, <c>ProcTame</c>,
/// <c>AllocNewSummon</c>), <c>StructPlayer::EquipSummon</c>/<c>DoSummon</c>/<c>DoUnSummon</c>,
/// <c>StructSkill</c>'s taming checks and <c>BroadcastTamingMessage</c>.
/// </summary>
public sealed class CreatureService : ICreatureService, ICreatureEventListener, IDisposable
{
    private const int TamingSkill = BuffCatalog.TamingSkill;
    private const byte ChatPartySystem = (byte)ChatType.PartySystem;

    private readonly ILogger _logger = Log.ForContext<CreatureService>();
    private readonly ICreatureCatalog _catalog;
    private readonly ICharacterService _characters;
    private readonly MonsterWorldState _world;
    private readonly ICombatService _combat;
    private readonly SummonWorldService _summons;
    private readonly IPlayerVisibilityService _players;
    private readonly IPartyService _parties;
    private readonly IBuffCatalog _skills;
    private readonly ICombatRandom _random;
    private readonly object _lock = new();
    private readonly Dictionary<long, Taming> _tamings = new();
    private readonly Dictionary<uint, SummonMove> _moves = new();
    private readonly Dictionary<uint, SummonSwing> _swings = new();
    private readonly CancellationTokenSource _stop = new();

    public CreatureService(ICreatureCatalog catalog, ICharacterService characters, MonsterWorldState world,
        ICombatService combat, SummonWorldService summons, IPlayerVisibilityService players = null,
        IPartyService parties = null, IBuffCatalog skills = null, CreatureEvents events = null,
        ICombatRandom random = null, bool runTicks = true)
    {
        _catalog = catalog;
        _characters = characters;
        _world = world;
        _combat = combat;
        _summons = summons;
        _players = players;
        _parties = parties;
        _skills = skills;
        _random = random ?? CombatRandom.Shared;
        events?.Attach(this);
        if (runTicks)
        {
            _ = RunAsync();
        }
    }

    private sealed record Taming(GameClient Tamer, long CardItemId, int SkillLevel, int MonsterId)
    {
        public uint Deadline { get; set; }
    }

    private sealed class SummonMove
    {
        public float X, Y, DestX, DestY;
        public uint StartTick;
        public byte Speed;
    }

    private sealed class SummonSwing
    {
        public GameClient Master;
        public long CardItemId;
        public long TargetInstanceId;
        public DateTime NextSwingAt;
    }

    // ---- world entry and exit -------------------------------------------------------------------------------

    public async Task OnWorldEntryAsync(GameClient client)
    {
        var info = client.ConnectionInfo;
        CreatureState state;
        try
        {
            state = await _characters.GetCreatureStateAsync(info.CharacterName, _catalog.CardIds);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the creatures of {clientTag}", client.ClientTag);
            state = null;
        }

        if (state is null)
        {
            // The bootstrap keeps its 303, empty.
            client.Connection.Send(GameCharacterPackets.BuildEquipSummon(info.SummonSlots));
            return;
        }

        CreatureCard main = null;
        lock (info.SummonLock)
        {
            info.CreatureCards.Clear();
            foreach (var record in state.Cards)
            {
                var card = ToCard(record);
                info.CreatureCards[card.ItemId] = card;
            }

            info.SummonSlots = state.Slots;
            if (state.MainSummonId is { } mainId)
            {
                main = info.CreatureCards.Values.FirstOrDefault(c => c.SummonId == mainId);
            }
        }

        // SendCharacterInfo: the creature window gets every formed summon, then the formation.
        foreach (var card in SlottedCards(info))
        {
            SendSummonInfo(client, card);
        }

        client.Connection.Send(GameCharacterPackets.BuildEquipSummon(info.SummonSlots));

        // The main summon comes back with its master (StructPlayer::onLogin, AddNoise 50).
        if (main is not null && main.HasSummon && Array.IndexOf(info.SummonSlots, main.ItemId) >= 0)
        {
            EnterWorld(client, main, SummonWorldService.LoginNoiseRange);
        }
    }

    public void OnWorldExit(GameClient client)
    {
        List<long> tamed;
        lock (_lock)
        {
            tamed = _tamings.Where(pair => ReferenceEquals(pair.Value.Tamer, client)).Select(pair => pair.Key).ToList();
            foreach (var handle in _swings.Where(pair => ReferenceEquals(pair.Value.Master, client)).Select(p => p.Key).ToList())
            {
                _swings.Remove(handle);
            }
        }

        foreach (var instanceId in tamed)
        {
            ClearTamer(instanceId, broadcast: true);
        }

        foreach (var summon in client.ConnectionInfo.Summons)
        {
            lock (_lock)
            {
                _moves.Remove(summon.Handle);
            }
        }
    }

    private static CreatureCard ToCard(CreatureCardRecord record) => new()
    {
        ItemId = record.Card.Id,
        Code = (int)record.Card.ItemResourceId,
        Amount = record.Card.Amount,
        Flag = record.Card.Flag,
        Enhance = (int)record.Card.Enhance,
        SummonId = record.Summon?.Id ?? 0,
        SummonCode = record.Summon?.SummonResourceId ?? 0,
        SummonName = record.Summon?.Name ?? string.Empty,
        Level = Math.Max(1, record.Summon?.Lv ?? 1),
        Exp = record.Summon?.Exp ?? 0,
        Sp = record.Summon?.Sp ?? 0,
        Hp = record.Summon?.Hp ?? 0,
        Mp = record.Summon?.Mp ?? 0
    };

    private static List<CreatureCard> SlottedCards(ConnectionInfo info)
    {
        lock (info.SummonLock)
        {
            return info.SummonSlots.Where(id => id != 0)
                .Select(id => info.CreatureCards.GetValueOrDefault(id))
                .Where(card => card is { HasSummon: true })
                .ToList();
        }
    }

    /// <summary>
    /// <c>SendAddSummonMessage</c>: 301, then the summon's stats, vitals, level and experience, all on its handle.
    /// </summary>
    private void SendSummonInfo(GameClient client, CreatureCard card)
    {
        if (!_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return;
        }

        if (card.SummonHandle == 0)
        {
            card.SummonHandle = WorldObjectHandle.Next();
        }

        var stats = CreatureRules.SummonStats(resource, card.Level);
        var (hp, mp) = Vitals(card, stats);
        client.Connection.Send(GameSummonPackets.BuildAddSummonInfo(card.Handle, card.SummonHandle, card.SummonName,
            card.SummonCode, card.Level, card.Sp));
        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, stats, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, new StatBlock(), StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildHpMp(card.SummonHandle, 0, hp, (int)stats.MaxHp, 0, mp,
            (int)stats.MaxMp));
        client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(card.SummonHandle, card.Level, 1));
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(card.SummonHandle, card.Exp, 0));
        card.InfoSent = true;
    }

    /// <summary>A summon never saved with vitals (a fresh one) starts full.</summary>
    private static (int Hp, int Mp) Vitals(CreatureCard card, StatBlock stats)
    {
        var maxHp = Math.Max(1, (int)stats.MaxHp);
        var maxMp = Math.Max(0, (int)stats.MaxMp);
        var hp = card.Hp <= 0 ? maxHp : Math.Min(card.Hp, maxHp);
        var mp = card.Mp <= 0 ? maxMp : Math.Min(card.Mp, maxMp);
        return (hp, mp);
    }

    // ---- taming -----------------------------------------------------------------------------------------------

    public ResultCode CheckTaming(ConnectionInfo info, long instanceId)
    {
        if (!_world.TryGetInstance(instanceId, out var monster) || !_world.IsAlive(instanceId))
        {
            return ResultCode.NotActable;
        }

        var cardCode = CardFor(monster);
        bool hasTamer;
        lock (_lock)
        {
            hasTamer = _tamings.ContainsKey(instanceId);
        }

        var attempt = new TamingAttempt(true, cardCode, hasTamer, _world.GetHp(instanceId), monster.Hp,
            FindEmptyCard(info, cardCode) is not null, info.TamingTargetInstanceId >= 0);
        return TamingRules.ResultCodeOf(TamingRules.Resolve(attempt));
    }

    /// <summary><c>GetTameItemCode</c>: <c>SummonResource[taming_code].card_id</c>, 0 when not tamable.</summary>
    private int CardFor(MonsterInstance monster) =>
        monster.TamingId != 0 && _catalog.TryGetSummon(monster.TamingId, out var summon) ? summon.CardId : 0;

    /// <summary><c>FindEmptySummonCard</c>: the card code, without the summon flag.</summary>
    private static CreatureCard FindEmptyCard(ConnectionInfo info, int code)
    {
        if (code == 0)
        {
            return null;
        }

        lock (info.SummonLock)
        {
            return info.CreatureCards.Values.FirstOrDefault(c => c.Code == code && !c.IsBound && c.Amount > 0);
        }
    }

    public bool StartTaming(GameClient client, long instanceId, int skillLevel)
    {
        var info = client.ConnectionInfo;
        if (CheckTaming(info, instanceId) != ResultCode.Success
            || !_world.TryGetInstance(instanceId, out var monster))
        {
            return false;
        }

        var card = FindEmptyCard(info, CardFor(monster));
        lock (_lock)
        {
            if (card is null || _tamings.ContainsKey(instanceId))
            {
                return false;
            }

            _tamings[instanceId] = new Taming(client, card.ItemId, skillLevel, monster.MonsterId)
            {
                Deadline = unchecked(ServerClock.Now + CreatureRules.TamingIntervalTicks)
            };
        }

        info.TamingTargetInstanceId = instanceId;
        info.TamingCardItemId = card.ItemId;

        // CREATURE_TAMING: a successful SetTamer draws the monster's attention (AddHate 1).
        _world.AddHate(instanceId, client, 1);
        Broadcast(client, instanceId, monster.MonsterId, GameSummonPackets.TamingModeStart);
        return true;
    }

    public void OnMonsterDamaged(GameClient attacker, long instanceId)
    {
        lock (_lock)
        {
            if (_tamings.TryGetValue(instanceId, out var taming) && ReferenceEquals(taming.Tamer, attacker))
            {
                taming.Deadline = unchecked(ServerClock.Now + CreatureRules.TamingIntervalTicks);
            }
        }
    }

    /// <summary>
    /// <c>ProcTame</c>, at the monster's death. The draw is decided now, since a success takes the loot away; the
    /// cards change once the database has them.
    /// </summary>
    public bool OnMonsterKilled(long instanceId)
    {
        Taming taming;
        lock (_lock)
        {
            if (!_tamings.Remove(instanceId, out taming))
            {
                return false;
            }
        }

        var tamer = taming.Tamer;
        var info = tamer.ConnectionInfo;
        ForgetTaming(info, instanceId);

        // A tamer gone or dead fails without a frame (BroadcastTamingMessage with no player sends nothing).
        if (!IsOnline(tamer) || info.CharacterHp <= 0)
        {
            return false;
        }

        var (mx, my) = _world.GetPosition(instanceId);
        var (tx, ty) = info.PositionAt(ServerClock.Now);
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(taming.CardItemId);
        }

        if (CombatRange.Distance(mx, my, tx, ty) > CreatureRules.TamingRange || card is null || card.Amount <= 0
            || card.IsBound || !_world.TryGetInstance(instanceId, out var monster))
        {
            Broadcast(tamer, instanceId, taming.MonsterId, GameSummonPackets.TamingModeFailed);
            return false;
        }

        var (var0, var1) = TamingVars();
        var success = CreatureRules.TamingSucceeds(monster.TamingPercentage, var0, var1, taming.SkillLevel,
            0, _random.Next(1_000_000) + 1);
        _ = CommitTamingAsync(tamer, instanceId, monster, card, success);
        return success;
    }

    private (decimal Var0, decimal Var1) TamingVars()
    {
        if (_skills is not null && _skills.TryGet(TamingSkill, out var fields) && fields.Vars is { Length: >= 2 } vars)
        {
            return (vars[0], vars[1]);
        }

        // Epic 7 SkillResource 4003: var1 0.06, var2 0.03.
        return (0.06m, 0.03m);
    }

    private async Task CommitTamingAsync(GameClient tamer, long instanceId, MonsterInstance monster,
        CreatureCard card, bool success)
    {
        var info = tamer.ConnectionInfo;
        try
        {
            string name = null;
            int hp = 0, mp = 0;
            if (success && _catalog.TryGetSummon(monster.TamingId, out var resource))
            {
                name = _catalog.RandomName(_random.Next);
                var stats = CreatureRules.SummonStats(resource, 1);
                hp = (int)stats.MaxHp;
                mp = (int)stats.MaxMp;
            }

            var commit = await _characters.CommitTamingAsync(info.CharacterName, card.ItemId, success,
                monster.TamingId, name, hp, mp);
            if (commit is null)
            {
                Broadcast(tamer, instanceId, monster.MonsterId, GameSummonPackets.TamingModeFailed);
                return;
            }

            lock (info.SummonLock)
            {
                if (commit.RemainingAmount > 0)
                {
                    card.Amount = commit.RemainingAmount;
                }
                else
                {
                    info.CreatureCards.Remove(card.ItemId);
                }

                if (commit.NewCard is not null && commit.Summon is not null)
                {
                    info.CreatureCards[commit.NewCard.Id] = ToCard(new CreatureCardRecord(commit.NewCard, commit.Summon));
                }
            }

            tamer.Connection.Send(commit.RemainingAmount > 0
                ? GameCharacterPackets.BuildUpdateItemCount(card.Handle, commit.RemainingAmount)
                : GameCharacterPackets.BuildDestroyItem(card.Handle));
            if (commit.NewCard is not null)
            {
                foreach (var frame in GameCharacterPackets.BuildInventory(new[] { commit.NewCard }))
                {
                    tamer.Connection.Send(frame);
                }
            }

            Broadcast(tamer, instanceId, monster.MonsterId,
                success ? GameSummonPackets.TamingModeSuccess : GameSummonPackets.TamingModeFailed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Taming of monster {instanceId} by {clientTag} could not be saved", instanceId,
                tamer.ClientTag);
        }
    }

    /// <summary><c>ClearTamer</c>: the monster forgets its tamer, the card its taming mark, and the region is told.</summary>
    private void ClearTamer(long instanceId, bool broadcast)
    {
        Taming taming;
        lock (_lock)
        {
            if (!_tamings.Remove(instanceId, out taming))
            {
                return;
            }
        }

        ForgetTaming(taming.Tamer.ConnectionInfo, instanceId);
        if (broadcast && IsOnline(taming.Tamer))
        {
            Broadcast(taming.Tamer, instanceId, taming.MonsterId, GameSummonPackets.TamingModeAbandon);
        }
    }

    private static void ForgetTaming(ConnectionInfo info, long instanceId)
    {
        if (info.TamingTargetInstanceId == instanceId)
        {
            info.TamingTargetInstanceId = -1;
            info.TamingCardItemId = 0;
        }
    }

    /// <summary>
    /// <c>BroadcastTamingMessage</c>: 310 to every player who sees the monster (each with its own handle for it),
    /// then the <c>TAMING_*|name|</c> line to the tamer's party, or to the tamer alone from <c>@SYSTEM</c>.
    /// </summary>
    private void Broadcast(GameClient tamer, long instanceId, int monsterId, sbyte mode)
    {
        var tamerHandle = tamer.ConnectionInfo.CharacterHandle;
        var recipients = new HashSet<GameClient> { tamer };
        if (_players?.Registry is { } registry)
        {
            foreach (var client in registry.Clients)
            {
                recipients.Add(client);
            }
        }

        foreach (var client in recipients)
        {
            var handle = client.ConnectionInfo.GetMonsterHandle(instanceId);
            if (handle != 0)
            {
                client.Connection.Send(GameSummonPackets.BuildTamingInfo(mode, tamerHandle, handle));
            }
        }

        var text = mode switch
        {
            GameSummonPackets.TamingModeStart => "TAMING_START",
            GameSummonPackets.TamingModeSuccess => "TAMING_SUCCESS",
            _ => "TAMING_FAILED"
        } + "|" + _catalog.MonsterName(monsterId) + "|";
        if (_parties?.TrySendPartyLine(tamer, text) != true)
        {
            tamer.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", ChatPartySystem, text));
        }
    }

    private bool IsOnline(GameClient client) =>
        client.ConnectionInfo.CharacterHandle != 0
        && (_players?.Registry is null
            || _players.Registry.TryResolve(client.ConnectionInfo.CharacterHandle, out var current)
            && ReferenceEquals(current, client));

    // ---- formation (303) -----------------------------------------------------------------------------------

    public async Task EquipAsync(GameClient client, IReadOnlyList<uint> cardHandles, bool openDialog)
    {
        var info = client.ConnectionInfo;
        var slotCount = CreatureRules.SlotCount(info.LearnedSkills.GetValueOrDefault(CreatureRules.CreatureControlSkill));
        long[] current;
        lock (info.SummonLock)
        {
            current = info.SummonSlots.Length == CreatureRules.MaxSlots
                ? (long[])info.SummonSlots.Clone()
                : new long[CreatureRules.MaxSlots];
        }

        var resolved = CreatureRules.ResolveFormation(current, cardHandles, slotCount,
            card => CanBind(info, card), card => IsCardInWorld(info, card));
        if (resolved is null)
        {
            client.Connection.Send(GameCharacterPackets.BuildEquipSummon(current, openDialog));
            return;
        }

        // A bound card with no summon yet gets one (AllocNewSummon), and its 301 the first time it is formed.
        foreach (var cardId in resolved.Where(id => id != 0))
        {
            CreatureCard card;
            lock (info.SummonLock)
            {
                card = info.CreatureCards.GetValueOrDefault(cardId);
            }

            if (card is null)
            {
                continue;
            }

            if (!card.HasSummon && !await CreateSummonAsync(info, card))
            {
                Array.Clear(resolved, Array.IndexOf(resolved, cardId), 1);
                continue;
            }

            if (!card.InfoSent)
            {
                SendSummonInfo(client, card);
            }
        }

        // An unformed main summon is no longer the main one.
        var main = info.MainSummonCardId;
        if (main != 0 && Array.IndexOf(resolved, main) < 0)
        {
            info.MainSummonCardId = 0;
        }

        lock (info.SummonLock)
        {
            info.SummonSlots = resolved;
        }

        await _characters.SaveCreatureFormationAsync(info.CharacterName, resolved, MainSummonId(info));
        client.Connection.Send(GameCharacterPackets.BuildEquipSummon(resolved, openDialog));
    }

    private bool CanBind(ConnectionInfo info, long cardId)
    {
        lock (info.SummonLock)
        {
            return info.CreatureCards.TryGetValue(cardId, out var card) && card.IsBound && card.Amount > 0;
        }
    }

    private static bool IsCardInWorld(ConnectionInfo info, long cardId)
    {
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(cardId);
        }

        return card is not null && card.SummonHandle != 0
               && info.Summons.Any(summon => summon.Handle == card.SummonHandle);
    }

    private async Task<bool> CreateSummonAsync(ConnectionInfo info, CreatureCard card)
    {
        // A bound card names its creature only through its summon row; a card bound outside taming has none,
        // and its summon code is the card's first summon resource.
        var resource = _catalog.FirstSummonForCard(card.Code);
        if (resource is null)
        {
            return false;
        }

        var stats = CreatureRules.SummonStats(resource, 1);
        var summon = await _characters.CreateSummonAsync(info.CharacterName, card.ItemId, resource.Id,
            _catalog.RandomName(_random.Next), (int)stats.MaxHp, (int)stats.MaxMp);
        if (summon is null)
        {
            return false;
        }

        card.SummonId = summon.Id;
        card.SummonCode = summon.SummonResourceId;
        card.SummonName = summon.Name;
        card.Level = Math.Max(1, summon.Lv);
        card.Hp = summon.Hp;
        card.Mp = summon.Mp;
        return true;
    }

    private static long? MainSummonId(ConnectionInfo info)
    {
        lock (info.SummonLock)
        {
            return info.CreatureCards.GetValueOrDefault(info.MainSummonCardId)?.SummonId is { } id and > 0 ? id : null;
        }
    }

    // ---- summon (4001) and unsummon (4002) ------------------------------------------------------------------

    public ResultCode CheckSummon(ConnectionInfo info, uint cardHandle, bool summon)
    {
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(cardHandle);
        }

        if (card is null || !card.IsBound || !card.HasSummon)
        {
            return ResultCode.NotExist;
        }

        var inWorld = IsCardInWorld(info, card.ItemId);
        if (summon)
        {
            // PrepareSummon: one of the formation's cards, its summon not already in the world.
            return Array.IndexOf(info.SummonSlots, card.ItemId) < 0 || inWorld
                ? ResultCode.NotActable
                : ResultCode.Success;
        }

        return inWorld ? ResultCode.Success : ResultCode.NotActable;
    }

    public bool Summon(GameClient client, uint cardHandle)
    {
        var info = client.ConnectionInfo;
        if (CheckSummon(info, cardHandle, true) != ResultCode.Success)
        {
            return false;
        }

        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(cardHandle);
        }

        // Player::DoSummon: the main summon in the world goes back first.
        if (info.MainSummonCardId != 0 && info.MainSummonCardId != card.ItemId)
        {
            Unsummon(client, (uint)info.MainSummonCardId);
        }

        if (!EnterWorld(client, card, SummonWorldService.SummonNoiseRange))
        {
            return false;
        }

        info.MainSummonCardId = card.ItemId;
        _ = SaveFormationAsync(info);
        return true;
    }

    public bool Unsummon(GameClient client, uint cardHandle)
    {
        var info = client.ConnectionInfo;
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(cardHandle);
        }

        if (card is null || !IsCardInWorld(info, card.ItemId))
        {
            return false;
        }

        lock (_lock)
        {
            _moves.Remove(card.SummonHandle);
            _swings.Remove(card.SummonHandle);
        }

        _summons.Leave(info, client.ClientTag, client.Connection, card.SummonHandle, client);
        if (info.MainSummonCardId == card.ItemId)
        {
            info.MainSummonCardId = 0;
            _ = SaveFormationAsync(info);
        }

        return true;
    }

    private async Task SaveFormationAsync(ConnectionInfo info)
    {
        try
        {
            await _characters.SaveCreatureFormationAsync(info.CharacterName, (long[])info.SummonSlots.Clone(),
                MainSummonId(info));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save the creature formation of {name}", info.CharacterName);
        }
    }

    private bool EnterWorld(GameClient client, CreatureCard card, int noiseRange)
    {
        if (!_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return false;
        }

        var info = client.ConnectionInfo;
        if (!card.InfoSent)
        {
            SendSummonInfo(client, card);
        }

        var stats = CreatureRules.SummonStats(resource, card.Level);
        var (hp, mp) = Vitals(card, stats);
        var entry = new SummonWorldEntry
        {
            CardHandle = card.Handle,
            Code = card.SummonCode,
            Name = card.SummonName,
            Level = card.Level,
            Sp = card.Sp,
            Hp = hp,
            MaxHp = (int)stats.MaxHp,
            Mp = mp,
            MaxMp = (int)stats.MaxMp,
            Z = info.Z,
            NoiseRange = noiseRange,
            IsFirstEnter = true
        };

        var handle = _summons.Enter(info, client.ClientTag, client.Connection, entry, client, card.SummonHandle,
            sendInfo: false);
        if (handle == 0)
        {
            return false;
        }

        card.SummonHandle = handle;
        var presence = info.Summons.FirstOrDefault(s => s.Handle == handle);
        lock (_lock)
        {
            _moves[handle] = new SummonMove
            {
                X = presence?.X ?? info.X,
                Y = presence?.Y ?? info.Y,
                DestX = presence?.X ?? info.X,
                DestY = presence?.Y ?? info.Y,
                StartTick = ServerClock.Now,
                Speed = MonsterMovement.SpeedByte(stats.MoveSpeed)
            };
        }

        return true;
    }

    public void FollowWarp(GameClient client)
    {
        var info = client.ConnectionInfo;
        foreach (var presence in info.Summons.ToArray())
        {
            CreatureCard card;
            lock (info.SummonLock)
            {
                card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == presence.Handle);
            }

            if (card is null)
            {
                continue;
            }

            lock (_lock)
            {
                _moves.Remove(presence.Handle);
                _swings.Remove(presence.Handle);
            }

            _summons.Leave(info, client.ClientTag, client.Connection, presence.Handle, client);
            EnterWorld(client, card, SummonWorldService.WarpNoiseRange);
        }
    }

    // ---- movement and attack ---------------------------------------------------------------------------------

    public bool OwnsSummon(GameClient client, uint handle) =>
        handle != 0 && client.ConnectionInfo.Summons.Any(summon => summon.Handle == handle);

    public void MoveSummon(GameClient client, uint summonHandle, float x, float y, uint clientTime, byte speedSync,
        ReadOnlySpan<byte> waypoints)
    {
        var info = client.ConnectionInfo;
        if (!OwnsSummon(client, summonHandle))
        {
            return;
        }

        SummonMove move;
        lock (_lock)
        {
            if (!_moves.TryGetValue(summonHandle, out move))
            {
                return;
            }

            // onMoveRequest: a summon walks at its own speed, or at its master's when it follows (speed_sync).
            if (speedSync != 0)
            {
                move.Speed = info.MoveSpeed != 0 ? info.MoveSpeed : ConnectionInfo.EchoedMoveSpeed;
            }

            var count = waypoints.Length / 8;
            move.X = x;
            move.Y = y;
            move.StartTick = ServerClock.Now;
            move.DestX = count > 0 ? BinaryPrimitives.ReadSingleLittleEndian(waypoints.Slice((count - 1) * 8, 4)) : x;
            move.DestY = count > 0 ? BinaryPrimitives.ReadSingleLittleEndian(waypoints.Slice((count - 1) * 8 + 4, 4)) : y;
        }

        var frame = GameMovePackets.BuildMove(summonHandle, clientTime, info.Layer, move.Speed, waypoints);
        client.Connection.Send(frame);
        _players?.SendToObservers(client, frame);
    }

    public void SummonAttack(GameClient client, uint summonHandle, uint targetHandle)
    {
        var info = client.ConnectionInfo;
        if (!OwnsSummon(client, summonHandle))
        {
            return;
        }

        if (targetHandle == 0 || !info.TryResolveMonster(targetHandle, out var instanceId) || !_world.IsAlive(instanceId))
        {
            StopSwing(summonHandle, client);
            return;
        }

        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
        }

        if (card is null)
        {
            return;
        }

        lock (_lock)
        {
            _swings[summonHandle] = new SummonSwing
            {
                Master = client,
                CardItemId = card.ItemId,
                TargetInstanceId = instanceId,
                NextSwingAt = DateTime.UtcNow
            };
        }
    }

    private void StopSwing(uint summonHandle, GameClient client)
    {
        SummonSwing swing;
        lock (_lock)
        {
            if (!_swings.Remove(summonHandle, out swing))
            {
                return;
            }
        }

        var target = client.ConnectionInfo.GetMonsterHandle(swing.TargetInstanceId);
        client.Connection.Send(GameAttackPackets.BuildEndAttack(summonHandle, target));
    }

    private (float X, float Y) SummonPosition(uint handle, uint now)
    {
        lock (_lock)
        {
            if (!_moves.TryGetValue(handle, out var move))
            {
                return (0, 0);
            }

            var length = CombatRange.Distance(move.X, move.Y, move.DestX, move.DestY);
            var end = MonsterMovement.EndTick(move.StartTick, length, move.Speed);
            return MonsterMovement.PositionAt(move.X, move.Y, move.DestX, move.DestY, move.StartTick, end, now);
        }
    }

    /// <summary>
    /// One swing of a summon: the official rule (<see cref="CombatFormulas.Resolve"/>) from the summon's stats, landed
    /// through <see cref="ICombatService.ApplyDamage(GameClient, long, uint, int, int)"/> so the kill, the reward and
    /// the loot are the master's (<c>StructMonster::onDamage</c> credits a summon's damage to its master).
    /// </summary>
    public void ProcessSwings(DateTime now)
    {
        List<(uint Handle, SummonSwing Swing)> due;
        lock (_lock)
        {
            due = _swings.Where(pair => pair.Value.NextSwingAt <= now).Select(pair => (pair.Key, pair.Value)).ToList();
        }

        foreach (var (handle, swing) in due)
        {
            var client = swing.Master;
            var info = client.ConnectionInfo;
            CreatureCard card;
            lock (info.SummonLock)
            {
                card = info.CreatureCards.GetValueOrDefault(swing.CardItemId);
            }

            var monsterHandle = info.GetMonsterHandle(swing.TargetInstanceId);
            if (card is null || !OwnsSummon(client, handle) || monsterHandle == 0
                || !_world.IsAlive(swing.TargetInstanceId)
                || !_world.TryGetInstance(swing.TargetInstanceId, out var monster)
                || !_catalog.TryGetSummon(card.SummonCode, out var resource))
            {
                StopSwing(handle, client);
                continue;
            }

            var tick = ServerClock.Now;
            var (sx, sy) = SummonPosition(handle, tick);
            var (mx, my) = _world.GetPosition(swing.TargetInstanceId);
            if (CombatRange.Distance(sx, sy, mx, my) > CreatureRules.SummonReach(resource, monster.Size, monster.Scale))
            {
                swing.NextSwingAt = now.AddMilliseconds(200);
                continue;
            }

            var stats = CreatureRules.SummonStats(resource, card.Level);
            var defender = _combat.GetMonsterStats(swing.TargetInstanceId) ?? new StatBlock();
            var hit = CombatFormulas.Resolve(Combatant.From(stats, card.Level), Combatant.From(defender, monster.Level),
                stats.AttackPointRight, DamageKind.Physical, 0, 0, _random);
            var intervalMs = CombatService.IntervalMs(CombatFormulas.AttackIntervalTicks(stats.AttackSpeed));
            var targetHp = _combat.ApplyDamage(client, swing.TargetInstanceId, monsterHandle, hit.Damage, hit.Damage);
            var summonHp = Vitals(card, stats).Hp;

            client.Connection.Send(GameAttackPackets.BuildAttackEvent(handle, monsterHandle, intervalMs, intervalMs,
                GameAttackPackets.ActionAttack, hit.Damage, targetHp, summonHp, (byte)hit.Flags));
            ObserverFrames.SendMonsterFrame(_players, client, swing.TargetInstanceId, (_, observerHandle) =>
                GameAttackPackets.BuildAttackEvent(handle, observerHandle, intervalMs, intervalMs,
                    GameAttackPackets.ActionAttack, hit.Damage, targetHp, summonHp, (byte)hit.Flags));

            if (targetHp <= 0)
            {
                StopSwing(handle, client);
                continue;
            }

            swing.NextSwingAt = now.AddMilliseconds(intervalMs);
        }
    }

    /// <summary>
    /// The taming window: past <see cref="CreatureRules.TamingIntervalTicks"/> without a hit of the tamer, or when
    /// the monster has given up its enemy (it goes home), the tamer is cleared (<c>MonsterAI.cpp</c>).
    /// </summary>
    public void ProcessTamings(uint now)
    {
        List<long> expired;
        lock (_lock)
        {
            expired = _tamings
                .Where(pair => unchecked((int)(now - pair.Value.Deadline)) > 0
                               || !_world.TryGetAggro(pair.Key, out _, out _))
                .Select(pair => pair.Key)
                .ToList();
        }

        foreach (var instanceId in expired)
        {
            if (_world.IsAlive(instanceId))
            {
                ClearTamer(instanceId, broadcast: true);
            }
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                try
                {
                    ProcessSwings(DateTime.UtcNow);
                    if (++ticks % 10 == 0)
                    {
                        ProcessTamings(ServerClock.Now);
                    }
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "The creature tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
