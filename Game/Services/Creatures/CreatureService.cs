using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
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
    bool HoldSummon(GameClient client, uint summonHandle, bool hold) => false;
    bool ShowNameChange(GameClient client, uint summonOrSlot) => false;
    int NameIdOf(CreatureCard card) => 0;
    Task ChangeNameAsync(GameClient client, string name) => Task.CompletedTask;
    void SetSp(GameClient client, CreatureCard card, int sp) { }
    void SyncSp(GameClient client, uint summonHandle, int maxSp) { }

    /// <summary>After a warp the summons in the world follow their master.</summary>
    void FollowWarp(GameClient client);

    /// <summary>A summon of <paramref name="master"/> in the world, as a monster sees it to fight it.</summary>
    bool TryGetSummonTarget(GameClient master, uint summonHandle, out SummonTarget target);

    /// <summary>A monster's (or a player's) hit on a summon; the HP left.</summary>
    int DamageSummon(GameClient master, uint summonHandle, int damage, bool byMonster = true);

    /// <summary>A 402 naming one of the client's summons; false when the handle is not one.</summary>
    Task<bool> TryLearnSkillAsync(GameClient client, GameActionPackets.LearnSkillRequest request);

    /// <summary>452: the skills of a card's summon.</summary>
    void SendCardSkillList(GameClient client, uint itemHandle);

    /// <summary><c>/ride &lt;handle&gt;</c>: the master mounts its summon.</summary>
    bool Mount(GameClient client, uint summonHandle);

    /// <summary><c>/unride</c>, or a fall.</summary>
    void Unmount(GameClient client, sbyte flag = 0);

    /// <summary>The speed byte of a rider, null when not riding.</summary>
    byte? RidingSpeed(ConnectionInfo info);

    IReadOnlyList<CreatureCard> FormedCards(ConnectionInfo info);

    CreatureCard FindCard(ConnectionInfo info, uint cardHandle);

    bool IsOut(ConnectionInfo info, CreatureCard card);

    int FormOf(CreatureCard card);

    (int Hp, int MaxHp, int Mp, int MaxMp) VitalsOf(GameClient client, CreatureCard card);

    void SetSummonVitals(GameClient master, CreatureCard card, int hp, int mp);

    bool Evolve(GameClient client, CreatureCard card);

    /// <summary>
    /// <c>TM_CS_PUTON_ITEM</c> (200) on a summon handle (<c>onPutonItem</c> with a summon target): answers the request
    /// itself, <c>AccessDenied</c> when the handle is not one of the master's summons.
    /// </summary>
    Task EquipItemAsync(GameClient client, GameActionPackets.PutonItemRequest request) => Task.CompletedTask;

    /// <summary><c>TM_CS_PUTOFF_ITEM</c> (201) on a summon handle (<c>onPutoffItem</c>).</summary>
    Task UnequipItemAsync(GameClient client, GameActionPackets.PutoffItemRequest request) => Task.CompletedTask;
}

/// <summary>A summon in the world as a monster fights it: where it is, how alive it is, and its body and stats.</summary>
public readonly record struct SummonTarget(uint Handle, float X, float Y, byte Layer, int Hp, int Level,
    StatBlock Stats, float Size, float Scale);

