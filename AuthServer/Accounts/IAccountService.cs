using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Auth;

namespace Navislamia.AuthServer.Accounts;

public interface IAccountService
{
    Task<AccountEntity?> ValidateCredentialsAsync(string username, string password);

    Task<AccountEntity> CreateAccountAsync(string username, string password);
}
