using System.Collections.Generic;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>A summon in the world: its handle, what it was entered with and where.</summary>
public sealed record SummonPresence(uint Handle, SummonWorldEntry Entry, float X, float Y, byte Layer);

/// <summary>
/// The frames that show a player's companions — the pet and the summons — to another player
/// (docs/packet-specs/socle-diffusion-compagnons.md). A companion's handle comes from
/// <see cref="WorldObjectHandle"/> and is the same for every client, unlike a monster's, so one frame serves
/// every observer. They follow the master's own <c>TS_SC_ENTER</c> and precede its <c>TS_SC_LEAVE</c>.
/// </summary>
/// <remarks>
/// Lock-free on purpose: the player visibility calls these under an observer's visibility lock, and the pet
/// and summon paths take their own lock first and the visibility lock second. The pet reference and the
/// summon array are read once each, which is atomic; a pet position read mid-move is at worst a frame old.
/// Observers get the object (<c>TS_SC_ENTER</c>, <c>is_first_enter = 0</c>) and never the creature window
/// (351, 301), which belongs to the master.
/// </remarks>
public static class CompanionFrames
{
    public static List<byte[]> Enter(ConnectionInfo master, uint now)
    {
        var frames = new List<byte[]>();

        if (master.ActivePet is { } pet && pet.Entry is { } entry)
        {
            var (x, y) = pet.PositionAt(now);
            frames.Add(GameSpawnPackets.BuildEnterPet(pet.Handle, x, y, entry.Z, entry.Layer, entry.Hp, entry.MaxHp,
                entry.Mp, entry.MaxMp, entry.Level, entry.Race, entry.FaceDirection, false, master.CharacterHandle,
                entry.PetCode, entry.Name));
        }

        foreach (var summon in master.Summons)
        {
            frames.Add(SummonEnter(master.CharacterHandle, summon, false));
        }

        return frames;
    }

    public static List<byte[]> Leave(ConnectionInfo master)
    {
        var frames = new List<byte[]>();

        if (master.ActivePet is { } pet)
        {
            frames.Add(GameSpawnPackets.BuildLeave(pet.Handle));
        }

        foreach (var summon in master.Summons)
        {
            frames.Add(GameSpawnPackets.BuildLeave(summon.Handle));
        }

        return frames;
    }

    public static byte[] SummonEnter(uint masterHandle, SummonPresence summon, bool isFirstEnter)
    {
        var entry = summon.Entry;
        return GameSpawnPackets.BuildEnterSummon(summon.Handle, summon.X, summon.Y, entry.Z, summon.Layer,
            entry.Hp, entry.MaxHp, entry.Mp, entry.MaxMp, entry.Level, entry.FaceDirection, isFirstEnter,
            masterHandle, (uint)entry.Code, entry.Name, entry.Enhance);
    }
}
