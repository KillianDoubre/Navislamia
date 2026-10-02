using System;
using System.Collections.Generic;
using System.Threading;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Network.Clients;

public class ConnectionInfo
{
    public List<(int Job, int JobLevel)> PreviousJobs { get; } = new();
    public IReadOnlyList<StatEffect> ItemEffects { get; set; } = Array.Empty<StatEffect>();
    public IReadOnlyList<StatEffect> PassiveEffects { get; set; } = Array.Empty<StatEffect>();
    public IReadOnlyList<StatEffect> BuffEffects { get; set; } = Array.Empty<StatEffect>();
    public int MainTitleId { get; set; }
    public IReadOnlyList<StatEffect> TitleEffects { get; set; } = Array.Empty<StatEffect>();
    public ItemType? EquippedWeapon { get; set; }

    /// <summary>
    /// What the shield slot holds when it is not a shield: a second weapon (dual wield) or the arrows of an archer,
    /// with its handle and count (docs/packet-specs/socle-mecaniques-combat.md). Seeded with the stats.
    /// </summary>
    public Navislamia.Game.Services.Combat.LeftHandItem LeftHand { get; set; }

    /// <summary>The main-hand weapon's <c>AttackRange</c> (its range × 100); 0 bare-handed.</summary>
    public float WeaponAttackRange { get; set; }

    /// <summary>The main-hand weapon's own effects, which the left hand of a dual wielder does not share.</summary>
    public IReadOnlyList<Navislamia.Game.Services.Stats.StatEffect> RightWeaponEffects { get; set; } =
        Array.Empty<Navislamia.Game.Services.Stats.StatEffect>();

    /// <summary>Guards <see cref="ActiveBuffs"/>: the expiry tick and the client thread both touch it.</summary>
    public object BuffLock { get; } = new();

    public List<ActiveBuff> ActiveBuffs { get; } = new();
    public Dictionary<uint, List<ActiveBuff>> StoredSummonBuffs { get; } = new();

    /// <summary>Active auras by <c>toggle_group</c>: one aura per group at a time.</summary>
    public Dictionary<int, int> ActiveAuras { get; } = new();

    public Dictionary<int, uint> SkillCooldowns { get; } = new();
    public SemaphoreSlim ItemUseLock { get; } = new(1, 1);
    public Dictionary<int, uint> ItemCooldowns { get; } = new();
    public ushort NextStateHandle { get; set; }
    public string AccountName { get; set; }
    public List<string> CharacterList { get; set; } = new();
    public uint CharacterHandle { get; set; }
    public byte[] WearFrame { get; set; }
    public long? PartyId { get; set; }
    public long? GuildId { get; set; }
    public int CharacterStamina { get; set; }
    public uint TargetHandle { get; set; }
    public int CharacterHp { get; set; }
    public int CharacterMaxHp { get; set; }
    public int CharacterMp { get; set; }
    public int CharacterLevel { get; set; }
    public int CharacterRace { get; set; }
    public int CharacterJob { get; set; }
    public int CharacterJobLevel { get; set; }
    public long CharacterExp { get; set; }
    public object ProgressLock { get; } = new();
    public long CharacterJp { get; set; }
    /// <summary>
    /// The gold the session holds, persisted on save. A booth trade moves gold between two sessions from
    /// two receiving threads, so a check followed by a write would let two purchases spend the same coins:
    /// every change goes through <see cref="AddGold"/>, <see cref="TryDebitGold"/> or
    /// <see cref="TryCreditGold"/>, which judge and write under <see cref="GoldLock"/>.
    /// </summary>
    public long CharacterGold
    {
        get { lock (GoldLock) { return _characterGold; } }
        set { lock (GoldLock) { _characterGold = value; } }
    }

    public readonly object GoldLock = new();

    private long _characterGold;

    /// <summary>Adds (or, negative, removes) gold without a bound, as a kill reward or a GM command does.</summary>
    public long AddGold(long amount)
    {
        lock (GoldLock)
        {
            _characterGold += amount;
            return _characterGold;
        }
    }

