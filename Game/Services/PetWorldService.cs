using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The caller the pet socle was missing: it turns one <see cref="PetWorldEntry"/> into the frames that make
/// a familier <b>exist in the world and leave it again</b> —
/// <c>TS_SC_ADD_PET_INFO</c> (351) then <c>TS_SC_ENTER</c> (3) to bring it in (the client's only entry case,
/// <c>objType = EOT_Pet</c> (7), see <c>docs/packet-specs/socle-familier-pet.md</c> §5, §5.1, §7.2), and
/// <c>TS_SC_UNSUMMON_PET</c> (350) then <c>TS_SC_LEAVE</c> (9) to take it out (§4.1, §4.3).
/// <para>
/// It decides nothing about the game: every value no reference settles — the whole placement
/// (<c>x</c>/<c>y</c>/<c>z</c>/<c>layer</c>), the statistics, the <c>race</c>, the <c>pet_code</c>, the two
/// open <c>int32</c> of the 351, the first-entry flag — is carried by the entry. Only the master handle
/// comes from the session: the pet belongs to the character (<c>CharacterEntity.PetId</c>, fiche §1, §9.2).
/// </para>
/// <para>
/// What it does <b>not</b> do is stated with its reason in the sheet: it broadcasts to nobody (nothing in
/// NavisLamia knows which other players see a session), it emits no <c>TM_CS_SET_PET_FILTER</c> answer
/// (355 has none, §7.2.4) and it touches no pet table (no service reads or writes them yet). The
/// <c>TS_SC_LEAVE</c> after the 350 is carried <b>by symmetry</b> with the summon socle, not by proof: the
/// client removes the actor on the 350 alone (fiche §11.5, <c>NON ÉTABLI</c>).
/// </para>
/// </summary>
public sealed class PetWorldService
{
    private readonly ILogger _logger = Log.ForContext<PetWorldService>();

    /// <summary>
    /// Brings one pet into the world and returns the handle it was given, or 0 when the session or the
    /// connection is missing and nothing was sent. The frame order is the reference's: the creature window
    /// first (<c>TS_SC_ADD_PET_INFO</c>, which carries the same handle as the object), then the object
    /// (<c>TS_SC_ENTER</c>) at the caller's placement. Returns 0 on a null session, connection or entry.
    /// </summary>
    public uint Enter(ConnectionInfo session, string clientTag, Connection connection, PetWorldEntry entry)
    {
        if (session is null || connection is null || entry is null)
        {
            return 0;
        }

        var handle = WorldObjectHandle.Next();

        connection.Send(GamePetPackets.BuildAddPetInfo(entry.CageHandle, handle, entry.Name, entry.Code,
            entry.Unknown));

        connection.Send(GameSpawnPackets.BuildEnterPet(handle, entry.X, entry.Y, entry.Z, entry.Layer,
            entry.Hp, entry.MaxHp, entry.Mp, entry.MaxMp, entry.Level, entry.Race, entry.FaceDirection,
            entry.IsFirstEnter, session.CharacterHandle, entry.PetCode, entry.Name));

        _logger.Debug(
            "{ClientTag} pet {Handle} (code {PetCode}) enters the world at {X}/{Y}/{Z} layer {Layer} of master {MasterHandle}",
            clientTag, handle, entry.PetCode, entry.X, entry.Y, entry.Z, entry.Layer,
            session.CharacterHandle);

        return handle;
    }

    /// <summary>
    /// Takes one pet out of the world: <c>TS_SC_UNSUMMON_PET</c> (350) then <c>TS_SC_LEAVE</c> (9), both to
    /// the master. The client needs no more than the 350 — it finds the actor by its handle, checks the
    /// <c>objType</c> (7) and removes it itself (fiche §4.6) — so the 9 is the summon socle's symmetry
    /// (<c>SummonWorldService.Leave</c>), whose real need for a pet is <c>NON ÉTABLI</c> (fiche §11.5). The
    /// <paramref name="session"/> only labels the log line. Returns false, and sends nothing, on a missing
    /// connection or an empty handle.
    /// </summary>
    public bool Leave(ConnectionInfo session, string clientTag, Connection connection, uint petHandle)
    {
        if (connection is null || petHandle == 0)
        {
            return false;
        }

        connection.Send(GamePetPackets.BuildUnsummonPet(petHandle));
        connection.Send(GameSpawnPackets.BuildLeave(petHandle));

        _logger.Debug("{ClientTag} pet {Handle} leaves the world (master {MasterHandle})", clientTag,
            petHandle, session?.CharacterHandle);

        return true;
    }
}
