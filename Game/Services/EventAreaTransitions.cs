using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>Server position changes notify the area service without a warp/interpreter dependency cycle.</summary>
public sealed class EventAreaTransitions
{
    public Action<GameClient> Changed { get; set; }
}