    /// <summary>Takes <paramref name="amount"/> if the session holds it; nothing changes otherwise.</summary>
    public bool TryDebitGold(long amount)
    {
        lock (GoldLock)
        {
            if (amount < 0 || _characterGold < amount)
            {
                return false;
            }

            _characterGold -= amount;
            return true;
        }
    }

    /// <summary>Adds <paramref name="amount"/> unless the balance would pass <paramref name="max"/>.</summary>
    public bool TryCreditGold(long amount, long max)
    {
        lock (GoldLock)
        {
            if (amount < 0 || _characterGold > max - amount)
            {
                return false;
            }

            _characterGold += amount;
            return true;
        }
    }

    /// <summary>
    /// Set while a booth purchase or sale of this session is between its checks and its database write:
    /// a second request in the meantime is dropped, like <see cref="CommercialTakeoutInProgress"/>.
    /// </summary>
    public int BoothTradeInProgress;
    public int CharacterChaos { get; set; }

    /// <summary>
    /// The creature formation as stored on the character (six item handles at most), read once at world
    /// entry. Both the login TM_EQUIP_SUMMON (303) and the answer to TM_CS_GET_SUMMON_SETUP_INFO (324)
    /// are built from it, so the two cannot disagree. One source only: sending the two frames from two
    /// different reads would make the window depend on which one the player opened.
    /// </summary>
    public long[] SummonSlots { get; set; } = Array.Empty<long>();

    /// <summary>
    /// The creature cards of the bag, by item id, with their summons (docs/packet-specs/socle-apprivoisement-invocation.md
    /// §15), loaded at world entry and kept in step by taming and formation. Guarded by <see cref="SummonLock"/>.
    /// </summary>
    public Dictionary<long, Navislamia.Game.Services.Creatures.CreatureCard> CreatureCards { get; } = new();

    /// <summary>The card item id of the summon <c>m_pMainSummon</c> names (in the world or to re-enter at login), 0 for none.</summary>
    public long MainSummonCardId { get; set; }

    /// <summary>The card a taming in progress marked (<c>ITEM_FLAG_TAMING</c>, kept in memory), 0 for none.</summary>
    public long TamingCardItemId { get; set; }

    /// <summary>The monster being tamed (<c>StructPlayer::GetTamingTarget</c>), -1 for none.</summary>
    public long TamingTargetInstanceId { get; set; } = -1;

    /// <summary>
    /// The pet the character has out, called by its cage (<c>PetSummonService</c>), or null. Guarded by
    /// <see cref="PetLock"/>: an item use and a warp can both move it. The player visibility reads the
    /// reference <b>without</b> that lock (a reference read is atomic): it holds an observer's visibility lock
    /// at that point, and the pet path takes the two in the other order.
    /// </summary>
    public Navislamia.Game.Services.Pets.ActivePet ActivePet { get; set; }

    public object PetLock { get; } = new();

    /// <summary>
    /// The summons this character has in the world (<c>SummonWorldService</c>), replaced whole on every change
    /// under <see cref="SummonLock"/> so that a reader — the player visibility, under an observer's lock —
    /// takes a consistent snapshot without it.
    /// </summary>
    public Navislamia.Game.Services.SummonPresence[] Summons { get; set; } =
        Array.Empty<Navislamia.Game.Services.SummonPresence>();

    public object SummonLock { get; } = new();

    /// <summary>
    /// The raw value of the last <c>TM_CS_SET_PET_FILTER</c> (355). Its meaning is not established: it is
    /// kept, never applied (the pet collects everything its master owns).
    /// </summary>
    public uint PetPickupFilter { get; set; }

    /// <summary>
    /// Where the character is heading: the last waypoint of its last move request, or its position after
    /// a world entry or a warp. <see cref="X"/>/<see cref="Y"/> is where it started, the destination is
    /// where a follower should go (the pet).
    /// </summary>
    public float DestinationX { get; set; }
    public float DestinationY { get; set; }

