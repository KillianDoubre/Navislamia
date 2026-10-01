using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>A durable completion mark, separate from the active quest list sent in packet 600.</summary>
public class CharacterQuestCompletionEntity : Entity
{
    public long CharacterId { get; set; }
    public int Code { get; set; }
    public DateTime CompletedAt { get; set; }
}
