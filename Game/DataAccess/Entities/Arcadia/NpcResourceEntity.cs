using System;

namespace Navislamia.Game.DataAccess.Entities.Arcadia;

public class NpcResourceEntity : Entity
{
    public int TextId { get; set; }
    public int NameId { get; set; }
    public int RaceId { get; set; }
    public int SexualId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Face { get; set; }
    public int LocalFlag { get; set; }
    public bool IsPeriodic { get; set; }
    public DateTime BeginOfPeriod { get; set; }
    public DateTime EndOfPeriod { get; set; }
    public int FaceX { get; set; }
    public int FaceY { get; set; }
    public int FaceZ { get; set; }
    public string ModelFile { get; set; }
    public int HairId { get; set; }
    public int FaceId { get; set; }
    public int BodyId { get; set; }
    public int WeaponItemId { get; set; }
    public int ShieldItemId { get; set; }
    public int ClothesItemId { get; set; }
    public int HelmItemId { get; set; }
    public int GlovesItemId { get; set; }
    public int BootsItemId { get; set; }
    public int BeltItemId { get; set; }
    public int MantleItemId { get; set; }
    public int NecklaceItemId { get; set; }
    public int EarringItemId { get; set; }
    public int Ring1ItemId { get; set; }
    public int Ring2ItemId { get; set; }
    public int MotionId { get; set; }
    public int IsRoam { get; set; }
    public int RoamingId { get; set; }
    public int StandardWalkSpeed { get; set; }
    public int StandardRunSpeed { get; set; }
    public int WalkSpeed { get; set; }
    public int RunSpeed { get; set; }
    public int Attackable { get; set; }
    public int OffensiveType { get; set; }
    public int SpawnType { get; set; }
    public int ChaseRange { get; set; }
    public int RegenTime { get; set; }
    public int Level { get; set; }
    public int StatId { get; set; }
    public int AttackRange { get; set; }
    public int AttackSpeedType { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int AttackPoint { get; set; }
    public int MagicPoint { get; set; }
    public int Defence { get; set; }
    public int MagicDefence { get; set; }
    public int AttackSpeed { get; set; }
    public int MagicSpeed { get; set; }
    public int Accuracy { get; set; }
    public int Avoid { get; set; }
    public int MagicAccuracy { get; set; }
    public int MagicAvoid { get; set; }
    public string AiScript { get; set; }
    public string ContactScript { get; set; }
    public int TextureGroup { get; set; }
    public int Type { get; set; }
}
