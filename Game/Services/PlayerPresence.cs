using System;

namespace Navislamia.Game.Services;

/// <summary>
/// The fixed physical traits of a character, the ones a session does not otherwise carry. The
/// <c>PLAYER_INFO</c> block of <c>TS_SC_ENTER</c> is a snapshot of them
/// (docs/packet-specs/socle-visibilite-joueurs.md §3.3), and it is the only place an observer can
/// learn them: the repository keeps no <c>CharacterEntity</c> in the session.
/// </summary>
/// <remarks>
/// Captured once, at world entry, from the same entity the local <c>TS_SC_ENTER</c> is built from, so
/// a peer reads byte for byte what the character sees of itself. Everything that changes during a
/// session (level, job, health, position, states) stays out of here and is read live from
/// <c>ConnectionInfo</c>.
/// </remarks>
public sealed class PlayerAppearance
{
    public byte Race { get; init; }
    public byte Sex { get; init; }
    public uint SkinColor { get; init; }
    public uint FaceId { get; init; }
    public uint FaceTextureId { get; init; }
    public uint HairId { get; init; }
    public uint HairColorIndex { get; init; }
    public uint HairColorRgb { get; init; }
    public uint HideEquipFlag { get; init; }
}

/// <summary>
/// One character present in the world, as the visibility index and the streaming service need it: a
/// shared handle and a mutable place.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is <c>character.Id</c> — the handle <c>GameActions.OnLogin</c> already puts in
/// <c>TS_SC_ENTER</c> (<c>Actions/GameActions.cs:181</c>). It is deliberately <b>not</b> a per-client
/// handle like the ones <c>WorldObjectStreamer</c> allocates for NPCs, monsters and props: a player
/// has one handle for every observer, because <c>TS_SC_MOVE</c> and <c>TS_SC_LEAVE</c> carry a single
/// handle and a walk is diffused to several clients at once
/// (docs/packet-specs/socle-visibilite-joueurs.md §9.2).
/// </remarks>
public sealed class PlayerPresence
{
    public PlayerPresence(uint handle, PlayerAppearance appearance, byte layer, float x, float y, float z)
    {
        if (appearance is null)
        {
            throw new ArgumentNullException(nameof(appearance));
        }

        Handle = handle;
        Appearance = appearance;
        Layer = layer;
        X = x;
        Y = y;
        Z = z;
    }

    public uint Handle { get; }

    public PlayerAppearance Appearance { get; }

    public byte Layer { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}
