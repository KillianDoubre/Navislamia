using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>A replaceable state snapshot; card 0 is the player, other cards identify their summons.</summary>
public class CharacterStateEntity
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public long SummonCardId { get; set; }
    public int StateId { get; set; }
    public int SkillId { get; set; }
    public int StateLevel { get; set; }
    public long RemainingTicks { get; set; }
    public bool Infinite { get; set; }
    public int? PeriodicBaseDamage { get; set; }
    public int? RemainingFireTicks { get; set; }
    public DateTime SavedAtUtc { get; set; }
}
