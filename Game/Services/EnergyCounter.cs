using System;
using System.Collections.Generic;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>StructCreature::AddEnergy/RemoveEnergy/OnUpdate. Session data; neither warp nor death clears it.</summary>
public sealed class EnergyCounter
{
    public const uint UpkeepTicks = 180000;
    public const int Maximum = 10;
    private readonly Queue<uint> _expires = new();
    public object Gate { get; } = new();
    public int Count { get { lock (Gate) return _expires.Count; } }
    public int Add(int amount, int capacity, uint now)
    {
        lock (Gate)
        {
            var added = Math.Clamp(amount, 0, Math.Max(0, Math.Min(Maximum, capacity) - _expires.Count));
            for (var i = 0; i < added; i++) _expires.Enqueue(unchecked(now + UpkeepTicks));
            return added;
        }
    }
    public bool Consume(int amount, bool preserve = false)
    {
        lock (Gate)
        {
            if (amount < 0 || amount > _expires.Count) return false;
            if (preserve) return true;
            for (var i = 0; i < amount; i++) _expires.Dequeue();
            return true;
        }
    }
    public bool Expire(uint now, Func<bool> preserve = null)
    {
        lock (Gate)
        {
            if (!_expires.TryPeek(out var first) || unchecked((int)(now - first)) <= 0) return false;
            if (preserve?.Invoke() == true) return false;
            var before = _expires.Count;
            while (_expires.TryPeek(out var due) && unchecked((int)(now - due)) > 0) _expires.Dequeue();
            return before != _expires.Count;
        }
    }
    public void Clear() { lock (Gate) _expires.Clear(); }
    public static int Capacity(ConnectionInfo info) => Math.Min(Maximum, (int)info.LearnedSkills.GetValueOrDefault(1082));
    public static void Publish(GameClient client) => client.SendToSelfAndObservers(
        GameStateResultPackets.Energy(client.ConnectionInfo.CharacterHandle, client.ConnectionInfo.Energy.Count));
}
