using System;

namespace Navislamia.Game.DataAccess.Entities.Auth;

public class AccountEntity
{
    public int Id { get; set; }

    public string Username { get; set; }

    public string PasswordHash { get; set; }

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public int LastServerIdx { get; set; }
}
