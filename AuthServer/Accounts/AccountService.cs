using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.AuthServer.Accounts;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;

    public AccountService(IAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<AccountEntity?> ValidateCredentialsAsync(string username, string password)
    {
        var account = await _accounts.GetByUsernameAsync(username);
        if (account is null)
        {
            return null;
        }

        return PasswordHasher.Verify(password, account.PasswordHash) ? account : null;
    }

    public async Task<AccountEntity> CreateAccountAsync(string username, string password)
    {
        var account = new AccountEntity
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password)
        };

        return await _accounts.AddAsync(account);
    }
}
