using System;

namespace Navislamia.Game.DataAccess.Entities.Arcadia;

public sealed class EventAreaResourceEntity : Entity
{
    public int BeginTime { get; set; }
    public int EndTime { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public long RaceJobLimit { get; set; }
    public int[] Conditions { get; set; } = Array.Empty<int>();
    public int[] Values { get; set; } = Array.Empty<int>();
    public int CountLimit { get; set; }
    public string EnterHandler { get; set; } = "";
    public string LeaveHandler { get; set; } = "";
}
