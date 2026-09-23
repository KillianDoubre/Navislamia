using System;
using System.Linq;

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

/// <summary>
/// The pet a character has out: its world handle, the cage that called it, what it entered with, and the
/// state the behaviour tick moves — where it walks, which item it goes for, whether a rename was offered.
/// Mutated under <c>ConnectionInfo.PetLock</c> only.
/// </summary>
public sealed class ActivePet
{
    public ActivePet(uint handle, uint cageHandle, PetWorldEntry entry, float collectRange = 0)
    {
        Handle = handle;
        CageHandle = cageHandle;
        Entry = entry;
        CollectRange = collectRange;
        StartX = DestX = entry?.X ?? 0;
        StartY = DestY = entry?.Y ?? 0;
    }

    public uint Handle { get; }
    public uint CageHandle { get; }
    public PetWorldEntry Entry { get; }

    /// <summary>World units within which the pet collects its master's loot; 0 when it collects nothing.</summary>
    public float CollectRange { get; }

    public float StartX { get; private set; }
    public float StartY { get; private set; }
    public float DestX { get; private set; }
    public float DestY { get; private set; }
    public uint StartTick { get; private set; }
    public uint EndTick { get; private set; }

    /// <summary>The ground item the pet is walking to, 0 when none.</summary>
    public uint PickupTarget { get; set; }

    /// <summary>A 353 was sent for this pet: the 354 that echoes its handle may rename it.</summary>
    public bool RenameOffered { get; set; }

    public (float X, float Y) PositionAt(uint nowTick) =>
        MonsterMovement.PositionAt(StartX, StartY, DestX, DestY, StartTick, EndTick, nowTick);

    public bool HasArrived(uint nowTick) => unchecked((int)(nowTick - EndTick)) >= 0;

    /// <summary>Starts a move from where the pet is now; the caller broadcasts it with the same tick and speed.</summary>
    public void MoveTo(float x, float y, byte speed, uint nowTick)
    {
        var (fromX, fromY) = PositionAt(nowTick);
        StartX = fromX;
        StartY = fromY;
        DestX = x;
        DestY = y;
        StartTick = nowTick;
        EndTick = MonsterMovement.EndTick(nowTick, PetSummonRules.Distance(fromX, fromY, x, y), speed);
    }
}

/// <summary>
/// The pure decisions of a pet: what a cage use does, the entry frame values, where to follow, and whether
/// a name is acceptable. The values no source settles are gathered in <see cref="PetSummonDefaults"/>.
/// </summary>
public static class PetSummonRules
{
    /// <summary>
    /// World units per meter. A skill range is stored in meters and NGemity multiplies it by 12 wherever it
    /// becomes a distance (<c>SkillProp.cpp</c>, <c>GetVar(n) * 12.0f</c>; <c>Unit.cpp:510</c>,
    /// <c>DEFAULT_UNIT_SIZE</c>).
    /// </summary>
    public const float UnitsPerMeter = 12f;

    public const int NameMinLength = 4;
    public const int NameMaxLength = 18;

    public static float MetersToUnits(int meters) => meters > 0 ? meters * UnitsPerMeter : 0f;

    public static float Distance(float x1, float y1, float x2, float y2) =>
        MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

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
        byte layer, bool isFirstEnter, string name = null) => new()
    {
        CageHandle = cageHandle,
        PetCode = (uint)pet.PetId,
        Code = pet.PetId,
        Unknown = PetSummonDefaults.Unknown,
        Name = name ?? pet.Name ?? string.Empty,
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

    /// <summary>
    /// Where a pet should walk to stay with its master, or null when it is close enough.
    /// <para>
    /// The master is where it <b>is</b>, never where it is going: heading for the destination made a pet
    /// faster than its master reach it first, i.e. overtake it (measured in game on 2026-09-23). A walking
    /// master (a non-zero <paramref name="headingX"/>/<paramref name="headingY"/>, unit vector) is trailed
    /// <see cref="PetSummonDefaults.FollowGap"/> behind, along its heading, so the target is always behind it;
    /// a standing master is joined to the same gap on the pet's own side.
    /// </para>
    /// </summary>
    public static (float X, float Y)? FollowTarget(float petX, float petY, float masterX, float masterY,
        float headingX = 0, float headingY = 0)
    {
        var distance = Distance(petX, petY, masterX, masterY);
        if (distance <= PetSummonDefaults.FollowDistance)
        {
            return null;
        }

        if (headingX != 0 || headingY != 0)
        {
            return (masterX - headingX * PetSummonDefaults.FollowGap, masterY - headingY * PetSummonDefaults.FollowGap);
        }

        var ratio = PetSummonDefaults.FollowGap / distance;
        return (masterX + (petX - masterX) * ratio, masterY + (petY - masterY) * ratio);
    }

    /// <summary>The unit vector from (x, y) to its destination, or (0, 0) once there.</summary>
    public static (float X, float Y) Heading(float x, float y, float destX, float destY)
    {
        var length = Distance(x, y, destX, destY);
        return length < 0.5f ? (0, 0) : ((destX - x) / length, (destY - y) / length);
    }

    /// <summary>A pet this far from its master is brought back beside it rather than walked there.</summary>
    public static bool IsTooFarToWalk(float petX, float petY, float masterX, float masterY) =>
        Distance(petX, petY, masterX, masterY) > PetSummonDefaults.RecallDistance;

    /// <summary>
    /// A pet name follows the character name rule the server already applies
    /// (<c>StringExtensions.IsValidName</c>, used by the character name check): 4 to 18 letters or digits.
    /// The banned words are checked by the caller, which holds the repository.
    /// </summary>
    public static bool IsValidName(string name) =>
        name is { Length: >= NameMinLength and <= NameMaxLength } && name.All(char.IsLetterOrDigit);
}

/// <summary>
/// The values of a pet that <b>no source settles</b> (docs/packet-specs/socle-familier-pet.md,
/// <c>NON ÉTABLI</c> 2, §11.3, §11.7, §17). They are choices of this repository, gathered here so each one
/// is a single line to change once a capture or a table says otherwise. A pet in Rappelz does not fight, so
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

    /// <summary>The 5th <c>int32</c> of <c>TM_SC_ADD_PET_INFO</c>, named by no source.</summary>
    public const int Unknown = 0;

    /// <summary>
    /// The pet's <c>TS_SC_MOVE</c> speed. The server echoes its players at 100; the pet walks a little faster
    /// so it catches up once its master stops.
    /// </summary>
    public const byte MoveSpeed = 120;

    /// <summary>The pet stays put while its master's destination is within 3 m of it.</summary>
    public const float FollowDistance = 3 * PetSummonRules.UnitsPerMeter;

    /// <summary>When it moves, the pet stops 2 m short of its master's destination.</summary>
    public const float FollowGap = 2 * PetSummonRules.UnitsPerMeter;

    /// <summary>
    /// Beyond this distance the pet is taken out and brought back beside its master: the client view, since
    /// a pet out of view is dropped by the client (CLAUDE.md, the 540-unit window).
    /// </summary>
    public const float RecallDistance = 540f;
}
