using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>A weekly registration and its best completed raid, in 10 ms server ticks.</summary>
public sealed class GuildRaidEntity : Entity
{
    public long GuildId { get; set; }
    public long DungeonId { get; set; }
    public DateTime Week { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? LastCompletedAt { get; set; }
    public int BestTime { get; set; }
    public bool Boss1Dead { get; set; }
    public bool Boss2Dead { get; set; }
    public bool WrappedUp { get; set; }
}

/// <summary>Original sides survive captures and server restarts; ownership remains in Dungeons.</summary>
public sealed class GuildSiegeEntity : Entity
{
    public long DungeonId { get; set; }
    public DateTime Week { get; set; }
    public long? DefenderId { get; set; }
    public long AttackerId { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long? WinnerId { get; set; }
    public bool CoreDestroyed { get; set; }
}

/// <summary>One participation and one title result per character and siege, including offline players.</summary>
public sealed class GuildSiegeParticipantEntity : Entity
{
    public long SiegeId { get; set; }
    public long CharacterId { get; set; }
    public bool Attacker { get; set; }
    public bool StartCredited { get; set; }
    public bool EndCredited { get; set; }
}
