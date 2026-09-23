namespace Navislamia.Game.Services;

/// <summary>
/// Everything <see cref="PetWorldService"/> needs to put one familier (pet) on the wire, and nothing
/// else: what a reference settles is documented, what it does not is <b>caller-supplied</b>, as
/// <c>docs/packet-specs/socle-familier-pet.md</c> §9.1.4 requires. A field whose value is still open is
/// never given a plausible-looking default here — the caller states it or the pet does not enter.
/// <para>
/// There is no <c>FromEntity</c> counterpart to <c>SummonWorldEntry.FromEntity</c>, on purpose: the two
/// links a pet tram carries are rapprochements the fiche keeps as parameters (fiche §9.2: <c>pet_code</c>
/// ← <c>PetEntity.PetResourceId</c>, <c>cage_handle</c> ← the object of <c>PetEntity.ItemId</c>, neither
/// guaranteed by a key), and the row carries no statistic at all (fiche §11.3) — the only field left to
/// read would be the name. Reading the row here would therefore be an implicit join the lot forbids.
/// </para>
/// </summary>
public sealed class PetWorldEntry
{
    /// <summary>
    /// <c>cage_handle</c> — the first field of <c>TM_SC_ADD_PET_INFO</c> (351, absolute offset 7,
    /// <c>BuildAddPetInfo</c>), the cage the pet is stored in. <c>PetEntity.ItemId</c> is a rapprochement
    /// by symmetry with <c>SummonEntity.CardItemId</c> and it is not established whether the client wants
    /// the object's handle or the <c>ItemId</c> (fiche <c>NON ÉTABLI</c> 6): the caller supplies it, and no
    /// join is made here.
    /// </summary>
    public uint CageHandle { get; init; }

    /// <summary>
    /// <c>pet_code</c> of the entry (3, offset 68). <c>PetEntity.PetResourceId</c> is the reference's
    /// rapprochement — the same shape as <c>SummonEntity.SummonResourceId</c> → <c>summon_code</c> — but no
    /// foreign key guarantees the column holds a <c>PetResource.id</c> (fiche <c>NON ÉTABLI</c> 1), so the
    /// caller supplies it.
    /// </summary>
    public uint PetCode { get; init; }

    /// <summary>
    /// The 4ᵉ <c>int32</c> of <c>TM_SC_ADD_PET_INFO</c> (offset 34), the one both references call
    /// <c>code</c> — and the only name they give it. Its source is <b>not established</b> (fiche
    /// <c>NON ÉTABLI</c> 2). Unlike the summon socle, this field is deliberately <b>not</b> unified with
    /// <see cref="PetCode"/>: no reference states that the two carry one value, and the symmetry with
    /// <c>ADD_SUMMON_INFO</c> is an argument, not a proof. Do not substitute a guess.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// The <b>fifth</b> <c>int32</c> of <c>TM_SC_ADD_PET_INFO</c> (offset 38). No source names it: rzu and
    /// NGemity stop at <c>code</c> and the client copies it without a label (fiche <c>NON ÉTABLI</c> 2).
    /// The caller supplies it and it must not receive an invented constant.
    /// </summary>
    public int Unknown { get; init; }

    /// <summary>
    /// The pet's display name — <c>PetEntity.Name</c>, 18 usable characters. It is written in both frames
    /// of the entry (351 @15 and 3 @76), from this one value.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// <c>level</c> of the entry (3, offset 50). <b>Caller-supplied</b>: neither <c>PetEntity</c> nor
    /// <c>PetResource</c> has a level column (fiche §11.3). The 351 carries no level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>The pet's current health: <c>hp</c> of the entry only — the 351 carries no HP.</summary>
    public int Hp { get; init; }

    /// <summary>
    /// <c>max_hp</c> of the entry. <b>Caller-supplied</b>: no table carries a pet's maximum and NGemity
    /// has no pet logic at all (fiche §9.2, §11.3). Do not substitute <see cref="Hp"/>.
    /// </summary>
    public int MaxHp { get; init; }

    /// <summary>The pet's current mana: <c>mp</c> of the entry only — the 351 carries no MP.</summary>
    public int Mp { get; init; }

    /// <summary><c>max_mp</c> of the entry — caller-supplied, same reserve as <see cref="MaxHp"/>.</summary>
    public int MaxMp { get; init; }

    /// <summary>
    /// <c>race</c> of the creature block (offset 54). The field exists in 7.3
    /// (<c>&lt; EPIC_9_6_7</c>) and no server source carries one for a pet: caller-supplied
    /// (fiche §11.7).
    /// </summary>
    public byte Race { get; init; }

    /// <summary>
    /// <c>face_direction</c> of the entry (offset 30) — caller-supplied: nothing establishes which way a
    /// pet faces (fiche §11.7).
    /// </summary>
    public float FaceDirection { get; init; }

    /// <summary>
    /// <c>x</c> of the entry (offset 12). <b>Caller-supplied</b>, with <see cref="Y"/>, <see cref="Z"/> and
    /// <see cref="Layer"/>: the summon socle jitters its master's position on three paliers that come from
    /// three named reference sites (<c>Skill.cpp:640</c>, <c>Player.cpp:820</c>, <c>World.cpp:468</c>) and
    /// <b>none of them has a pet equivalent</b> — no reference places a pet in the world at all (fiche
    /// §9.1.4, §11.7-8). Inventing a palier here would be deciding a game rule, so the whole placement is
    /// the caller's.
    /// </summary>
    public float X { get; init; }

    /// <summary><c>y</c> of the entry (offset 16) — caller-supplied, see <see cref="X"/>.</summary>
    public float Y { get; init; }

    /// <summary><c>z</c> of the entry (offset 20) — caller-supplied, see <see cref="X"/>.</summary>
    public float Z { get; init; }

    /// <summary><c>layer</c> of the entry (offset 24) — caller-supplied, see <see cref="X"/>.</summary>
    public byte Layer { get; init; }

    /// <summary>
    /// 1 on the first entry into the world, 0 on a re-entry (offset 59, fiche <c>NON ÉTABLI</c> 7).
    /// </summary>
    public bool IsFirstEnter { get; init; }
}
