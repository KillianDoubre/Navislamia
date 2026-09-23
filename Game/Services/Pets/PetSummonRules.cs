namespace Navislamia.Game.Services.Pets;

public enum PetCageAction
{
    /// <summary>No pet is out: the cage calls its pet.</summary>
    Summon,

    /// <summary>The pet of this very cage is out: using the cage again puts it away.</summary>
    Dismiss,

    /// <summary>Another cage's pet is out: it is put away and this cage's pet comes out.</summary>
    Swap
}

/// <summary>The pet a character has out: its world handle, the cage that called it and what it entered with.</summary>
public sealed record ActivePet(uint Handle, uint CageHandle, PetWorldEntry Entry);

/// <summary>
/// The pure decisions of calling a pet with its cage: what a use does, and the entry frame values. The
/// values no source settles are gathered in <see cref="PetSummonDefaults"/>, named as choices.
/// </summary>
public static class PetSummonRules
{
    /// <summary>One pet at a time, toggled by its own cage.</summary>
    public static PetCageAction Decide(ActivePet active, uint cageHandle)
    {
        if (active is null)
        {
            return PetCageAction.Summon;
        }

        return active.CageHandle == cageHandle ? PetCageAction.Dismiss : PetCageAction.Swap;
    }

    /// <summary>
    /// The entry of a pet called at the master's position. <c>cage_handle</c> is the handle of the cage the
    /// player used — the item the pet is stored in — and <c>pet_code</c> the client table's id.
    /// </summary>
    public static PetWorldEntry BuildEntry(PetDefinition pet, uint cageHandle, float x, float y, float z,
        byte layer, bool isFirstEnter) => new()
    {
        CageHandle = cageHandle,
        PetCode = (uint)pet.PetId,
        Code = PetSummonDefaults.Code,
        Unknown = PetSummonDefaults.Unknown,
        Name = pet.Name ?? string.Empty,
        Level = PetSummonDefaults.Level,
        Hp = PetSummonDefaults.MaxHp,
        MaxHp = PetSummonDefaults.MaxHp,
        Mp = PetSummonDefaults.MaxMp,
        MaxMp = PetSummonDefaults.MaxMp,
        Race = PetSummonDefaults.Race,
        FaceDirection = PetSummonDefaults.FaceDirection,
        X = x,
        Y = y,
        Z = z,
        Layer = layer,
        IsFirstEnter = isFirstEnter
    };
}

/// <summary>
/// The values of a pet's frames that <b>no source settles</b> (docs/packet-specs/socle-familier-pet.md,
/// <c>NON ÉTABLI</c> 2, §11.3, §11.7). They are choices of this repository, gathered here so each one is a
/// single line to change once a capture or a table says otherwise. A pet in Rappelz does not fight, so
/// nothing reads its statistics server-side.
/// </summary>
public static class PetSummonDefaults
{
    /// <summary>No pet table carries a level (neither <c>PetEntity</c>, <c>PetResource</c> nor <c>db_pet.rdb</c>).</summary>
    public const int Level = 1;

    /// <summary>No table carries a pet's health; the pet enters at full health.</summary>
    public const int MaxHp = 100;

    /// <summary>No table carries a pet's mana.</summary>
    public const int MaxMp = 0;

    public const byte Race = 0;

    public const float FaceDirection = 0f;

    /// <summary>
    /// The 4th <c>int32</c> of <c>TM_SC_ADD_PET_INFO</c>, which both references call <c>code</c>: its source
    /// is not established, and it is deliberately not unified with <c>pet_code</c> (fiche §14.4).
    /// </summary>
    public const int Code = 0;

    /// <summary>The 5th <c>int32</c> of <c>TM_SC_ADD_PET_INFO</c>, named by no source.</summary>
    public const int Unknown = 0;
}
