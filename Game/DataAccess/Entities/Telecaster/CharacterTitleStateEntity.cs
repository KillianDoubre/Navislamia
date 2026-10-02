using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

public sealed class CharacterTitleStateEntity
{
    public long CharacterId { get; set; }
    public int[] OpenedTitleIds { get; set; } = Array.Empty<int>();
    public int[] OwnedTitleIds { get; set; } = Array.Empty<int>();
    public int[] ConditionIds { get; set; } = Array.Empty<int>();
    public long[] ConditionCounts { get; set; } = Array.Empty<long>();
}
