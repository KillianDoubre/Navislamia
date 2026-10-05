using System;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services.Movement;

/// <summary>
/// The moves the server imposes on a player, sent to the player and to every player who sees it, each on its own
/// clock — what the official <c>ArcadiaServer::SetMove</c> broadcasts to the region.
/// </summary>
public static class PlayerMoves
{
    /// <summary>
    /// <c>StructCreature::onDead</c> (<c>StructCreature.cpp:5728-5737</c>): a creature that dies walking stops where it
    /// stands. Without it the client, and its observers, kept walking the dead character to its destination.
    /// </summary>
    public static void Stop(GameClient client, IPlayerVisibilityService players)
    {
        var info = client.ConnectionInfo;
        var now = ServerClock.Now;
        var (x, y) = Buffs.SkillCastRangeRules.PlayerPosition(info, now);
        info.BeginWalk(x, y, Array.Empty<(float X, float Y)>(), now);
        Send(client, players, recipient => GameMovePackets.BuildStopMove(info.CharacterHandle,
            unchecked(now + recipient.ClientClockOffset), info.Layer));
    }

    /// <summary>
    /// <c>GetValidWayPoint</c>'s correction: an obstacle on the claimed way, so the walk becomes one step back to
    /// where the server has the player (<c>vMoveInfo = { curPosFromServer }</c>).
    /// </summary>
    public static void WalkBackTo(GameClient client, IPlayerVisibilityService players, float x, float y)
    {
        var info = client.ConnectionInfo;
        var now = ServerClock.Now;
        info.BeginWalk(x, y, Array.Empty<(float X, float Y)>(), now);
        Send(client, players, recipient => GameMovePackets.BuildMove(info.CharacterHandle,
            unchecked(now + recipient.ClientClockOffset), info.Layer, info.MoveSpeed, x, y));
    }

    private static void Send(GameClient client, IPlayerVisibilityService players, Func<ConnectionInfo, byte[]> frame)
    {
        client.Connection.Send(frame(client.ConnectionInfo));
        if (players is null) return;
        foreach (var observer in players.Observers(client))
        {
            observer.Connection.Send(frame(observer.ConnectionInfo));
        }
    }
}
