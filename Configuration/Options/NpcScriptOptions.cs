namespace Navislamia.Configuration.Options;

public sealed class NpcScriptOptions
{
    /// <summary>Official get_local_info() country code, independent of the resource local_flag mask.</summary>
    public int LocalInfo { get; set; } = 1;
}
