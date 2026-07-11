using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly AuthContext _context;

    public AccountRepository(DbContextOptions<AuthContext> options)
    {
        _context = new AuthContext(options);
    }

    public async Task<AccountEntity> GetByUsernameAsync(string username)
    {
        return await _context.Accounts.FirstOrDefaultAsync(a => a.Username == username);
    }

    public async Task<AccountEntity> AddAsync(AccountEntity account)
    {
        var entity = (await _context.Accounts.AddAsync(account)).Entity;
        await _context.SaveChangesAsync();
        return entity;
    }

    public void EnsureCreated()
    {
        _context.Database.EnsureCreated();
    }
}
