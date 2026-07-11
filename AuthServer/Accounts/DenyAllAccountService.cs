using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Auth;

namespace Navislamia.AuthServer.Accounts;

public sealed class DenyAllAccountService : IAccountService
{
    public Task<AccountEntity?> ValidateCredentialsAsync(string username, string password)
        => Task.FromResult<AccountEntity?>(null);

    public Task<AccountEntity> CreateAccountAsync(string username, string password)
        => throw new InvalidOperationException("Account database is unavailable.");
}
