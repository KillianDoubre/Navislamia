using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
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
    public int CharacterMp { get; set; }
    public int CharacterLevel { get; set; }
    public int CharacterRace { get; set; }
    public int CharacterJob { get; set; }
    public int CharacterJobLevel { get; set; }
    public long CharacterExp { get; set; }
    public long CharacterJp { get; set; }
    public long CharacterGold { get; set; }
    public int CharacterChaos { get; set; }
    public uint ClientClockOffset { get; set; }
    public List<int> TimeSyncGaps { get; } = new();
    public DateTime NextInventoryArrangeAt { get; set; }
    public string CharacterName { get; set; }
    public byte Layer { get; set; }
    public readonly object NpcVisibilityLock = new();
    public readonly object MonsterVisibilityLock = new();
    public Dictionary<long, uint> SpawnedNpcs { get; } = new();
    public Dictionary<uint, long> SpawnedNpcIdsByHandle { get; } = new();
    public Dictionary<long, uint> SpawnedMonsters { get; } = new();

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
    }

    public void ClearCharacterSession()
    {
        CharacterHandle = 0;
        TargetHandle = 0;
        CharacterHp = 0;
        CharacterMp = 0;
        CharacterLevel = 0;
        CharacterRace = 0;
        CharacterJob = 0;
        CharacterJobLevel = 0;
        CharacterExp = 0;
        CharacterJp = 0;
        CharacterGold = 0;
        CharacterChaos = 0;
        CharacterName = string.Empty;
        TimeSyncGaps.Clear();
        NextInventoryArrangeAt = default;
        Layer = 0;
        X = 0;
        Y = 0;
        Z = 0;
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
        ClearVisibleObjects();
    }

    public void ClearNpcDialog()
    {
        NpcDialogHandle = 0;
        NpcDialogTriggers.Clear();
    }
}
