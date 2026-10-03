using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public sealed class DungeonOptions
{
    /// <summary>Bit identifying the regional DungeonResource rows. The default is the first exported region.</summary>
    public int LocalFlag { get; set; } = 1;
    public string TimeZone { get; set; } = "Europe/Paris";
    public List<int> ClosedDungeons { get; set; } = new();
}
