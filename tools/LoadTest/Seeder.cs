using Microsoft.EntityFrameworkCore;
using Navislamia.AuthServer.Accounts;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Repositories;

namespace Navislamia.LoadTest;

/// <summary>
/// Creates the bot accounts in the auth database through the AuthServer's own <see cref="AccountService"/>
/// (same password hash as a real account). Characters are not inserted here: each bot creates its own through
/// <c>TM_CS_CREATE_CHARACTER</c> on its first run, the path a real client takes.
/// </summary>
public static class Seeder
{
    public static async Task<int> RunAsync(LoadOptions options)
    {
        if (options.AuthDatabase is null)
        {
            Console.WriteLine("Base d'authentification introuvable : lancer l'outil depuis le dépôt (AuthServer/appsettings.json).");
            return 1;
        }

        var contextOptions = new DbContextOptionsBuilder<AuthContext>().UseNpgsql(options.AuthDatabase).Options;
        var accounts = new AccountRepository(contextOptions);
        var service = new AccountService(accounts);

        var created = 0;
        for (var i = 1; i <= options.Count; i++)
        {
            var name = LoadOptions.AccountName(options.Prefix, i);
            if (await accounts.GetByUsernameAsync(name) is not null) continue;
            await service.CreateAccountAsync(name, options.Password);
            created++;
        }

        Console.WriteLine($"{options.Count} comptes {LoadOptions.AccountName(options.Prefix, 1)}…" +
                          $"{LoadOptions.AccountName(options.Prefix, options.Count)} prêts ({created} créés, " +
                          $"mot de passe « {options.Password} »).");
        return 0;
    }
}
