using System;

namespace Navislamia.Game.Services.Guilds;

/// <summary>Attaches the existing authenticated upload connection without a guild/network dependency cycle.</summary>
public sealed class GuildUploadRelay
{
    public Func<bool> IsReady { get; set; } = () => false;
    public Action<byte[]> Send { get; set; }
}
