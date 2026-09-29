namespace Navislamia.Game.DataAccess.Entities.Arcadia;

/// <summary>
/// The `MixResource` table: the crafting recipes the mix engine resolves against
/// (`TM_CS_MIX`, 256). One row per recipe, 109 payload columns, read in the order of the
/// reference schema <c>ArcadiaSchemaPSQL.sql:471-582</c> and loaded positionally by NGemity
/// (<c>ObjectMgr.cpp:1108-1146</c>).
///
/// The columns are carried here one by one, in schema order, rather than as per-group arrays:
/// the reference has no key at all (`id` is unique on the 754 measured rows, so it becomes the
/// primary key) and the migration is expected to reproduce the 109 columns. <c>mix_type</c> is
/// read raw, no `MIX_TYPE` enumeration is written here (NGemity compares it to plain ints,
/// <c>MixManager.cpp:1474-1492</c>).
/// See docs/packet-specs/socle-artisanat-ressources.md §5.1 and §8 (L1a).
/// </summary>
public class MixResourceEntity : Entity
{
    public int MixType { get; set; }
    public int MixValue01 { get; set; }
    public int MixValue02 { get; set; }
    public int MixValue03 { get; set; }
    public int MixValue04 { get; set; }
    public int MixValue05 { get; set; }
    public int MixValue06 { get; set; }
    public int SubMaterialCount { get; set; }
    public int MainType01 { get; set; }
    public int MainValue01 { get; set; }
    public int MainType02 { get; set; }
    public int MainValue02 { get; set; }
    public int MainType03 { get; set; }
    public int MainValue03 { get; set; }
    public int MainType04 { get; set; }
    public int MainValue04 { get; set; }
    public int MainType05 { get; set; }
    public int MainValue05 { get; set; }
    public int Sub01Type01 { get; set; }
    public int Sub01Value01 { get; set; }
    public int Sub01Type02 { get; set; }
    public int Sub01Value02 { get; set; }
    public int Sub01Type03 { get; set; }
    public int Sub01Value03 { get; set; }
    public int Sub01Type04 { get; set; }
    public int Sub01Value04 { get; set; }
    public int Sub01Type05 { get; set; }
    public int Sub01Value05 { get; set; }
    public int Sub02Type01 { get; set; }
    public int Sub02Value01 { get; set; }
    public int Sub02Type02 { get; set; }
    public int Sub02Value02 { get; set; }
    public int Sub02Type03 { get; set; }
    public int Sub02Value03 { get; set; }
    public int Sub02Type04 { get; set; }
    public int Sub02Value04 { get; set; }
    public int Sub02Type05 { get; set; }
    public int Sub02Value05 { get; set; }
    public int Sub03Type01 { get; set; }
    public int Sub03Value01 { get; set; }
    public int Sub03Type02 { get; set; }
    public int Sub03Value02 { get; set; }
    public int Sub03Type03 { get; set; }
    public int Sub03Value03 { get; set; }
    public int Sub03Type04 { get; set; }
    public int Sub03Value04 { get; set; }
    public int Sub03Type05 { get; set; }
    public int Sub03Value05 { get; set; }
    public int Sub04Type01 { get; set; }
    public int Sub04Value01 { get; set; }
    public int Sub04Type02 { get; set; }
    public int Sub04Value02 { get; set; }
    public int Sub04Type03 { get; set; }
    public int Sub04Value03 { get; set; }
    public int Sub04Type04 { get; set; }
    public int Sub04Value04 { get; set; }
    public int Sub04Type05 { get; set; }
    public int Sub04Value05 { get; set; }
    public int Sub05Type01 { get; set; }
    public int Sub05Value01 { get; set; }
    public int Sub05Type02 { get; set; }
    public int Sub05Value02 { get; set; }
    public int Sub05Type03 { get; set; }
    public int Sub05Value03 { get; set; }
    public int Sub05Type04 { get; set; }
    public int Sub05Value04 { get; set; }
    public int Sub05Type05 { get; set; }
    public int Sub05Value05 { get; set; }
    public int Sub06Type01 { get; set; }
    public int Sub06Value01 { get; set; }
    public int Sub06Type02 { get; set; }
    public int Sub06Value02 { get; set; }
    public int Sub06Type03 { get; set; }
    public int Sub06Value03 { get; set; }
    public int Sub06Type04 { get; set; }
    public int Sub06Value04 { get; set; }
    public int Sub06Type05 { get; set; }
    public int Sub06Value05 { get; set; }
    public int Sub07Type01 { get; set; }
    public int Sub07Value01 { get; set; }
    public int Sub07Type02 { get; set; }
    public int Sub07Value02 { get; set; }
    public int Sub07Type03 { get; set; }
    public int Sub07Value03 { get; set; }
    public int Sub07Type04 { get; set; }
    public int Sub07Value04 { get; set; }
    public int Sub07Type05 { get; set; }
    public int Sub07Value05 { get; set; }
    public int Sub08Type01 { get; set; }
    public int Sub08Value01 { get; set; }
    public int Sub08Type02 { get; set; }
    public int Sub08Value02 { get; set; }
    public int Sub08Type03 { get; set; }
    public int Sub08Value03 { get; set; }
    public int Sub08Type04 { get; set; }
    public int Sub08Value04 { get; set; }
    public int Sub08Type05 { get; set; }
    public int Sub08Value05 { get; set; }
    public int Sub09Type01 { get; set; }
    public int Sub09Value01 { get; set; }
    public int Sub09Type02 { get; set; }
    public int Sub09Value02 { get; set; }
    public int Sub09Type03 { get; set; }
    public int Sub09Value03 { get; set; }
    public int Sub09Type04 { get; set; }
    public int Sub09Value04 { get; set; }
    public int Sub09Type05 { get; set; }
    public int Sub09Value05 { get; set; }
}
