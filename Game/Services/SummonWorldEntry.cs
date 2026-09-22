using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// Everything <see cref="SummonWorldService"/> needs to put one summon on the wire, and nothing else:
/// what a reference settles is defaulted or documented, what it does not is <b>caller-supplied</b>, as
/// <c>docs/packet-specs/socle-invocation-monde.md</c> §7 requires. A field whose value is still open is
/// never given a plausible-looking default here — the caller states it or the summon does not enter.
/// </summary>
public sealed class SummonWorldEntry
{
    /// <summary>
    /// The card the summon belongs to — <c>card_handle</c>, the first field of <c>TS_SC_ADD_SUMMON_INFO</c>
    /// (301, absolute offset 7, <c>BuildAddSummonInfo</c>) and of <c>TS_SC_REMOVE_SUMMON_INFO</c> (302).
    /// <c>SummonEntity.CardItemId</c> per §6.
    /// </summary>
    public uint CardHandle { get; init; }

    /// <summary>
    /// <c>code</c> of the 301 and <c>summon_code</c> of the entry (3, offset 68): the reference reads
    /// <c>SummonEntity.SummonResourceId</c> for the second and the same value for the first — the narrowing
    /// §6 records — but the caller supplies it because no foreign key guarantees the column holds a
    /// <c>SummonResource.id</c> (<c>NON ÉTABLI</c> 3). Written as an <c>int32</c> in the 301 and as an
    /// encoded <c>uint32</c> in the 3: the two fields carry one value, the cast is the only difference.
    /// </summary>
    public int Code { get; init; }

    /// <summary>The summon's display name — <c>SummonEntity.Name</c>, 18 usable characters (§3.1, offset 76).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary><c>SummonEntity.Lv</c>: level of the entry (offset 50) and of the 301 (offset 44).</summary>
    public int Level { get; init; }

    /// <summary><c>SummonEntity.Sp</c> — the 301 only; the entry tram carries no SP (§3.1).</summary>
    public int Sp { get; init; }

    /// <summary>The summon's current health: <c>hp</c> of the entry only — the 301 carries no HP.</summary>
    public int Hp { get; init; }

    /// <summary>
    /// <c>max_hp</c> of the entry. <b>Caller-supplied</b>: NGemity sends the summon's own maximum, which is
    /// not implemented on its side either, so no reference settles the value (§7, <c>NON ÉTABLI</c> 5).
    /// </summary>
    public int MaxHp { get; init; }

    /// <summary>The summon's current mana: <c>mp</c> of the entry only — the 301 carries no MP.</summary>
    public int Mp { get; init; }

    /// <summary><c>max_mp</c> of the entry — caller-supplied, same reserve as <see cref="MaxHp"/>.</summary>
    public int MaxMp { get; init; }

    /// <summary>
    /// <c>face_direction</c> of the entry (offset 30), copied from the master's by the reference: the
    /// caller decides from the master's state (§5.2).
    /// </summary>
    public float FaceDirection { get; init; }

    /// <summary>
    /// The <c>z</c> the summon is placed at. <b>Caller-supplied</b>: the reference sends the summon's own
    /// <c>z</c>, which its placement never sets — do not assume the master's (§5.2, <c>NON ÉTABLI</c> 6).
    /// </summary>
    public float Z { get; init; }

    /// <summary>
    /// <c>enhance</c> of the entry (offset 95), a field Epic ≥ 7.1 added and nothing fills yet
    /// (<c>NON ÉTABLI</c> 7). Leave at 0 while no source exists.
    /// </summary>
    public byte Enhance { get; init; }

    /// <summary>
    /// 1 on the first entry into the world, 0 on a re-entry (offset 59, <c>NON ÉTABLI</c> 8).
    /// </summary>
    public bool IsFirstEnter { get; init; }

    /// <summary>
    /// The bounded jitter applied to the master's position: one of
    /// <see cref="SummonWorldService.SummonNoiseRange"/> (summoning), <see
    /// cref="SummonWorldService.LoginNoiseRange"/> (login) or <see
    /// cref="SummonWorldService.WarpNoiseRange"/> (warp) — the caller knows which path it is on.
    /// 0 places the summon exactly on its master, which the reference never does (§5.2, §5.4).
    /// </summary>
    public int NoiseRange { get; init; }

    /// <summary>
    /// Reads the fields a summon's own row carries (§6) and takes the rest from the caller — the shape
    /// <c>GameSummonPackets.BuildAddSummonInfo(SummonEntity, …)</c> already uses.
    /// </summary>
    public static SummonWorldEntry FromEntity(SummonEntity summon, int code, int maxHp, int maxMp,
        float faceDirection, float z, int noiseRange, bool isFirstEnter, byte enhance = 0) =>
        new()
        {
            CardHandle = (uint)summon.CardItemId,
            Code = code,
            Name = summon.Name,
            Level = summon.Lv,
            Sp = summon.Sp,
            Hp = summon.Hp,
            MaxHp = maxHp,
            Mp = summon.Mp,
            MaxMp = maxMp,
            FaceDirection = faceDirection,
            Z = z,
            Enhance = enhance,
            IsFirstEnter = isFirstEnter,
            NoiseRange = noiseRange,
        };
}
