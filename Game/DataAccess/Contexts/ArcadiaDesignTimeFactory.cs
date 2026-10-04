using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Navislamia.Game.DataAccess.Contexts;

public sealed class ArcadiaDesignTimeFactory : IDesignTimeDbContextFactory<ArcadiaContext>
{
    public ArcadiaContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ArcadiaContext>()
        .UseNpgsql("Host=localhost;Database=Arcadia;Username=postgres;Password=design-time").Options);
}