/// <summary>
/// Taming, formation and summoning (docs/packet-specs/socle-apprivoisement-invocation.md §15), ported from the
/// official server source: <c>GameProc.cpp</c> (<c>SetTamer</c>, <c>ClearTamer</c>, <c>ProcTame</c>,
/// <c>AllocNewSummon</c>), <c>StructPlayer::EquipSummon</c>/<c>DoSummon</c>/<c>DoUnSummon</c>,
/// <c>StructSkill</c>'s taming checks and <c>BroadcastTamingMessage</c>.
/// </summary>
public sealed partial class CreatureService : ICreatureService, ICreatureEventListener, IDisposable
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
    private readonly SkillCatalog _skillTrees;
    private readonly IPkFieldService _pkFields;
    private readonly ISkillPassiveCatalog _passives;
    private readonly Rates.IRateService _rates;

    /// <summary>Dead summons still in the world, by handle, with their master and the tick they died at.</summary>
    private readonly Dictionary<uint, (GameClient Master, uint Since)> _deadSince = new();
    private readonly CancellationTokenSource _stop = new();

    private readonly Progression.ITitleService _titles;
    private readonly IItemWearCatalog _wearCatalog;
    private readonly IItemMatchCatalog _itemMatch;
    private readonly Casting.ICastInterrupts _castInterrupts;
    private readonly Stats.IItemStatCatalog _itemStats;
    private readonly IEtherealWear _ethereal;

    public CreatureService(ICreatureCatalog catalog, ICharacterService characters, MonsterWorldState world,
        ICombatService combat, SummonWorldService summons, IPlayerVisibilityService players = null,
        IPartyService parties = null, IBuffCatalog skills = null, CreatureEvents events = null,
        ICombatRandom random = null, bool runTicks = true, SkillCatalog skillTrees = null,
        ISkillPassiveCatalog passives = null, Rates.IRateService rates = null, IPkFieldService pkFields = null,
        Progression.ITitleService titles = null, IItemWearCatalog wearCatalog = null,
        IItemMatchCatalog itemMatch = null, Stats.IItemStatCatalog itemStats = null,
        Casting.ICastInterrupts castInterrupts = null, IEtherealWear ethereal = null)
    {
        _ethereal = ethereal;
        _wearCatalog = wearCatalog;
        _castInterrupts = castInterrupts;
        _itemMatch = itemMatch;
        _itemStats = itemStats;
        _titles = titles;
        _pkFields = pkFields;
        _skillTrees = skillTrees;
        _passives = passives;
        _rates = rates;
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

        IReadOnlyList<SummonSkillRecord> skills;
        try
        {
            skills = await _characters.GetSummonSkillsAsync(info.CharacterName) ?? Array.Empty<SummonSkillRecord>();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the summon skills of {clientTag}", client.ClientTag);
            skills = Array.Empty<SummonSkillRecord>();
        }

        IReadOnlyList<ItemEntity> equipment;
        try
        {
            equipment = await _characters.GetSummonEquipmentAsync(info.CharacterName) ?? Array.Empty<ItemEntity>();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the summon equipment of {clientTag}", client.ClientTag);
            equipment = Array.Empty<ItemEntity>();
        }

        CreatureCard main = null;
        lock (info.SummonLock)
        {
            info.CreatureCards.Clear();
            foreach (var record in state.Cards)
            {
                var card = ToCard(record);
                info.CreatureCards[card.ItemId] = card;
                foreach (var worn in equipment.Where(item => card.HasSummon && SummonWearRules.IsWornBy(item, card.SummonId)))
                {
                    card.Equipment.Add(new SummonWornItem(worn.Id, (int)worn.ItemResourceId, (int)worn.WearInfo,
                        worn.Enhance, EtherealWearRules.Exhausted(worn, _itemMatch)));
                }
            }

            foreach (var skill in skills)
            {
                var owner = info.CreatureCards.Values.FirstOrDefault(c => c.SummonId == skill.SummonId);
                if (owner is not null)
                {
                    owner.Skills[skill.SkillId] = skill.Level;
                }
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

        // The inventory went out with the summons' items unworn: each one now goes on its summon, whose handle the
        // client has just learnt (SendItemWearInfoMessage of DB_Login's summon branch).
        foreach (var card in SlottedCards(info))
        {
            foreach (var worn in equipment.Where(item => SummonWearRules.IsWornBy(item, card.SummonId)))
            {
                SendSummonItemWear(client, card, worn);
            }
        }

        // The main summon comes back with its master (StructPlayer::onLogin, AddNoise 50).
        if (main is not null && main.HasSummon && Array.IndexOf(info.SummonSlots, main.ItemId) >= 0)
        {
            EnterWorld(client, main, SummonWorldService.LoginNoiseRange);
        }

        // DB_Login: the formation and the cards held count for the titles once they are loaded.
        _ = _titles?.RefreshAsync(client);
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
            _castInterrupts?.ForgetSummon(client, summon.Handle);
            lock (_lock)
            {
                _moves.Remove(summon.Handle);
                _deadSince.Remove(summon.Handle);
            }
        }

        // The summons' exp, level and vitals go with the master's save (Summons rows, DB_UpdateSummon).
        var info = client.ConnectionInfo;
        info.RideHandle = 0;
        foreach (var (card, presence) in OutCards(info))
        {
            card.Hp = presence.Hp;
            card.Mp = presence.Mp;
            card.HpKnown = true;
        }

        List<CreatureCard> cards;
        lock (info.SummonLock)
        {
            cards = info.CreatureCards.Values.Where(c => c.HasSummon).ToList();
        }

        if (cards.Count > 0 && !string.IsNullOrEmpty(info.CharacterName))
        {
            _ = SaveProgressAsync(info, cards);
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
        Mp = record.Summon?.Mp ?? 0,
        HpKnown = record.Summon is not null,
        Jp = record.Summon?.Jp ?? 0,
        MaxReachedLevel = Math.Max(1, Math.Max(record.Summon?.MaxLevel ?? 1, record.Summon?.Lv ?? 1)),
        LastDecreasedExp = record.Summon?.LastDecreasedExp ?? 0,
        PreviousSummonIds = Pad(record.Summon?.PreviousSummonResourceIds),
        PreviousLevels = Pad(record.Summon?.PreviousLevel)
    };

    private static T[] Pad<T>(T[] values)
    {
        var padded = new T[2];
        if (values is not null)
        {
            Array.Copy(values, padded, Math.Min(values.Length, padded.Length));
        }

        return padded;
    }

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

        var stats = StatsOf(client.ConnectionInfo, card, resource);
        var (hp, mp) = Vitals(card, stats);
        client.Connection.Send(GameSummonPackets.BuildAddSummonInfo(card.Handle, card.SummonHandle, card.SummonName,
            card.SummonCode, card.Level, card.Sp));
        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, stats, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, new StatBlock(), StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildHpMp(card.SummonHandle, 0, hp, (int)stats.MaxHp, 0, mp,
            (int)stats.MaxMp));
        client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(card.SummonHandle, card.Level, card.Level));
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(card.SummonHandle, card.Exp, card.Jp));
        SendSp(client, card, stats);
        // StructPlayer::AddSummon: SendSkillMessage after the summon's information.
        client.Connection.Send(GameCharacterPackets.BuildSkillList(card.SummonHandle, SkillEntriesOf(client.ConnectionInfo,
            card)));
        card.InfoSent = true;
    }

    /// <summary>A summon never saved with vitals (a fresh one) starts full.</summary>
    private static (int Hp, int Mp) Vitals(CreatureCard card, StatBlock stats)
    {
        var maxHp = Math.Max(1, (int)stats.MaxHp);
        var maxMp = Math.Max(0, (int)stats.MaxMp);
        // A row never written with vitals starts full; a known 0 HP is a dead summon and stays dead.
        var hp = !card.HpKnown ? maxHp : Math.Clamp(card.Hp, 0, maxHp);
        var mp = !card.HpKnown ? maxMp : Math.Clamp(card.Mp, 0, maxMp);
        return (hp, mp);
    }

    private static int CreatureMastery(ConnectionInfo info) =>
        info.LearnedSkills.TryGetValue(SummonProgression.CreatureMasterySkill, out var level) ? level : 0;

    /// <summary>The master's side of a summon's stats: its level, its Creature Mastery and the card's enhance.</summary>
    private SummonStatContext Context(ConnectionInfo info, CreatureCard card) =>
        new(Math.Max(1, info.CharacterLevel), CreatureMastery(info), _catalog.Enhance(card.Enhance).StatAmplify);

    private StatBlock StatsOf(ConnectionInfo info, CreatureCard card, SummonResourceInfo resource)
    {
        var block = CreatureRules.SummonStats(resource, card.Level, Context(info, card));
        block.MaxSp = 1000;
        var masterSp = _passives is null ? Array.Empty<StatEffect>()
            : info.LearnedSkills.SelectMany(s => _passives.ResolveSummonSp(s.Key, s.Value)).ToArray();
        // StructSummon::CalculateStat: bit 23 means MaxSP here. Flat additions precede amplification.
        var worn = ItemEffectsOf(info, card).Select(e => e.Target == StatTarget.MaxStamina
            ? e with { Target = StatTarget.MaxSp } : e).ToArray();
        StatCalculator.ApplyEffects(block, worn, masterSp);

        if (_passives is null)
        {
            return block;
        }

        // The summon's own passives (StructSummon::applyPassiveSkillEffect): a summon carries no weapon, so only
        // the passives that need none apply.
        var effects = SkillsOf(info, card).SelectMany(skill => _passives.Resolve(skill.Key, skill.Value, null))
            .ToArray();
        if (effects.Length > 0)
        {
            StatCalculator.ApplyEffects(block, effects);
        }

        return block;
    }

    private StatEffect[] ItemEffectsOf(ConnectionInfo info, CreatureCard card)
    {
        if (_itemStats is null)
        {
            return Array.Empty<StatEffect>();
        }

        lock (info.SummonLock)
        {
            return card.Equipment.Where(item => !item.Exhausted).SelectMany(item => _itemStats.GetEffects(item.ResourceId)).ToArray();
        }
    }

    public void OnEquipmentDurabilityChanged(GameClient client, IReadOnlyList<ItemEntity> items)
    {
        var info = client.ConnectionInfo;
        var affected = new List<CreatureCard>();
        lock (info.SummonLock)
            foreach (var card in info.CreatureCards.Values)
                for (var index = 0; index < card.Equipment.Count; index++)
                {
                    var worn = card.Equipment[index];
                    var item = items.FirstOrDefault(i => i.Id == worn.ItemId);
                    if (item is null) continue;
                    card.Equipment[index] = worn with { Exhausted = EtherealWearRules.Exhausted(item, _itemMatch) };
                    if (!affected.Contains(card)) affected.Add(card);
                }
        foreach (var card in affected) RefreshEquippedSummon(client, card);
    }

    // ---- equipment ----------------------------------------------------------------------------------------------

    private const ushort PutOnRequestId = (ushort)GamePackets.TM_CS_PUTON_ITEM;
    private const ushort PutOffRequestId = (ushort)GamePackets.TM_CS_PUTOFF_ITEM;

    private CreatureCard SummonOf(ConnectionInfo info, uint summonHandle)
    {
        lock (info.SummonLock)
        {
            return info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle && c.HasSummon);
        }
    }

    public async Task EquipItemAsync(GameClient client, GameActionPackets.PutonItemRequest request)
    {
        var info = client.ConnectionInfo;
        var card = SummonOf(info, request.TargetHandle);
        if (card is null)
        {
            client.SendResult(PutOnRequestId, (ushort)ResultCode.AccessDenied, 0);
            return;
        }

        if (info.CharacterHp <= 0 || _wearCatalog is null)
        {
            client.SendResult(PutOnRequestId, (ushort)ResultCode.NotActable, 0);
            return;
        }

        var item = await _characters.GetItemByHandleAsync(info.CharacterName, request.ItemHandle);
        if (item is null)
        {
            client.SendResult(PutOnRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        // StructSummon::TranslateWearPosition: a card-form item, wearable, at the summon's level.
        if (!SummonWearRules.IsCardForm(item.Flag) || !_wearCatalog.TryGetWearFields(item.ItemResourceId, out var fields)
            || !ItemWearRules.IsWearAllowed(fields, card.Level))
        {
            client.SendResult(PutOnRequestId, (ushort)ResultCode.NotActable, 0);
            return;
        }

        var group = _itemMatch is not null && _itemMatch.TryGetFields(item.ItemResourceId, out var match)
            ? (ItemGroup)match.Group : ItemGroup.Etc;
        var slots = _catalog.Enhance(card.Enhance).SlotAmount;
        SummonEquipResult result;
        try
        {
            result = await _characters.EquipSummonItemAsync(info.CharacterName, request.ItemHandle, card.SummonId,
                (_, worn) => SummonWearRules.Resolve(request.Position, group, worn.Select(entry =>
                    new SummonWearRules.Worn((int)entry.WearInfo, GroupOf(entry.ItemResourceId))).ToArray(), slots));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not equip item {itemHandle} on summon {summonHandle} for {clientTag}",
                request.ItemHandle, request.TargetHandle, client.ClientTag);
            client.SendResult(PutOnRequestId, (ushort)ResultCode.DBError, 0);
            return;
        }

        if (result.Code != ResultCode.Success)
        {
            client.SendResult(PutOnRequestId, (ushort)result.Code, 0);
            return;
        }

        lock (info.SummonLock)
        {
            if (result.Displaced is { } displaced)
            {
                card.Equipment.RemoveAll(worn => worn.ItemId == displaced.Id);
            }

            card.Equipment.Add(new SummonWornItem(result.Equipped.Id, (int)result.Equipped.ItemResourceId,
                (int)result.Equipped.WearInfo, result.Equipped.Enhance, EtherealWearRules.Exhausted(result.Equipped, _itemMatch)));
        }

        // putoffItem then putonItem: the 287 of each item, on the summon's handle.
        if (result.Displaced is { } off)
        {
            SendSummonItemWear(client, card, off);
        }

        SendSummonItemWear(client, card, result.Equipped);
        RefreshEquippedSummon(client, card);
        client.SendResult(PutOnRequestId, (ushort)ResultCode.Success, 0);
    }

    public async Task UnequipItemAsync(GameClient client, GameActionPackets.PutoffItemRequest request)
    {
        var info = client.ConnectionInfo;
        var card = SummonOf(info, request.TargetHandle);
        if (card is null)
        {
            client.SendResult(PutOffRequestId, (ushort)ResultCode.AccessDenied, 0);
            return;
        }

        ItemEntity item;
        try
        {
            item = await _characters.UnequipSummonItemAsync(info.CharacterName, card.SummonId, request.Position);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not take off slot {slot} of summon {summonHandle} for {clientTag}",
                request.Position, request.TargetHandle, client.ClientTag);
            client.SendResult(PutOffRequestId, (ushort)ResultCode.DBError, 0);
            return;
        }

        if (item is null)
        {
            client.SendResult(PutOffRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        lock (info.SummonLock)
        {
            card.Equipment.RemoveAll(worn => worn.ItemId == item.Id);
        }

        SendSummonItemWear(client, card, item);
        RefreshEquippedSummon(client, card);
        client.SendResult(PutOffRequestId, (ushort)ResultCode.Success, 0);
    }

    private int GroupOf(long resourceId) =>
        _itemMatch is not null && _itemMatch.TryGetFields(resourceId, out var fields) ? (int)fields.Group : 0;

    /// <summary><c>SendItemWearInfoMessage(master, summon, item)</c>: the 287 of a summon's item.</summary>
    private static void SendSummonItemWear(GameClient client, CreatureCard card, ItemEntity item) =>
        client.Connection.Send(GameCharacterPackets.BuildItemWearInfo((uint)item.Id, (short)item.WearInfo,
            item.WearInfo == ItemWearType.None ? 0 : card.SummonHandle, (int)item.Enhance, (byte)item.ElementalEffectType));

    /// <summary>The summon's stats after a put-on or put-off: both stat packets on its handle.</summary>
    private void RefreshEquippedSummon(GameClient client, CreatureCard card)
    {
        if (!_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return;
        }

        RefreshSummonStats(client, card, resource);
        var byItem = new StatBlock();
        var effects = ItemEffectsOf(client.ConnectionInfo, card);
        if (effects.Length > 0)
        {
            StatCalculator.ApplyEffects(byItem, effects);
        }

        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, byItem, StatInfoType.ByItem));
    }

    // ---- experience -------------------------------------------------------------------------------------------

    public long OnLimitPlayerExperience(GameClient player, long exp)
    {
        var info = player.ConnectionInfo;
        var above = OutCards(info).Any(pair => pair.Card.Level > info.CharacterLevel);
        return above ? Math.Min(exp, SummonProgression.PlayerExpLimit(Math.Max(1, info.CharacterLevel))) : exp;
    }

    /// <summary>
    /// <c>distributeExpToSummons</c>: every summon out in the world, alive, within <c>VISIBLE_RANGE</c> of its master
    /// and below its level takes the master's hunting exp whole (the master loses nothing). A summon kept in its card
    /// takes <c>m_fDeactiveSummonExpAmp</c> = 0 of it: nothing.
    /// </summary>
    public void OnExperienceGained(GameClient player, long exp)
    {
        if (exp <= 0)
        {
            return;
        }

        var info = player.ConnectionInfo;
        var (px, py) = info.PositionAt(ServerClock.Now);
        foreach (var (card, presence) in OutCards(info))
        {
            if (presence.Hp <= 0 || card.Level >= info.CharacterLevel
                || SummonProgression.IsExpLimitReached(card.Exp, _catalog.NeedExp))
            {
                continue;
            }

            var (sx, sy) = SummonPosition(presence.Handle, ServerClock.Now);
            if (CombatRange.Distance(px, py, sx, sy) > SummonProgression.VisibleRange)
            {
                continue;
            }

            GainExperience(player, card, exp);
        }
    }

    /// <summary>The cards whose summon is out in the world, with their presence.</summary>
    private static List<(CreatureCard Card, SummonPresence Presence)> OutCards(ConnectionInfo info)
    {
        var result = new List<(CreatureCard, SummonPresence)>();
        lock (info.SummonLock)
        {
            foreach (var presence in info.Summons)
            {
                var card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == presence.Handle);
                if (card is not null)
                {
                    result.Add((card, presence));
                }
            }
        }

        return result;
    }

    /// <summary><c>StructSummon::AddExp</c> then <c>onExpChange</c>: the exp update, and a level-up when one is reached.</summary>
    public void GainExperience(GameClient master, CreatureCard card, long gain, bool force = false)
    {
        if (!_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return;
        }

        var info = master.ConnectionInfo;
        long exp;
        int previous;
        SummonLevelChange change;
        lock (info.SummonLock)
        {
            gain = SummonProgression.CapGain(card.Exp, gain, card.Level, Math.Max(1, resource.Form), _catalog.NeedExp,
                force);
            if (gain <= 0)
            {
                return;
            }

            card.Exp += gain;
            exp = card.Exp;
            previous = card.Level;
            change = SummonProgression.ResolveLevel(card.Exp, Math.Max(1, resource.Form), card.MaxReachedLevel,
                card.Level, _catalog.NeedExp);
            card.Level = change.Level;
            card.MaxReachedLevel = change.MaxReachedLevel;
            card.Jp += change.JpGained;
        }

        // SendExpMsg: the master's creature window.
        master.Connection.Send(GameCharacterPackets.BuildExpUpdate(card.SummonHandle, exp, card.Jp));
        if (change.LevelChanged(previous))
        {
            OnLevelChanged(master, card, resource, previous);
        }
    }

    /// <summary>
    /// The level part of <c>onExpChange</c>: stats again, full HP/MP on a gain (alive), the <c>jp</c> property, the
    /// level to the master and to whoever sees the summon, and the summon row saved (<c>DB_UpdateSummon</c>).
    /// </summary>
    private void OnLevelChanged(GameClient master, CreatureCard card, SummonResourceInfo resource, int previous)
    {
        var info = master.ConnectionInfo;
        var stats = StatsOf(info, card, resource);
        var presence = Array.Find(info.Summons, s => s.Handle == card.SummonHandle);
        var gained = card.Level > previous;
        int prevHp, prevMp, hp, mp;
        lock (info.SummonLock)
        {
            prevHp = presence?.Hp ?? card.Hp;
            prevMp = presence?.Mp ?? card.Mp;
            hp = prevHp;
            mp = prevMp;
            if (gained && !(card.IsDead || presence is { Hp: <= 0 }))
            {
                hp = (int)stats.MaxHp;
                mp = (int)stats.MaxMp;
            }

            card.Hp = hp;
            card.Mp = mp;
            card.HpKnown = true;
        }

        if (presence is not null)
        {
            presence.Entry.Level = card.Level;
            _summons.RefreshStats(presence, stats);
            presence.Hp = Math.Min(hp, (int)presence.Stats.MaxHp);
            presence.Mp = Math.Min(mp, (int)presence.Stats.MaxMp);
            hp = presence.Hp;
            mp = presence.Mp;
        }

        var maxHp = (int)(presence?.Stats ?? stats).MaxHp;
        var maxMp = (int)(presence?.Stats ?? stats).MaxMp;
        var hpmp = GameStatPackets.BuildHpMp(card.SummonHandle, hp - prevHp, hp, maxHp, mp - prevMp, mp, maxMp);
        var level = GameCharacterPackets.BuildLevelUpdate(card.SummonHandle, card.Level, card.Level);
        master.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, presence?.Stats ?? stats,
            StatInfoType.Total));
        master.Connection.Send(hpmp);
        if (gained)
        {
            master.Connection.Send(GameStatPackets.BuildProperty(card.SummonHandle, "jp", card.Jp));
        }

        master.Connection.Send(level);
        if (presence is not null)
        {
            _players?.SendToObservers(master, hpmp);
            _players?.SendToObservers(master, level);
        }

        _ = SaveProgressAsync(info, new[] { card });
    }

    private async Task SaveProgressAsync(ConnectionInfo info, IReadOnlyList<CreatureCard> cards)
    {
        try
        {
            var progress = cards.Where(c => c.HasSummon).Select(c => c.Progress()).ToList();
            await _characters.SaveSummonProgressAsync(info.CharacterName, progress);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save the summons of {name}", info.CharacterName);
        }
    }

    // ---- a summon in a fight ---------------------------------------------------------------------------------

    /// <summary><c>GameRule::GetDeadSummonHoldTime</c>: a dead summon stays 60 s in the world, then is sent back.</summary>
    public const uint DeadHoldTicks = 6000;

    public bool TryGetSummonTarget(GameClient master, uint summonHandle, out SummonTarget target)
    {
        target = default;
        var info = master.ConnectionInfo;
        var presence = Array.Find(info.Summons, s => s.Handle == summonHandle);
        if (presence is null)
        {
            return false;
        }

        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
        }

        if (card is null || !_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return false;
        }

        var (x, y) = SummonPosition(summonHandle, ServerClock.Now);
        target = new SummonTarget(summonHandle, x, y, presence.Layer, presence.Hp, card.Level, presence.Stats,
            resource.Size, resource.Scale);
        return true;
    }

    /// <summary>
    /// <c>StructSummon::onDamage</c>: the summon loses HP, its master's creature window and whoever sees it get the
    /// vitals (509), and at 0 HP it dies (<see cref="OnSummonDeath"/>). Returns the HP left.
    /// </summary>
    public int DamageSummon(GameClient master, uint summonHandle, int damage, bool byMonster = true)
    {
        var info = master.ConnectionInfo;
        var presence = Array.Find(info.Summons, s => s.Handle == summonHandle);
        if (presence is null)
        {
            return 0;
        }

        int hp, mp, maxHp, maxMp;
        bool died;
        lock (info.SummonLock)
        {
            if (presence.Hp <= 0)
            {
                return 0;
            }

            var card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
            if (card is not null) _ethereal?.Hit(master, false, damage, summon: card);

            presence.Hp = Math.Max(0, presence.Hp - Math.Max(0, damage));
            hp = presence.Hp;
            mp = presence.Mp;
            maxHp = (int)presence.Stats.MaxHp;
            maxMp = (int)presence.Stats.MaxMp;
            died = hp == 0;
        }

        var frame = GameStatPackets.BuildHpMp(summonHandle, -Math.Max(0, damage), hp, maxHp, 0, mp, maxMp);
        if (died) _castInterrupts?.ForgetSummon(master, summonHandle);
        else if (damage > 0) _castInterrupts?.SummonDamaged(master, summonHandle, damage);
        master.Connection.Send(frame);
        _players?.SendToObservers(master, frame);

        // StructSummon::onDamage: a hit on the mount throws its rider off 30 times in 100.
        if (!died && damage > 0 && info.RideHandle == summonHandle && _random.Next(100) <= UnmountProbabilityOnDamage)
        {
            Unmount(master, UnmountFall);
        }

        if (died)
        {
            OnSummonDeath(master, summonHandle, byMonster);
        }

        return hp;
    }

    /// <summary>
    /// <c>StructSummon::onDead</c>: the summon stops fighting, the monsters forget it, a death by a monster above
    /// level 5 costs <c>GetDeadEXPPenalty</c> (a level can be lost, no JP is given back), and it is sent back after
    /// <see cref="DeadHoldTicks"/>. It stays dead — 0 HP on its card — until revived.
    /// </summary>
    private void OnSummonDeath(GameClient master, uint summonHandle, bool byMonster)
    {
        var info = master.ConnectionInfo;
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
            if (card is not null)
            {
                card.Hp = 0;
                card.HpKnown = true;
            }
        }

        var now = ServerClock.Now;
        var (x, y) = SummonPosition(summonHandle, now);
        lock (_lock)
        {
            _swings.Remove(summonHandle);
            _deadSince[summonHandle] = (master, now);
            if (_moves.TryGetValue(summonHandle, out var move))
            {
                move.X = move.DestX = x;
                move.Y = move.DestY = y;
                move.StartTick = now;
            }
        }

        _world.ForgetSummon(master, summonHandle);
        if (info.RideHandle == summonHandle)
        {
            Unmount(master, UnmountFall);
        }

        if (card is null)
        {
            return;
        }

        if (byMonster && _catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            var penalty = SummonProgression.DeathPenalty(card.Level, _catalog.NeedExp);
            if (penalty > 0)
            {
                int previous;
                SummonLevelChange change;
                lock (info.SummonLock)
                {
                    penalty = Math.Min(penalty, card.Exp);
                    card.Exp -= penalty;
                    card.LastDecreasedExp = penalty;
                    previous = card.Level;
                    change = SummonProgression.ResolveLevel(card.Exp, Math.Max(1, resource.Form), card.MaxReachedLevel,
                        card.Level, _catalog.NeedExp);
                    card.Level = change.Level;
                }

                master.Connection.Send(GameCharacterPackets.BuildExpUpdate(summonHandle, card.Exp, card.Jp));
                if (change.LevelChanged(previous))
                {
                    OnLevelChanged(master, card, resource, previous);
                    return;
                }
            }
        }

        _ = SaveProgressAsync(info, new[] { card });
    }

    /// <summary>
    /// A summon's vitals set from outside the fight (the creature keeper's care, a revival): alive again when its HP
    /// rises above 0, which takes it off the dead hold.
    /// </summary>
    public void SetSummonVitals(GameClient master, CreatureCard card, int hp, int mp)
    {
        var info = master.ConnectionInfo;
        var presence = Array.Find(info.Summons, s => s.Handle == card.SummonHandle);
        int maxHp, maxMp;
        lock (info.SummonLock)
        {
            if (presence is not null)
            {
                maxHp = (int)presence.Stats.MaxHp;
                maxMp = (int)presence.Stats.MaxMp;
                presence.Hp = Math.Clamp(hp, 0, maxHp);
                presence.Mp = Math.Clamp(mp, 0, maxMp);
                hp = presence.Hp;
                mp = presence.Mp;
            }
            else
            {
                maxHp = Math.Max(hp, 1);
                maxMp = Math.Max(mp, 0);
            }

            card.Hp = hp;
            card.Mp = mp;
            card.HpKnown = true;
        }

        if (hp > 0)
        {
            lock (_lock)
            {
                _deadSince.Remove(card.SummonHandle);
            }
        }

        if (card.SummonHandle != 0)
        {
            var frame = GameStatPackets.BuildHpMp(card.SummonHandle, 0, hp, maxHp, 0, mp, maxMp);
            master.Connection.Send(frame);
            if (presence is not null)
            {
                _players?.SendToObservers(master, frame);
            }
        }

        _ = SaveProgressAsync(info, new[] { card });
    }

    /// <summary>A dead summon is sent back once its hold is over (<c>SummonAI.cpp</c>, <c>PendUnSummon</c>).</summary>
    public void ProcessDeadSummons(uint now)
    {
        List<(uint Handle, GameClient Master)> due = null;
        lock (_lock)
        {
            foreach (var (handle, (master, since)) in _deadSince)
            {
                if (unchecked((int)(now - since)) >= (int)DeadHoldTicks)
                {
                    (due ??= new List<(uint, GameClient)>()).Add((handle, master));
                }
            }

            if (due is not null)
            {
                foreach (var (handle, _) in due)
                {
                    _deadSince.Remove(handle);
                }
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (var (handle, master) in due)
        {
            CreatureCard card;
            lock (master.ConnectionInfo.SummonLock)
            {
                card = master.ConnectionInfo.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == handle);
            }

            if (card is not null && IsCardInWorld(master.ConnectionInfo, card.ItemId))
            {
                Unsummon(master, card.Handle);
            }
        }
    }

    /// <summary>
    /// <c>StructCreature::regenHPMP</c> for the summons out in the world: every 3 s, the regeneration of their stats,
    /// nothing for a dead one (516 to the master and to whoever sees it).
    /// </summary>
    public void ProcessRegeneration()
    {
        var clients = _players?.Registry?.Clients;
        if (clients is null)
        {
            return;
        }

        foreach (var master in clients)
        {
            foreach (var (_, presence) in OutCards(master.ConnectionInfo))
            {
                int hpGain, mpGain, hp, mp;
                lock (master.ConnectionInfo.SummonLock)
                {
                    if (presence.Hp <= 0)
                    {
                        continue;
                    }

                    var stats = presence.Stats;
                    var maxHp = (int)stats.MaxHp;
                    var maxMp = (int)stats.MaxMp;
                    hp = Math.Min(maxHp, presence.Hp + PlayerRegenerationService.Gain(maxHp, stats.HpRegenPoint,
                        stats.HpRegenPercentage, 1));
                    mp = Math.Min(maxMp, presence.Mp + PlayerRegenerationService.Gain(maxMp, stats.MpRegenPoint,
                        stats.MpRegenPercentage, 1));
                    hpGain = hp - presence.Hp;
                    mpGain = mp - presence.Mp;
                    if (hpGain == 0 && mpGain == 0)
                    {
                        continue;
                    }

                    presence.Hp = hp;
                    presence.Mp = mp;
                }

                var frame = GameStatPackets.BuildRegenHpMp(presence.Handle, hpGain, mpGain, hp, mp);
                master.Connection.Send(frame);
                _players.SendToObservers(master, frame);
            }
        }
    }

    // ---- skills ----------------------------------------------------------------------------------------------

    /// <summary><c>SKILL_CREATURE_RIDING</c>: a summon that learned it can be ridden (<c>onCompleteCalculateStat</c>).</summary>
    public const int CreatureRidingSkill = 11001;

    private const ushort LearnSkillRequestId = (ushort)GamePackets.TM_CS_LEARN_SKILL;

    /// <summary>
    /// <c>TM_CS_LEARN_SKILL</c> (402) naming one of the client's summons: <c>StructSummon::IsLearnableSkill</c> — the
    /// trees of the forms left behind first, each at the level it reached, then the current form's at the summon's
    /// level, the card's enhance bounding each rule — paid with the summon's JP. False when the handle is not a summon
    /// of this client, so the player's own path answers it.
    /// </summary>
    public async Task<bool> TryLearnSkillAsync(GameClient client, GameActionPackets.LearnSkillRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.Handle == 0 || request.Handle == info.CharacterHandle)
        {
            return false;
        }

        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == request.Handle && c.HasSummon);
        }

        if (card is null)
        {
            return false;
        }

        if (_skillTrees is null)
        {
            client.SendResult(LearnSkillRequestId, (ushort)ResultCode.NotActable, request.SkillId);
            return true;
        }

        byte currentLevel;
        Dictionary<int, byte> learned;
        List<(int, int)> forms;
        lock (info.SummonLock)
        {
            currentLevel = card.Skills.GetValueOrDefault(request.SkillId);
            learned = new Dictionary<int, byte>(card.Skills);
            forms = Enumerable.Range(0, 2).Where(i => card.PreviousSummonIds[i] != 0)
                .Select(i => ((int)card.PreviousSummonIds[i], card.PreviousLevels[i])).ToList();
        }

        var evaluation = _skillTrees.EvaluateAcrossJobs(forms, card.SummonCode, card.Level, card.Level,
            request.SkillId, currentLevel, request.TargetLevel, learned, card.Jp, _rates?.SkillJpCost ?? 1,
            card.Enhance);
        if (!evaluation.IsSuccess)
        {
            client.SendResult(LearnSkillRequestId, (ushort)evaluation.Result, request.SkillId);
            return true;
        }

        var remaining = (int)Math.Max(0, card.Jp - evaluation.Cost);
        if (!await _characters.SaveSummonSkillAsync(info.CharacterName, card.SummonId, request.SkillId,
                request.TargetLevel, remaining))
        {
            client.SendResult(LearnSkillRequestId, (ushort)ResultCode.DBError, request.SkillId);
            return true;
        }

        lock (info.SummonLock)
        {
            card.Jp = remaining;
            card.Skills[request.SkillId] = request.TargetLevel;
        }

        // onJPChange then onRegisterSkill: the summon's JP, its new skill level, then the result.
        client.Connection.Send(GameStatPackets.BuildProperty(card.SummonHandle, "jp", card.Jp));
        client.Connection.Send(GameCharacterPackets.BuildSkillList(card.SummonHandle,
            new[] { new KeyValuePair<int, byte>(request.SkillId, request.TargetLevel) }));
        client.SendResult(LearnSkillRequestId, (ushort)ResultCode.Success, request.SkillId);

        // A passive (Creature Riding among them) moves the summon's stats.
        if (_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            RefreshSummonStats(client, card, resource);
        }

        return true;
    }

    /// <summary>The stats of a summon whose skills or master changed, out in the world or kept in its card.</summary>
    private void RefreshSummonStats(GameClient client, CreatureCard card, SummonResourceInfo resource)
    {
        var info = client.ConnectionInfo;
        var stats = StatsOf(info, card, resource);
        var presence = Array.Find(info.Summons, s => s.Handle == card.SummonHandle);
        if (presence is not null)
        {
            _summons.RefreshStats(presence, stats);
            stats = presence.Stats;
        }

        client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, stats, StatInfoType.Total));
        SendSp(client, card, stats);
    }

    /// <summary>onSummonCardSkillList: 451 with base levels, or an empty list for an unbound card.</summary>
    public void SendCardSkillList(GameClient client, uint itemHandle)
    {
        var info = client.ConnectionInfo;
        CreatureCard card;
        lock (info.SummonLock)
            card = info.CreatureCards.Values.FirstOrDefault(c => c.Handle == itemHandle && c.Amount > 0);
        if (card is null)
        {
            client.SendResult((ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST, (ushort)ResultCode.NotExist);
            return;
        }
        client.Connection.Send(GameSmallPackets.SkillLevels(card.HasSummon ? SkillsOf(info, card)
            : Array.Empty<KeyValuePair<int, byte>>()));
    }

    private static SkillListEntry[] SkillEntriesOf(ConnectionInfo info, CreatureCard card)
    {
        var now = ServerClock.Now;
        lock (info.SummonLock)
            return card.Skills.Select(s => new SkillListEntry(s.Key, s.Value,
                card.SkillCooldownDurations.GetValueOrDefault(s.Key),
                card.SkillCooldowns.TryGetValue(s.Key, out var ready)
                    ? (uint)Math.Max(0, unchecked((int)(ready - now))) : 0)).ToArray();
    }

    private static KeyValuePair<int, byte>[] SkillsOf(ConnectionInfo info, CreatureCard card)
    {
        lock (info.SummonLock)
        {
            return card.Skills.ToArray();
        }
    }

    /// <summary>Whether a summon may be ridden: it learned Creature Riding (11001).</summary>
    public static bool IsRidable(CreatureCard card) => card.Skills.ContainsKey(CreatureRidingSkill);

    // ---- mount ---------------------------------------------------------------------------------------------------

    public const sbyte UnmountNormal = 0;
    public const sbyte UnmountFall = 1;
    public const sbyte UnmountUnsummon = 2;

    /// <summary><c>GameRule::DEFAULT_UNMOUNT_PROBABILITY_ON_DAMAGE</c>: the chance in 100 a hit throws the rider off.</summary>
    public const int UnmountProbabilityOnDamage = 30;

    /// <summary><c>GameRule::UNMOUNT_PENALTY</c>: a fall costs 5 % of the rider's max HP.</summary>
    public const float UnmountPenalty = 0.05f;

    /// <summary>The location types a summon cannot be ridden in (<c>IsMountable</c>: secret and instance dungeons,
    /// battle arena, prayer hall).</summary>
    private static readonly short[] UnmountableLocations = { 12, 14, 15, 16 };

    /// <summary>
    /// <c>/ride &lt;handle&gt;</c>, <c>StructPlayer::MountSummon</c>: the master, able to act, not sitting, not in a
    /// dungeon, mounts its summon out in the world when it is alive and learned Creature Riding. 320 to everyone who
    /// sees the master on success, to the master alone (success 0) otherwise.
    /// </summary>
    public bool Mount(GameClient client, uint summonHandle)
    {
        var info = client.ConnectionInfo;
        var presence = Array.Find(info.Summons, s => s.Handle == summonHandle);
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
        }

        var (x, y) = info.PositionAt(ServerClock.Now);
        var mountable = info.RideHandle == 0 && info.CharacterHp > 0 && !info.IsSitting
                        && !SummonFall.IsActive(info, ServerClock.Now)
                        && presence is { Hp: > 0 } && card is not null && IsRidable(card)
                        && !Progression.MonsterRewardBonuses.InDungeon(x, y)
                        && Array.IndexOf(UnmountableLocations, _pkFields?.LocationType(info) ?? (short)0) < 0;
        if (!mountable)
        {
            client.Connection.Send(GameSummonPackets.BuildMountSummon(info.CharacterHandle, summonHandle, 0, 0, false));
            return false;
        }

        info.RideHandle = summonHandle;
        _combat.StopAttack(client);
        lock (_lock)
        {
            _swings.Remove(summonHandle);
        }

        var frame = GameSummonPackets.BuildMountSummon(info.CharacterHandle, summonHandle, x, y, true);
        client.Connection.Send(frame);
        _players?.SendToObservers(client, frame);
        // UpdateTitleConditionBySummonMount.
        _ = _titles?.RefreshAsync(client);
        return true;
    }

    /// <summary>
    /// <c>StructPlayer::UnMount</c>: 321 to everyone who sees the master; a fall (<see cref="UnmountFall"/>) costs the
    /// living rider 5 % of their max HP.
    /// </summary>
    public void Unmount(GameClient client, sbyte flag = UnmountNormal)
    {
        var info = client.ConnectionInfo;
        var summonHandle = info.RideHandle;
        if (summonHandle == 0)
        {
            return;
        }

        info.RideHandle = 0;
        var frame = GameSummonPackets.BuildUnmountSummon(info.CharacterHandle, summonHandle, flag);
        client.Connection.Send(frame);
        _players?.SendToObservers(client, frame);
        _ = _titles?.RefreshAsync(client);
        if (flag == UnmountFall && info.CharacterHp > 0)
        {
            _combat.StopAttack(client);
            var now = ServerClock.Now;
            var (x, y) = info.PositionAt(now);
            info.X = info.DestinationX = x;
            info.Y = info.DestinationY = y;
            var stop = GameMovePackets.BuildStopMove(info.CharacterHandle, unchecked(now + info.ClientClockOffset), info.Layer);
            client.Connection.Send(stop);
            _players?.SendToObservers(client, stop);
            _castInterrupts?.ApplyState(client, SummonFall.StateId, 1, SummonFall.Duration);
            _combat.DamagePlayer(client, (int)(Math.Max(0, info.CharacterMaxHp) * UnmountPenalty));
        }
    }

    /// <summary>
    /// The speed byte of a rider (<c>StructSummon::GetRidingMoveSpeed</c>: <c>riding_speed / 7 × m_fRideSpeedMod</c>,
    /// the modifier 0.9 plus Creature Riding's <c>var0 + var1 × level</c>, at most 1), null when not riding. The rider
    /// keeps its own speed when that is faster (<c>IsApplyingFasterSpeedInRiding</c>).
    /// </summary>
    public byte? RidingSpeed(ConnectionInfo info)
    {
        var summonHandle = info.RideHandle;
        if (summonHandle == 0)
        {
            return null;
        }

        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
        }

        if (card is null || !_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return null;
        }

        var level = card.Skills.GetValueOrDefault(CreatureRidingSkill);
        var (var0, var1) = (0.1m, 0.01m);
        if (_skills is not null && _skills.TryGet(CreatureRidingSkill, out var fields) && fields.Vars is { Length: >= 2 } vars)
        {
            (var0, var1) = (vars[0], vars[1]);
        }

        var modifier = Math.Min(1m, 0.9m + var0 + var1 * level);
        return (byte)Math.Clamp((int)(resource.RidingSpeed / 7m * modifier), 0, byte.MaxValue);
    }

    public void OnSummonReflected(GameClient master, uint handle, int damage) => DamageSummon(master, handle, damage, byMonster: false);

    public void OnPlayerDamaged(GameClient player, int damage, bool died)
    {
        var info = player.ConnectionInfo;
        if (info.RideHandle == 0)
        {
            return;
        }

        // StructPlayer::onDead / onDamage: a dead rider falls, a hit rider falls 30 times in 100.
        if (died || (damage > 0 && _random.Next(100) <= UnmountProbabilityOnDamage))
        {
            Unmount(player, UnmountFall);
        }
    }

    // ---- the creature keeper's dialogs ---------------------------------------------------------------------

    /// <summary>The summons of the formation, in slot order (<c>get_creature_handle(0..5)</c>).</summary>
    public IReadOnlyList<CreatureCard> FormedCards(ConnectionInfo info)
    {
        lock (info.SummonLock)
        {
            return info.SummonSlots.Where(id => id != 0)
                .Select(id => info.CreatureCards.GetValueOrDefault(id))
                .Where(card => card is { HasSummon: true })
                .ToList();
        }
    }

    public CreatureCard FindCard(ConnectionInfo info, uint cardHandle)
    {
        lock (info.SummonLock)
        {
            return info.CreatureCards.Values.FirstOrDefault(c => c.Handle == cardHandle && c.HasSummon);
        }
    }

    public bool IsOut(ConnectionInfo info, CreatureCard card) => IsCardInWorld(info, card.ItemId);

    /// <summary>The summon's evolution depth (<c>evolution_depth</c>: 1 basic, 2 grown, 3 evolved).</summary>
    public int FormOf(CreatureCard card) =>
        _catalog.TryGetSummon(card.SummonCode, out var resource) ? Math.Max(1, resource.Form) : 1;

    /// <summary>The summon's HP and MP with their maxima, out in the world or kept in its card.</summary>
    public (int Hp, int MaxHp, int Mp, int MaxMp) VitalsOf(GameClient client, CreatureCard card)
    {
        var info = client.ConnectionInfo;
        var presence = Array.Find(info.Summons, s => s.Handle == card.SummonHandle);
        if (presence is not null)
        {
            lock (info.SummonLock)
            {
                return (presence.Hp, (int)presence.Stats.MaxHp, presence.Mp, (int)presence.Stats.MaxMp);
            }
        }

        if (!_catalog.TryGetSummon(card.SummonCode, out var resource))
        {
            return (0, 0, 0, 0);
        }

        var stats = StatsOf(info, card, resource);
        var (hp, mp) = Vitals(card, stats);
        return (hp, (int)stats.MaxHp, mp, (int)stats.MaxMp);
    }

    /// <summary>
    /// <c>StructSummon::DoEvolution</c> through <c>Creature_Evolution_exe</c>: a summon kept in its card, at level 50
    /// in its first form or 100 in its second, becomes its <c>evolve_target</c>; the form it leaves and the level it
    /// reached are kept (<c>ev_N_ID</c>/<c>ev_N_level</c>), the level stays, the stats follow, and the master gets
    /// 307, the stats, vitals, level and exp of the summon. False when it may not evolve.
    /// </summary>
    public bool Evolve(GameClient client, CreatureCard card)
    {
        var info = client.ConnectionInfo;
        if (!_catalog.TryGetSummon(card.SummonCode, out var resource) || resource.EvolveTarget == 0
            || !_catalog.TryGetSummon(resource.EvolveTarget, out var target) || IsCardInWorld(info, card.ItemId))
        {
            return false;
        }

        var form = Math.Max(1, resource.Form);
        var evolvable = form switch
        {
            1 => card.Level >= SummonProgression.NormalEvolveLevel,
            2 => card.Level >= SummonProgression.GrowthEvolveLevel,
            _ => false
        };
        if (!evolvable)
        {
            return false;
        }

        int previousHp, previousMp;
        lock (info.SummonLock)
        {
            previousHp = card.Hp;
            previousMp = card.Mp;
            card.PreviousSummonIds[form - 1] = card.SummonCode;
            card.PreviousLevels[form - 1] = card.Level;
            card.SummonCode = target.Id;
        }

        var stats = StatsOf(info, card, target);
        var (hp, mp) = Vitals(card, stats);
        lock (info.SummonLock)
        {
            card.Hp = hp;
            card.Mp = mp;
            card.HpKnown = true;
        }

        if (card.SummonHandle != 0)
        {
            client.Connection.Send(GameSummonPackets.BuildSummonEvolution(card.Handle, card.SummonHandle,
                card.SummonName, target.Id));
            client.Connection.Send(GameStatPackets.BuildStatInfo(card.SummonHandle, stats, StatInfoType.Total));
            client.Connection.Send(GameStatPackets.BuildHpMp(card.SummonHandle, hp - previousHp, hp, (int)stats.MaxHp,
                mp - previousMp, mp, (int)stats.MaxMp));
            client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(card.SummonHandle, card.Level, card.Level));
            client.Connection.Send(GameCharacterPackets.BuildExpUpdate(card.SummonHandle, card.Exp, card.Jp));
        }

        _ = SaveProgressAsync(info, new[] { card });
        return true;
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
                var stats = CreatureRules.SummonStats(resource, 1, new SummonStatContext(info.CharacterLevel,
                    CreatureMastery(info)));
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
            if (commit.MirrorItemId is { } mirror)
            {
                // The broken Mirror of Taming Card, and its "@243" on the item line (ProcTame).
                tamer.Connection.Send(commit.MirrorRemaining > 0
                    ? GameCharacterPackets.BuildUpdateItemCount((uint)mirror, commit.MirrorRemaining)
                    : GameCharacterPackets.BuildDestroyItem((uint)mirror));
                tamer.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Item, "@243"));
            }
            if (commit.NewCard is not null)
            {
                foreach (var frame in GameCharacterPackets.BuildInventory(new[] { commit.NewCard }))
                {
                    tamer.Connection.Send(frame);
                }
            }

            Broadcast(tamer, instanceId, monster.MonsterId,
                success ? GameSummonPackets.TamingModeSuccess : GameSummonPackets.TamingModeFailed);

            // UpdateTitleConditionBySummonTame, after the draw; the new card counts for the card conditions too.
            if (monster.TamingId != 0 && _titles is not null)
            {
                var rate = _catalog.TryGetSummon(monster.TamingId, out var tamed) ? tamed.Rate : 0;
                _ = _titles.RecordAsync(tamer, Progression.TitleEvents.SummonTame(monster.TamingId, rate, success));
            }
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

        // UpdateTitleConditionBySummonEquip: the formation conditions follow the new formation.
        _ = _titles?.RefreshAsync(client);
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

        var stats = CreatureRules.SummonStats(resource, 1, new SummonStatContext(info.CharacterLevel,
            CreatureMastery(info), _catalog.Enhance(card.Enhance).StatAmplify));
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
        card.HpKnown = true;
        card.MaxReachedLevel = Math.Max(1, summon.MaxLevel);
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
            _deadSince.Remove(card.SummonHandle);
        }

        _castInterrupts?.ForgetSummon(client, card.SummonHandle);
        _world.ForgetSummon(client, card.SummonHandle);
        if (info.RideHandle == card.SummonHandle)
        {
            Unmount(client, UnmountUnsummon);
        }

        // The card keeps the vitals its summon leaves with.
        if (Array.Find(info.Summons, s => s.Handle == card.SummonHandle) is { } presence)
        {
            card.Hp = presence.Hp;
            card.Mp = presence.Mp;
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

        var stats = StatsOf(info, card, resource);
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
            // The stats the summon buffs are folded into (SummonBuffStats.Refresh) and its swings read.
            BaseStats = stats,
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
        if (card.SkillCooldowns.Count > 0)
            client.Connection.Send(GameCharacterPackets.BuildSkillList(handle, SkillEntriesOf(info, card)));
        var presence = info.Summons.FirstOrDefault(s => s.Handle == handle);
        if (presence is not null)
        {
            presence.PositionProvider = tick => SummonPosition(handle, tick);
            SendSp(client, card, presence.Stats);
        }
        lock (_lock)
        {
            // StructPlayer::Summon: a dead summon summoned again is still dead, and is sent back after its hold.
            if (hp <= 0)
            {
                _deadSince[handle] = (client, ServerClock.Now);
            }

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
            _castInterrupts?.ForgetSummon(client, presence.Handle);
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

    private static bool IsDeadSummon(ConnectionInfo info, uint handle) =>
        Array.Find(info.Summons, s => s.Handle == handle) is { Hp: <= 0 };

    public bool OwnsSummon(GameClient client, uint handle) =>
        handle != 0 && client.ConnectionInfo.Summons.Any(summon => summon.Handle == handle);

    public bool HoldSummon(GameClient client, uint summonHandle, bool hold)
    {
        var presence = Array.Find(client.ConnectionInfo.Summons, s => s.Handle == summonHandle);
        if (presence is null || presence.Hp <= 0 || client.ConnectionInfo.RideHandle == summonHandle) return false;
        if (!hold) { presence.Held = false; return true; }
        _castInterrupts?.InterruptSummon(client, summonHandle);
        StopSwing(summonHandle, client);
        lock (_lock)
        {
            var p = SummonPosition(summonHandle, ServerClock.Now);
            if (_moves.TryGetValue(summonHandle, out var move))
            { move.X = move.DestX = p.X; move.Y = move.DestY = p.Y; move.StartTick = ServerClock.Now; }
            presence.Held = true;
        }
        var frame = GameMovePackets.BuildStopMove(summonHandle,
            unchecked(ServerClock.Now + client.ConnectionInfo.ClientClockOffset), presence.Layer);
        client.Connection.Send(frame); _players?.SendToObservers(client, frame);
        return true;
    }

    public void MoveSummon(GameClient client, uint summonHandle, float x, float y, uint clientTime, byte speedSync,
        ReadOnlySpan<byte> waypoints)
    {
        var info = client.ConnectionInfo;
        if (!OwnsSummon(client, summonHandle) || IsDeadSummon(info, summonHandle) || info.RideHandle == summonHandle)
        {
            return;
        }

        if (speedSync != 0 && Array.Find(info.Summons, s => s.Handle == summonHandle)?.Held == true) return;

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
        if (!OwnsSummon(client, summonHandle) || IsDeadSummon(info, summonHandle) || info.RideHandle == summonHandle)
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
            if (card is null || !OwnsSummon(client, handle) || monsterHandle == 0 || IsDeadSummon(info, handle)
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

            // The summon's own stats, its buffs included (SummonBuffStats), once it is in the world.
            var presence = Array.Find(info.Summons, s => s.Handle == handle);
            var stats = presence?.Stats ?? StatsOf(info, card, resource);
            var defender = _combat.GetMonsterStats(swing.TargetInstanceId) ?? new StatBlock();
            var hit = CombatFormulas.Resolve(Combatant.From(stats, card.Level), Combatant.From(defender, monster.Level),
                stats.AttackPointRight, DamageKind.Physical, 0, 0, _random);
            var intervalMs = CombatService.IntervalMs(CombatFormulas.AttackIntervalTicks(stats.AttackSpeed));
            // StructMonster::onDamage: the hate goes to the summon that hit, the kill and the reward to its master.
            var targetHp = _combat.ApplyDamage(client, swing.TargetInstanceId, monsterHandle, hit.Damage, 0);
            _ethereal?.Hit(client, true, hit.Damage, summon: card);
            if (targetHp > 0)
            {
                _world.AddSummonHate(swing.TargetInstanceId, client, handle, hit.Damage);
            }

            var summonHp = presence?.Hp ?? Vitals(card, stats).Hp;

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
                        ProcessDeadSummons(ServerClock.Now);
                    }

                    if (ticks % 30 == 0)
                    {
                        ProcessRegeneration();
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
