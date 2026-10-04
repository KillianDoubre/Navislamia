namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>Actual credited donations, independently of spent moral points. Period is server-local YYYYMM.</summary>
public sealed class DonationScoreEntity : Entity
{
    public long CharacterId { get; set; }
    public int Period { get; set; }
    public decimal Score { get; set; }
}
