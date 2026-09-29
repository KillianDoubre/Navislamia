using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Network.Clients;

public class ConnectionInfo
{
    public List<(int Job, int JobLevel)> PreviousJobs { get; } = new();
    public IReadOnlyList<StatEffect> ItemEffects { get; set; } = Array.Empty<StatEffect>();
    public IReadOnlyList<StatEffect> PassiveEffects { get; set; } = Array.Empty<StatEffect>();
    public IReadOnlyList<StatEffect> BuffEffects { get; set; } = Array.Empty<StatEffect>();
    public ItemType? EquippedWeapon { get; set; }

    /// <summary>Guards <see cref="ActiveBuffs"/>: the expiry tick and the client thread both touch it.</summary>
    public object BuffLock { get; } = new();

    public List<ActiveBuff> ActiveBuffs { get; } = new();

    /// <summary>Active auras by <c>toggle_group</c>: one aura per group at a time.</summary>
    public Dictionary<int, int> ActiveAuras { get; } = new();

    public Dictionary<int, uint> SkillCooldowns { get; } = new();
    public ushort NextStateHandle { get; set; }
    public string AccountName { get; set; }
    public List<string> CharacterList { get; set; } = new();
    public uint CharacterHandle { get; set; }
    public uint TargetHandle { get; set; }
    public int CharacterHp { get; set; }
    public int CharacterMaxHp { get; set; }
    public int CharacterMp { get; set; }
    public int CharacterLevel { get; set; }
    public int CharacterRace { get; set; }
    public int CharacterJob { get; set; }
    public int CharacterJobLevel { get; set; }
    public long CharacterExp { get; set; }
    public long CharacterJp { get; set; }
    public long CharacterGold { get; set; }
    public int CharacterChaos { get; set; }

    /// <summary>
    /// The creature formation as stored on the character (six item handles at most), read once at world
    /// entry. Both the login TM_EQUIP_SUMMON (303) and the answer to TM_CS_GET_SUMMON_SETUP_INFO (324)
    /// are built from it, so the two cannot disagree. One source only: sending the two frames from two
    /// different reads would make the window depend on which one the player opened.
    /// </summary>
    public long[] SummonSlots { get; set; } = Array.Empty<long>();

    /// <summary>
    /// The pet the character has out, called by its cage (<c>PetSummonService</c>), or null. Guarded by
    /// <see cref="PetLock"/>: an item use and a warp can both move it.
    /// </summary>
    public Navislamia.Game.Services.Pets.ActivePet ActivePet { get; set; }

    public object PetLock { get; } = new();

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
    /// The speed the server echoes a player's moves at (<c>GameClient.HandleMoveRequest</c>), which is also
    /// what it assumes to estimate where a walking character is.
    /// </summary>
    public const byte EchoedMoveSpeed = 100;

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
        var endTick = Navislamia.Game.Services.MonsterMovement.EndTick(MoveStartTick, length, EchoedMoveSpeed);
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

    /// <summary>
    /// <c>Characters.Permission</c>, read on world entry. A value of
    /// <see cref="Navislamia.Game.Services.GmCommands.GmCommandRules.GmPermission"/> or more unlocks the
    /// privileged GM commands — NGemity's own threshold (docs/gm-commands.md).
    /// </summary>
    public int CharacterPermission { get; set; }

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
    /// (<see cref="Navislamia.Game.Services.MonsterAiRules.PlayerDamage(int, bool)"/>). Session only.
    /// </summary>
    public bool IsImmortal { get; set; }

    /// <summary>
    /// 1 while an item resurrection is between its check and its effect. That path waits on the database
    /// to consume the item, and the character is still at 0 HP meanwhile: without this, two requests sent
    /// together would both pass the dead check and consume two items for one resurrection.
    /// </summary>
    public int ResurrectionInProgress;

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
        ActivePet = null;
        PetPickupFilter = 0;
        DestinationX = 0;
        DestinationY = 0;
        MoveStartTick = 0;
        PkMode = false;
        CharacterPermission = 0;
        IsSitting = false;
        IsBattleMode = false;
        IsWalking = false;
        IsImmortal = false;
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
        EquippedWeapon = null;
        lock (BuffLock)
        {
            ActiveBuffs.Clear();
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
        NpcDialogHandle = 0;
        NpcDialogTriggers.Clear();
    }
}
