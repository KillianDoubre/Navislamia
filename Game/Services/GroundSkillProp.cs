using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>The static world actor of a persistent skill, including late observers and LEAVE.</summary>
public sealed class GroundSkillProp
{
    private readonly object _gate = new();
    private readonly Dictionary<GameClient, uint> _seen = new();
    private readonly Func<IEnumerable<GameClient>> _clients;
    private readonly Func<GameClient, uint> _caster;
    private readonly Func<bool> _alive;
    private readonly float _x, _y, _z;
    private readonly byte _layer;
    private readonly uint _start, _duration;
    private readonly int _skillId;
    private readonly uint _handle = WorldObjectHandle.Next();
    public GroundSkillProp(float x, float y, float z, byte layer, uint start, uint duration, int skillId,
        Func<IEnumerable<GameClient>> clients, Func<GameClient, uint> caster, Func<bool> alive)
    {
        _x = x; _y = y; _z = z; _layer = layer; _start = start; _duration = duration;
        _skillId = skillId; _clients = clients; _caster = caster; _alive = alive;
    }
    public bool Sync(uint now)
    {
        lock (_gate)
        {
            var alive = _alive() && unchecked((int)(now - _start)) <= _duration;
            var viewers = alive ? _clients().Where(c => c.ConnectionInfo.CharacterHandle != 0
                && c.ConnectionInfo.Layer == _layer && CombatRange.Distance(_x, _y, c.ConnectionInfo.X, c.ConnectionInfo.Y)
                    <= WorldVisibility.ViewRange).ToHashSet() : new HashSet<GameClient>();
            foreach (var (client, characterHandle) in _seen.ToArray())
                if (!viewers.Contains(client) || client.ConnectionInfo.CharacterHandle != characterHandle)
                { Send(client, GameSpawnPackets.BuildLeave(_handle)); _seen.Remove(client); }
            foreach (var client in viewers)
            {
                if (_seen.ContainsKey(client)) continue;
                Send(client, GameSpawnPackets.BuildEnterSkillProp(_handle, _x, _y, _z, _layer,
                    _caster(client), unchecked(_start + client.ConnectionInfo.ClientClockOffset), _skillId));
                _seen[client] = client.ConnectionInfo.CharacterHandle;
            }
            return alive;
        }
    }
    private static void Send(GameClient client, byte[] frame)
    {
        try { client.Connection.Send(frame); }
        catch (Exception ex) { Log.Debug(ex, "Ground skill actor could not be sent to {ClientTag}", client.ClientTag); }
    }
}
