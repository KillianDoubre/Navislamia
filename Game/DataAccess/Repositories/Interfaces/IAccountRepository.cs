using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Auth;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IAccountRepository
{
    Task<AccountEntity> GetByUsernameAsync(string username);

    Task<AccountEntity> AddAsync(AccountEntity account);

    void EnsureCreated();
}
