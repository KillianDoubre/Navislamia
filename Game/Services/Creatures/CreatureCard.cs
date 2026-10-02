using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// A creature card of the bag as the session knows it: the item, its flag, and the summon it holds — the
/// official <c>StructItem::GetSummonStruct</c>. <see cref="SummonHandle"/> is allocated once per session, when the
/// summon is first announced (301), and kept for every entry into the world, like the official summon object.
/// </summary>
public sealed class CreatureCard
{
    public long ItemId { get; init; }
    public int Code { get; init; }
    public long Amount { get; set; }
    public ItemFlag Flag { get; set; }
    public int Enhance { get; init; }
    public long SummonId { get; set; }
    public int SummonCode { get; set; }
    public string SummonName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public uint SummonHandle { get; set; }

    /// <summary>Whether 301 has been sent this session (login for a slotted card, formation otherwise).</summary>
    public bool InfoSent { get; set; }

    public uint Handle => (uint)ItemId;
    public bool IsBound => CreatureRules.IsBound(Flag);
    public bool HasSummon => SummonId != 0 && SummonCode != 0;
}
