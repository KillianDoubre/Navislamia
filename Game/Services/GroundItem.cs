using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public class GroundItem
{
    public int TakenBy;

    public uint Handle { get; init; }
    public int ItemCode { get; init; }
    public long Count { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public byte Layer { get; init; }
    public GameClient Owner { get; init; }
    public uint OwnerHandle { get; init; }
    public DateTime ExpiresAt { get; init; }
}
