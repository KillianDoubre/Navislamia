using System;
using System.Collections.Generic;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The one visibility loop, shared by every kind of world object: stream what entered the client's
/// view, leave what left it, and keep the per-client handle maps in step.
/// </summary>
/// <remarks>
/// NPCs, monsters and field props had three copies of this, which is how they drifted apart — only two
/// of them maintained a handle-to-id map, and only one honoured objects that must stay visible without
/// being re-streamed. Anything the client must see goes through here.
/// </remarks>
public static class WorldObjectStreamer
{
    /// <param name="visibilityLock">Guards the client's maps; the world ticks and the client thread
    /// both reach them.</param>
    /// <param name="inRange">What the spatial index says is inside the view.</param>
    /// <param name="getId">The object's stable instance id, the key of the visible set.</param>
    /// <param name="buildEnter">Builds the object's enter packet for the handle it is given.</param>
    /// <param name="handlesById">The client's visible set.</param>
    /// <param name="idsByHandle">Optional reverse map, for kinds the client can act on by handle.</param>
    /// <param name="canEnter">Whether the object may be streamed. One that may not but is already
    /// visible stays visible rather than leaving: a monster's corpse outlives its death, and its
    /// <c>TS_SC_LEAVE</c> is deferred by the combat tick so the client can play the animation.</param>
    public static void Stream<T>(
        GameClient client,
        object visibilityLock,
        IReadOnlyList<T> inRange,
        Func<T, long> getId,
        Func<T, uint, byte[]> buildEnter,
        Dictionary<long, uint> handlesById,
        Dictionary<uint, long> idsByHandle = null,
        Func<T, bool> canEnter = null)
    {
        var visible = new HashSet<long>(inRange.Count);

        lock (visibilityLock)
        {
            foreach (var item in inRange)
            {
                var id = getId(item);
                var alreadyVisible = handlesById.ContainsKey(id);

                if (canEnter is not null && !canEnter(item))
                {
                    if (alreadyVisible)
                    {
                        visible.Add(id);
                    }

                    continue;
                }

                visible.Add(id);

                if (alreadyVisible)
                {
                    continue;
                }

                var handle = WorldObjectHandle.Next();
                client.Connection.Send(buildEnter(item, handle));

                handlesById[id] = handle;

                if (idsByHandle is not null)
                {
                    idsByHandle[handle] = id;
                }
            }

            DespawnMissing(client.Connection, handlesById, visible, idsByHandle);
        }
    }

    private static void DespawnMissing(Connection connection, Dictionary<long, uint> handlesById,
        HashSet<long> visible, Dictionary<uint, long> idsByHandle)
    {
        List<KeyValuePair<long, uint>> leaving = null;

        foreach (var pair in handlesById)
        {
            if (visible.Contains(pair.Key))
            {
                continue;
            }

            connection.Send(GameSpawnPackets.BuildLeave(pair.Value));
            leaving ??= new List<KeyValuePair<long, uint>>();
            leaving.Add(pair);
        }

        if (leaving is null)
        {
            return;
        }

        foreach (var pair in leaving)
        {
            handlesById.Remove(pair.Key);
            idsByHandle?.Remove(pair.Value);
        }
    }
}