    /// <summary>
    /// The <c>TS_SC_MOVE</c> speed of a character with no stats known: the default move speed of 120 on the
    /// wire. The official <c>onMoveRequest</c> echoes <c>GetRealMoveSpeed()</c>, the move speed divided by 7
    /// (docs/packet-specs/socle-vitesse-echo.md); this used to be 100, which the peers saw as a run six times
    /// too fast.
    /// </summary>
    public const byte EchoedMoveSpeed = 17;

    /// <summary>The cast between its <c>ST_Casting</c> and its fire, under <see cref="CastLock"/>.</summary>
    public Navislamia.Game.Services.Casting.PendingCast PendingCast { get; set; }

    public readonly object CastLock = new();

    /// <summary>
    /// The experience the last death took (<c>StructPlayer</c> <c>+0x1d0</c>): a resurrection gives a share of it
    /// back, a respawn in town forfeits it (docs/packet-specs/socle-mort-joueur.md).
    /// </summary>
    public long DeathExpLoss { get; set; }

    /// <summary>
    /// The speed of the character's current walk: <see cref="EchoedMoveSpeed"/> slowed by its load
    /// (<see cref="Navislamia.Game.Services.Weight.WeightRules.MoveSpeed"/>), set at each move request and used
    /// for the echo, the peers' copy and the position estimate alike.
    /// </summary>
    public byte MoveSpeed { get; set; } = EchoedMoveSpeed;

    /// <summary>
    /// What the bag weighs, worn items excluded. Kept in step by
    /// <see cref="Navislamia.Game.Services.Weight.CarriedWeightService"/>.
    /// </summary>
    public float CarriedWeight { get; set; }

    /// <summary>The weight refresh of this session: 0 idle, 1 running, 2 running and asked again.</summary>
    public int WeightRefreshState;

    /// <summary>
    /// The server tick at which the character was last known at (<see cref="X"/>, <see cref="Y"/>) while
    /// heading for its destination: a move request, a region update, a world entry or a warp.
    /// </summary>
    public uint MoveStartTick { get; set; }

    /// <summary>
    /// Where the character is at <paramref name="nowTick"/>, estimated from its last known position and its
    /// destination at <see cref="EchoedMoveSpeed"/> with the monsters' interpolation. The client does not
    /// report its position continuously; each region update corrects the estimate.
    /// </summary>
    public (float X, float Y) PositionAt(uint nowTick)
    {
        var length = MathF.Sqrt((DestinationX - X) * (DestinationX - X) + (DestinationY - Y) * (DestinationY - Y));
        var endTick = Navislamia.Game.Services.MonsterMovement.EndTick(MoveStartTick, length, MoveSpeed);
        return Navislamia.Game.Services.MonsterMovement.PositionAt(X, Y, DestinationX, DestinationY, MoveStartTick,
            endTick, nowTick);
    }

    /// <summary>
    /// The PK mode, loaded from <c>Characters.PkMode</c> on world entry and persisted again by the
    /// session save. It reaches the client only through the actor status mask
    /// (<see cref="Navislamia.Game.Network.Packets.Game.ActorStatus.ForPlayer"/>): the protocol has
    /// no PK packet of its own.
    /// </summary>
    public bool PkMode { get; set; }
    public decimal ImmoralPoint { get; set; }
    public int PkCount { get; set; }
    public int DkCount { get; set; }
    public Navislamia.Game.Services.PvpProgress GetPvpProgress()
    {
        lock (ProgressLock) return new(ImmoralPoint, PkCount, DkCount);
    }

    /// <summary>
    /// <c>Characters.Permission</c>, read on world entry. A value of
    /// <see cref="Navislamia.Game.Services.GmCommands.GmCommandRules.GmPermission"/> or more unlocks the
    /// privileged GM commands — NGemity's own threshold (docs/gm-commands.md).
    /// </summary>
    public int CharacterPermission { get; set; }
    public bool AutoUsed { get; set; }

