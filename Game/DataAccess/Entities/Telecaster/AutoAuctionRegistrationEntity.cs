using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>The last successful automatic registration, retained after its auction ends.</summary>
public class AutoAuctionRegistrationEntity : Entity
{
    public int ResourceId { get; set; }
    public DateTime LastRegisteredTime { get; set; }
}
