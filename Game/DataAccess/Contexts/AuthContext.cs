using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Entities.Auth;

namespace Navislamia.Game.DataAccess.Contexts;

public class AuthContext : DbContext
{
    public AuthContext(DbContextOptions<AuthContext> options) : base(options) { }

    public DbSet<AccountEntity> Accounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AccountEntity>()
            .HasIndex(a => a.Username)
            .IsUnique();
    }
}