    /// <summary>
    /// The three actor states the GM commands toggle. They reach the client only through the status
    /// mask (<see cref="Navislamia.Game.Network.Packets.Game.ActorStatus.ForPlayer"/>), which is a
    /// snapshot: every send passes all of them, together with <see cref="PkMode"/>.
    /// </summary>
    public bool IsSitting { get; set; }
    public bool IsBattleMode { get; set; }
    public bool IsWalking { get; set; }

    /// <summary>
    /// Set by the GM command <c>/immortal</c>: monsters still swing but deal no damage
    /// (<see cref="Navislamia.Game.Services.ICombatService.RollMonsterHit"/>). Session only.
    /// </summary>
    public bool IsImmortal { get; set; }

    /// <summary>
    /// 1 while an item resurrection is between its check and its effect. That path waits on the database
    /// to consume the item, and the character is still at 0 HP meanwhile: without this, two requests sent
    /// together would both pass the dead check and consume two items for one resurrection.
    /// </summary>
    public int ResurrectionInProgress;

    /// <summary>
    /// 1 while a commercial storage takeout (10005) is between resolving its row and consuming it. The goods
    /// are delivered in between, and the requests are fired without awaiting each other: without this, two
    /// takeouts sent together would both resolve the full row and both deliver it.
    /// </summary>
    public int CommercialTakeoutInProgress;

    /// <summary>
    /// 1 while a job change is being committed (validation, database, packets): a second confirmation sent in the
    /// meantime would otherwise change the job twice. Taken with <c>Interlocked.CompareExchange</c>.
    /// </summary>
    public int JobChangeInProgress;

    public uint ClientClockOffset { get; set; }
    public List<int> TimeSyncGaps { get; } = new();
    public DateTime NextInventoryArrangeAt { get; set; }
    public string CharacterName { get; set; }
    public byte Layer { get; set; }

    /// <summary>
    /// The event area this session is currently inside, or 0 for none. Written only by
    /// <c>EventAreaService</c>, from a claim the server verified against its own position, or from
    /// its own position detection. There is no server answer for either packet.
    /// </summary>
    public int CurrentEventAreaId { get; set; }

    /// <summary>
    /// The <c>WorldLocation.id</c> the character currently stands in, shared by the whole 902/903 family
    /// and, later, by the 901. It stays 0 until the position → location mapping exists: neither rzu
    /// (which always sends 0) nor Navislamia can resolve a position to a location id today.
    /// </summary>
    public int CurrentLocationId { get; set; }
    public readonly object NpcVisibilityLock = new();
    public readonly object MonsterVisibilityLock = new();
    public readonly object PropVisibilityLock = new();
    public Dictionary<long, uint> SpawnedNpcs { get; } = new();
    public Dictionary<uint, long> SpawnedNpcIdsByHandle { get; } = new();
    public Dictionary<long, uint> SpawnedMonsters { get; } = new();

    /// <summary>
    /// Visible field props, both ways: a prop is activated by casting at its handle, so resolution
    /// must be O(1) and must never reach a prop outside this client's visible set.
    /// </summary>
    public Dictionary<long, uint> SpawnedProps { get; } = new();

    public Dictionary<uint, long> SpawnedPropInstancesByHandle { get; } = new();

    /// <summary>
    /// The players this client was told about: the object id of the pair to the handle it is presented
    /// with. For a player the id <b>is</b> the handle — <c>character.Id</c> — and unlike NPCs,
    /// monsters and props it is the same for every observer, because <c>TS_SC_MOVE</c> and
    /// <c>TS_SC_LEAVE</c> carry a single handle (docs/packet-specs/socle-visibilite-joueurs.md §9.2).
    /// Guarded by <see cref="PlayerVisibilityLock"/>; the visibility socle keeps both sides of a pair
    /// in step.
    /// </summary>
    public Dictionary<long, uint> SpawnedPlayers { get; } = new();

