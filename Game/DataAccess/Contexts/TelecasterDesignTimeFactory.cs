using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Navislamia.Game.DataAccess.Contexts;

/// <summary>Build the Telecaster model for migrations without booting the game or loading Arcadia.</summary>
public sealed class TelecasterDesignTimeFactory : IDesignTimeDbContextFactory<TelecasterContext>
{
    public TelecasterContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TelecasterContext>()
        .UseNpgsql(System.Environment.GetEnvironmentVariable("NAVISLAMIA_TELECASTER_CONNECTION")
            ?? "Host=localhost;Database=Telecaster;Username=postgres;Password=design-time").Options);
}
