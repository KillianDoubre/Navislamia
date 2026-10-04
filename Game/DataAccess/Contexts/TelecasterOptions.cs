using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Navislamia.Game.DataAccess.Contexts;

public static class TelecasterOptions
{
    /// <summary>
    /// The host enables <c>EnableRetryOnFailure</c>, whose <c>NpgsqlRetryingExecutionStrategy</c> refuses a transaction
    /// begun by hand. A service that opens its own transactions, around units with effects outside the database (gold
    /// in session, chat lines) that a retry would replay, takes its contexts without retry instead. Options of another
    /// provider (the tests' in-memory database) are returned as they are.
    /// </summary>
    public static DbContextOptions<TelecasterContext> WithoutRetry(DbContextOptions<TelecasterContext> options)
    {
#pragma warning disable EF1001 // the extension is how a configured provider is recognised
        if (options?.FindExtension<NpgsqlOptionsExtension>() is null) return options;
#pragma warning restore EF1001
        return new DbContextOptionsBuilder<TelecasterContext>(options)
            .UseNpgsql(npgsql => npgsql.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)))
            .Options;
    }
}