    public readonly object GroundItemVisibilityLock = new();
    public HashSet<uint> SpawnedGroundItems { get; } = new();

    public readonly object PlayerVisibilityLock = new();

    /// <summary>
    /// The fixed physical traits of this character, captured at world entry from the entity the local
    /// <c>TS_SC_ENTER</c> is built from: the session has no <c>CharacterEntity</c>, and a peer cannot be
    /// presented without them. Null until the character enters the world.
    /// </summary>
    public PlayerAppearance Appearance { get; set; }

    /// <summary>
    /// Resolves a client-visible monster handle back to its instance id, so nothing can act on an object
    /// the client cannot see. The scan is over this client's visible set only — a handful of monsters —
    /// and only ever runs on a player action, which is why monsters keep no reverse dictionary the way
    /// NPCs do.
    /// </summary>
    public bool TryResolveMonster(uint handle, out long instanceId)
    {
        instanceId = -1;
        if (handle == 0)
        {
            return false;
        }

        lock (MonsterVisibilityLock)
        {
            foreach (var (id, spawnedHandle) in SpawnedMonsters)
            {
                if (spawnedHandle != handle)
                {
                    continue;
                }

                instanceId = id;
                return true;
            }
        }

        return false;
    }

    /// <summary>The handle this client knows a monster by, or 0 if it cannot see it.</summary>
    public uint GetMonsterHandle(long instanceId)
    {
        lock (MonsterVisibilityLock)
        {
            return SpawnedMonsters.TryGetValue(instanceId, out var handle) ? handle : 0;
        }
    }
    public uint NpcDialogHandle { get; set; }
    public long NpcDialogRevision { get; set; }
    public int NpcQuestCode { get; set; }

    /// <summary>
    /// The market whose window <c>MarketService.Open</c> just opened for <see cref="NpcDialogHandle"/>, or
    /// an empty string. <c>TM_CS_BUY_ITEM</c> (251) carries no market name, and the reference resolves a
    /// purchase through the market its last contact named (<c>GetLastContactStr("market")</c>,
    /// <c>WorldSession.cpp:733</c>): this is that memory, dropped with the dialog it belongs to
    /// (<see cref="ClearNpcDialog"/>), which is also what a purchase reads to know a counter is open.
    /// See docs/packet-specs/251-buy-item.md §5.3.
    /// </summary>
    public string OpenMarketName { get; set; } = string.Empty;

    public HashSet<string> NpcDialogTriggers { get; } = new();
    public Dictionary<int, byte> LearnedSkills { get; } = new();
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>
    /// The position the character reappears at after death: the position persisted with the character
    /// at world entry, captured by <c>GameActions.OnLogin</c>. This is option (a) of the resurrection
    /// specification's §16.1 — no new column, no migration.
    /// </summary>
    public float RespawnX { get; set; }
    public float RespawnY { get; set; }
    public byte RespawnLayer { get; set; }
    public int AccountId { get; set; }
    public int Version { get; set; }
    public float LastReadTime { get; set; }
    public bool AuthVerified { get; set; }
    public byte PcBangMode { get; set; }
    public int EventCode { get; set; }
    public int Age { get; set; }
    public int AgeLimitFlags { get; set; }
    public float ContinuousPlayTime { get; set; }
    public float ContinuousLogoutTime { get; set; }
    public float LastContinuousPlayTimeProcTime;
    public string NameToDelete { get; set; }
    public bool StorageSecurityCheck { get; set; } = false;

    /// <summary>Guards <see cref="Booth"/>: the receiving thread writes it, readers may not race it.</summary>
    public readonly object BoothLock = new();

    private StartBoothRequest _booth;

    /// <summary>
    /// The booth this character declared with <c>TM_CS_START_BOOTH</c> (700), or null when none is open.
    /// Nothing is persisted and nothing is broadcast: a booth does not survive a disconnection and is
    /// visible to no client (docs/packet-specs/socle-booths.md §5.3 point 8 and §7.7).
    /// </summary>
    public StartBoothRequest Booth
    {
        get { lock (BoothLock) { return _booth; } }
    }

