using System;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The caller the summon socle was missing: it turns one <see cref="SummonWorldEntry"/> into the frames
/// that make an invocation <b>exist in the world, sit somewhere, and leave again</b> —
/// <c>TS_SC_ADD_SUMMON_INFO</c> (301) then <c>TS_SC_ENTER</c> (3) to bring it in (<c>BuildEnterSummon</c>,
/// see <c>docs/packet-specs/socle-invocation-monde.md</c> §3.1, §5.1 and §5.2), and
/// <c>TS_SC_UNSUMMON</c> (305) then <c>TS_SC_LEAVE</c> (9) to take it out (§5.3).
/// <para>
/// It decides nothing about the game: every value no reference settles — maximum health and mana, the
/// <c>z</c>, the enhancement, the first-entry flag, the jitter palier — is carried by the entry, and the
/// placement comes from the master's own session (position, layer, handle). What it does <b>not</b> do is
/// stated with its reason in the specification: it does not broadcast to the region (nothing in NavisLamia
/// knows which other players see a session, so only the master's direct copy of the 305 exists here), it
/// does not emit the 306, 307, 320 and 321 (no trigger or scale is established for them) and it does not
/// touch the summon tables (no service reads or writes them yet).
/// </para>
/// </summary>
public sealed class SummonWorldService
{
    /// <summary>
    /// Jitter applied to the master's position when a summoning skill places the summon
    /// (<c>Skill.cpp:640</c>, §5.2): ±35 game units.
    /// </summary>
    public const int SummonNoiseRange = 70;

    /// <summary>Jitter applied when a summon re-enters the world at login (<c>Player.cpp:820</c>, §5.2).</summary>
    public const int LoginNoiseRange = 50;

    /// <summary>Jitter applied when warp re-places a summon (<c>World.cpp:468</c>, §5.2).</summary>
    public const int WarpNoiseRange = 35;

    private readonly ILogger _logger = Log.ForContext<SummonWorldService>();

    /// <summary>
    /// Brings one summon into the world and returns the handle it was given, or 0 when the session or the
    /// connection is missing and nothing was sent. The frame order is the reference's: the creature window
    /// first (<c>TS_SC_ADD_SUMMON_INFO</c>, which carries the same handle), then the object
    /// (<c>TS_SC_ENTER</c>), placed at the master's position plus the caller's jitter palier.
    /// <para>
    /// <c>NON ÉTABLI</c> 6 leaves the <c>z</c> to the caller, so the summon is placed at the master's
    /// <c>x</c>/<c>y</c> and layer only.
    /// </para>
    /// </summary>
    public uint Enter(ConnectionInfo session, string clientTag, Connection connection, SummonWorldEntry entry)
    {
        if (session is null || connection is null || entry is null)
        {
            return 0;
        }

        var handle = WorldObjectHandle.Next();
        var x = session.X + Jitter(entry.NoiseRange, NextRaw());
        var y = session.Y + Jitter(entry.NoiseRange, NextRaw());

        connection.Send(GameSummonPackets.BuildAddSummonInfo(entry.CardHandle, handle, entry.Name, entry.Code,
            entry.Level, entry.Sp));

        connection.Send(GameSpawnPackets.BuildEnterSummon(handle, x, y, entry.Z, session.Layer, entry.Hp,
            entry.MaxHp, entry.Mp, entry.MaxMp, entry.Level, entry.FaceDirection, entry.IsFirstEnter,
            session.CharacterHandle, (uint)entry.Code, entry.Name, entry.Enhance));

        _logger.Debug(
            "{ClientTag} summon {Handle} (code {Code}) enters the world at {X}/{Y}/{Z} layer {Layer} of master {MasterHandle} (noise {NoiseRange})",
            clientTag, handle, entry.Code, x, y, entry.Z, session.Layer, session.CharacterHandle, entry.NoiseRange);

        return handle;
    }

    /// <summary>
    /// Takes one summon out of the world: <c>TS_SC_UNSUMMON</c> (305) then <c>TS_SC_LEAVE</c> (9), both to
    /// the master — the direct copy <c>Player.cpp:1008-1018</c> makes in addition to the regional broadcast,
    /// which has no equivalent here (§5.3 step 3). The <paramref name="session"/> only labels the log line.
    /// Returns false, and sends nothing, on a missing connection or an empty handle.
    /// </summary>
    public bool Leave(ConnectionInfo session, string clientTag, Connection connection, uint summonHandle)
    {
        if (connection is null || summonHandle == 0)
        {
            return false;
        }

        connection.Send(GameSummonPackets.BuildUnsummon(summonHandle));
        connection.Send(GameSpawnPackets.BuildLeave(summonHandle));

        _logger.Debug("{ClientTag} summon {Handle} leaves the world (master {MasterHandle})", clientTag,
            summonHandle, session?.CharacterHandle);

        return true;
    }

    /// <summary>
    /// The bounded offset <c>AddNoise</c> adds to a master's position: <c>raw % noiseRange - noiseRange / 2</c>
    /// on integers (<c>Skill.cpp:640</c>), so a summon lands inside <c>[−range/2, +range/2)</c> of its master.
    /// A <paramref name="noiseRange"/> of 0 returns 0: the reference would divide by zero there, and a caller
    /// with no palier keeps the master's exact position instead of taking the send down.
    /// </summary>
    public static float Jitter(int noiseRange, uint raw) =>
        noiseRange <= 0 ? 0f : (int)(raw % (uint)noiseRange) - noiseRange / 2;

    private static uint NextRaw() => (uint)Random.Shared.Next();
}
