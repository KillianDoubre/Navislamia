using System.Collections.Generic;

namespace Navislamia.Game.Network.Clients;

public class ConnectionInfo
{
    public string AccountName { get; set; }
    public List<string> CharacterList { get; set; } = new();
    public uint CharacterHandle { get; set; }
    public uint TargetHandle { get; set; }
    public int CharacterHp { get; set; }
    public int CharacterLevel { get; set; }
    public long CharacterExp { get; set; }
    public long CharacterJp { get; set; }
    public long CharacterGold { get; set; }
    public int CharacterChaos { get; set; }
    public uint ClientClockOffset { get; set; }
    public string CharacterName { get; set; }
    public byte Layer { get; set; }
    public readonly object NpcVisibilityLock = new();
    public readonly object MonsterVisibilityLock = new();
    public Dictionary<long, uint> SpawnedNpcs { get; } = new();
    public Dictionary<uint, long> SpawnedNpcIdsByHandle { get; } = new();
    public Dictionary<long, uint> SpawnedMonsters { get; } = new();
    public uint NpcDialogHandle { get; set; }
    public HashSet<string> NpcDialogTriggers { get; } = new();
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
        SpawnedNpcs.Clear();
        SpawnedNpcIdsByHandle.Clear();
        SpawnedMonsters.Clear();
        ClearNpcDialog();
    }

    public void ClearNpcDialog()
    {
        NpcDialogHandle = 0;
        NpcDialogTriggers.Clear();
    }
}