    /// <summary>Whether a booth is open, i.e. whether the action lock of <c>BoothRules</c> applies.</summary>
    public bool IsBoothOpen
    {
        get { lock (BoothLock) { return _booth != null; } }
    }

    /// <summary>
    /// Opens the booth, replacing a declaration already held: the client's creation window cannot send
    /// a second <c>700</c> without closing the exchange window first, and no source describes a
    /// cumulative form.
    /// </summary>
    public void OpenBooth(StartBoothRequest booth)
    {
        lock (BoothLock)
        {
            _booth = booth;
        }
    }

    /// <summary>
    /// The booth type the status mask publishes: 1 (sell) or 2 (buy) while a booth is open, 0 otherwise.
    /// </summary>
    public byte BoothType
    {
        get { lock (BoothLock) { return _booth?.Type ?? 0; } }
    }

    /// <summary>
    /// Reads and replaces the booth in one step under <see cref="BoothLock"/>: a trade reserves the
    /// quantities it takes, so two customers can never buy the same unit. <paramref name="update"/> is
    /// pure and receives null when no booth is open; returning null closes it.
    /// </summary>
    public T UpdateBooth<T>(Func<StartBoothRequest, (StartBoothRequest Next, T Result)> update)
    {
        lock (BoothLock)
        {
            var (next, result) = update(_booth);
            _booth = next;
            return result;
        }
    }

    /// <summary>Closes the booth and forgets its declared items. Returns whether one was open.</summary>
    public bool CloseBooth()
    {
        lock (BoothLock)
        {
            var wasOpen = _booth != null;
            _booth = null;
            return wasOpen;
        }
    }

    /// <summary>
    /// Guards <see cref="WatchedBoothHandle"/>: the receiving thread writes it on a 702 or a 704, and
    /// the same regime as <see cref="BoothLock"/> applies — no reader may race a writer
    /// (docs/packet-specs/socle-booths-visibilite.md §5.2 point 6).
    /// </summary>
    public readonly object BoothWatchLock = new();

    private uint? _watchedBoothHandle;

    /// <summary>
    /// The booth handle this session last asked to watch with <c>TM_CS_WATCH_BOOTH</c> (702), or null
    /// when it watches nothing. It is what <c>TM_CS_STOP_WATCH_BOOTH</c> (704) closes, and it is
    /// forgotten with the character session. Nothing else in the repository reads it: the booth is not
    /// broadcast and a 703 is built on demand only
    /// (docs/packet-specs/socle-booths-visibilite.md §5.2 points 6 and 8).
    /// </summary>
    public uint? WatchedBoothHandle
    {
        get { lock (BoothWatchLock) { return _watchedBoothHandle; } }
    }

    /// <summary>
    /// Records the observed booth, replacing a handle already held: the client sends one 702 per
    /// observation and the latest one is the truth it expects to be looking at.
    /// </summary>
    public void BeginWatchingBooth(uint boothHandle)
    {
        lock (BoothWatchLock)
        {
            _watchedBoothHandle = boothHandle;
        }
    }

    /// <summary>Forgets the observed booth. Returns whether one was being watched.</summary>
    public bool StopWatchingBooth()
    {
        lock (BoothWatchLock)
        {
            var wasWatching = _watchedBoothHandle != null;
            _watchedBoothHandle = null;
            return wasWatching;
        }
    }

    public void ClearVisibleObjects()
    {
        lock (NpcVisibilityLock)
        {
            SpawnedNpcs.Clear();
            SpawnedNpcIdsByHandle.Clear();
            ClearNpcDialog();
        }

        lock (MonsterVisibilityLock)
        {
            SpawnedMonsters.Clear();
        }

        lock (PropVisibilityLock)
        {
            SpawnedProps.Clear();
            SpawnedPropInstancesByHandle.Clear();
        }

        lock (PlayerVisibilityLock)
        {
            SpawnedPlayers.Clear();
        }

        lock (GroundItemVisibilityLock)
        {
            SpawnedGroundItems.Clear();
        }
    }

