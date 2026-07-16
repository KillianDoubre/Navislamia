using System;
using System.Collections.Generic;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Network.Clients;

public class ConnectionInfo
{
    public List<(int Job, int JobLevel)> PreviousJobs { get; } = new();
    public IReadOnlyList<ItemStatEffect> ItemEffects { get; set; } = Array.Empty<ItemStatEffect>();
    public string AccountName { get; set; }
    public List<string> CharacterList { get; set; } = new();
    public uint CharacterHandle { get; set; }
    public uint TargetHandle { get; set; }
    public int CharacterHp { get; set; }
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
        ItemEffects = Array.Empty<ItemStatEffect>();
        ClearVisibleObjects();
    }

    public void ClearNpcDialog()
    {
        NpcDialogHandle = 0;
        NpcDialogTriggers.Clear();
    }
}
