using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services.Friends;

/// <summary>A character in a friend or block list: its id, and its name as <c>Characters</c> holds it.</summary>
public sealed record FriendEntry(long Id, string Name);

/// <summary>The lists of one character, as read at world entry (<c>DB_Login::readFriendsList</c>/<c>readDenialsList</c>).</summary>
public sealed record FriendLists(IReadOnlyList<FriendEntry> Friends, IReadOnlyList<FriendEntry> Denials);

/// <summary>The persistence of the friend and block lists (<c>CharacterFriends</c>).</summary>
public interface IFriendStore
{
    Task<FriendLists> LoadAsync(long ownerId);

    /// <summary>The character of that name, case aside (<c>smp_read_guild_party_id</c>); null when none exists.</summary>
    Task<FriendEntry> FindCharacterAsync(string name);

    Task AddAsync(long ownerId, long targetId, bool isDenial);

    Task RemoveAsync(long ownerId, long targetId, bool isDenial);

    /// <summary>
    /// <c>smp_update_character_name</c> with <c>IN_REMOVE_FROM_FRIEND_DENIAL</c>: the character leaves every other
    /// player's friend and block lists.
    /// </summary>
    Task RemoveFromOthersAsync(long targetId);
}

public sealed class FriendStore : IFriendStore
{
    private readonly DbContextOptions<TelecasterContext> _options;

    public FriendStore(DbContextOptions<TelecasterContext> options) => _options = options;

    public async Task<FriendLists> LoadAsync(long ownerId)
    {
        await using var db = new TelecasterContext(_options);
        // The join drops a deleted character: Characters carries the soft-deletion filter.
        var rows = await db.CharacterFriends.AsNoTracking()
            .Where(f => f.OwnerId == ownerId)
            .Join(db.Characters, f => f.TargetId, c => c.Id, (f, c) => new { f.Id, f.IsDenial, c.CharacterName, TargetId = c.Id })
            .OrderBy(r => r.Id)
            .ToListAsync();
        return new FriendLists(
            rows.Where(r => !r.IsDenial).Select(r => new FriendEntry(r.TargetId, r.CharacterName)).ToList(),
            rows.Where(r => r.IsDenial).Select(r => new FriendEntry(r.TargetId, r.CharacterName)).ToList());
    }

    public async Task<FriendEntry> FindCharacterAsync(string name)
    {
        await using var db = new TelecasterContext(_options);
        var lowered = name.ToLowerInvariant();
        return await db.Characters.AsNoTracking()
            .Where(c => c.CharacterName.ToLower() == lowered)
            .OrderBy(c => c.Id)
            .Select(c => new FriendEntry(c.Id, c.CharacterName))
            .FirstOrDefaultAsync();
    }

    public async Task AddAsync(long ownerId, long targetId, bool isDenial)
    {
        await using var db = new TelecasterContext(_options);
        db.CharacterFriends.Add(new CharacterFriendEntity { OwnerId = ownerId, TargetId = targetId, IsDenial = isDenial });
        await db.SaveChangesAsync();
    }

    public async Task RemoveFromOthersAsync(long targetId)
    {
        await using var db = new TelecasterContext(_options);
        var rows = await db.CharacterFriends.Where(f => f.TargetId == targetId).ToListAsync();
        db.CharacterFriends.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(long ownerId, long targetId, bool isDenial)
    {
        await using var db = new TelecasterContext(_options);
        var rows = await db.CharacterFriends
            .Where(f => f.OwnerId == ownerId && f.TargetId == targetId && f.IsDenial == isDenial)
            .ToListAsync();
        db.CharacterFriends.RemoveRange(rows);
        await db.SaveChangesAsync();
    }
}