    /// <summary>
    /// Resolves a client-visible prop handle back to its instance id.
    /// </summary>
    public bool TryResolveProp(uint handle, out long instanceId)
    {
        lock (PropVisibilityLock)
        {
            return SpawnedPropInstancesByHandle.TryGetValue(handle, out instanceId);
        }
    }

    public void ClearCharacterSession()
    {
        CharacterHandle = 0;
        TargetHandle = 0;
        CharacterHp = 0;
        CarriedWeight = 0;
        MoveSpeed = EchoedMoveSpeed;
        WearFrame = null;
        PartyId = null;
        GuildId = null;
        CharacterStamina = 0;
        ItemCooldowns.Clear();
        CharacterMaxHp = 0;
        CharacterMp = 0;
        CharacterLevel = 0;
        CharacterRace = 0;
        CharacterJob = 0;
        CharacterJobLevel = 0;
        CharacterExp = 0;
        CharacterJp = 0;
        CharacterGold = 0;
        CharacterChaos = 0;
        SummonSlots = Array.Empty<long>();
        lock (SummonLock)
        {
            CreatureCards.Clear();
        }

        MainSummonCardId = 0;
        TamingCardItemId = 0;
        TamingTargetInstanceId = -1;
        ActivePet = null;
        Summons = Array.Empty<Navislamia.Game.Services.SummonPresence>();
        PetPickupFilter = 0;
        DestinationX = 0;
        DestinationY = 0;
        MoveStartTick = 0;
        PkMode = false;
        ImmoralPoint = 0m;
        PkCount = 0;
        DkCount = 0;
        CharacterPermission = 0;
        AutoUsed = false;
        IsSitting = false;
        IsBattleMode = false;
        IsWalking = false;
        IsImmortal = false;
        lock (CastLock)
        {
            PendingCast = null;
        }

        DeathExpLoss = 0;

        CharacterName = string.Empty;
        TimeSyncGaps.Clear();
        NextInventoryArrangeAt = default;
        StorageSecurityCheck = false;
        Layer = 0;
        CurrentEventAreaId = 0;
        CurrentLocationId = 0;
        X = 0;
        Y = 0;
        Z = 0;
        RespawnX = 0;
        RespawnY = 0;
        RespawnLayer = 0;
        NameToDelete = string.Empty;
        LearnedSkills.Clear();
        PreviousJobs.Clear();
        ItemEffects = Array.Empty<StatEffect>();
        PassiveEffects = Array.Empty<StatEffect>();
        BuffEffects = Array.Empty<StatEffect>();
        MainTitleId = 0;
        TitleEffects = Array.Empty<StatEffect>();
        EquippedWeapon = null;
        LeftHand = null;
        RightWeaponEffects = Array.Empty<Navislamia.Game.Services.Stats.StatEffect>();
        lock (BuffLock)
        {
            ActiveBuffs.Clear();
            StoredSummonBuffs.Clear();
            ActiveAuras.Clear();
        }

        SkillCooldowns.Clear();
        NextStateHandle = 0;
        // A booth belongs to the character session: the next character never inherits it, and neither
        // does the booth it was watching.
        CloseBooth();
        StopWatchingBooth();
        ClearVisibleObjects();
    }

    public void ClearNpcDialog()
    {
        NpcDialogRevision++;
        NpcQuestCode = 0;
        NpcDialogHandle = 0;
        NpcDialogTriggers.Clear();

        // The trade window is bound to the dialog that opened it: a dialog that ended (or moved to
        // another NPC) no longer sells. TM_CS_BUY_ITEM (251) reads this to refuse with 7.
        OpenMarketName = string.Empty;
    }
}
