namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One favor counter of a character: the official <c>StructPlayer::AddFavor</c> keeps an id → value table,
/// unbounded, a missing id reading 0. The id is a favor group, or the NPC id itself for the group 999
/// every quest of the Epic 7 data uses (docs/packet-specs/socle-cycle-quete.md §12).
/// </summary>
public class CharacterFavorEntity : Entity
{
    public long CharacterId { get; set; }

    public int FavorId { get; set; }

    public int Value { get; set; }
}
