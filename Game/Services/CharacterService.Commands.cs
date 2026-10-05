using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

public partial class CharacterService
{
    public Task<ResultCode> RenameCharacterAsync(string oldName, string newName) =>
        _gate.RunPairAsync(oldName, newName ?? string.Empty, async () =>
        {
            if (!CharacterNameRules.Valid(newName) || _bannedWords?.ContainsBannedWord(newName) == true)
                return ResultCode.InvalidText;
            if (oldName == newName) return ResultCode.AlreadyExist;
            if (_nameCodePage is 1252 or 1250 or 1254 && CharacterNameRules.Reformat(newName) != newName)
                return ResultCode.InvalidText;
            using var repository = _repositories.Create();
            var character = await repository.GetCharacterByNameAsync(oldName);
            if (character is null) return ResultCode.NotExist;
            if (character.WasNameChanged) return ResultCode.AccessDenied;
            if (await repository.CharacterExistsAsync(newName)) return ResultCode.AlreadyExist;
            character.CharacterName = newName;
            character.WasNameChanged = true;
            try { await repository.SaveChangesAsync(); }
            catch (DbUpdateException) { return ResultCode.DBError; }
            return ResultCode.Success;
        });

    public Task<ResultCode> RenameSummonAsync(string characterName, long summonId, string name, long gold) =>
        RunExclusiveAsync(characterName, async repository =>
        {
            if (!CharacterNameRules.Valid(name) || _bannedWords?.ContainsBannedWord(name) == true)
                return ResultCode.InvalidText;
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            if (character is null) return ResultCode.NotExist;
            var summon = (await repository.GetSummonsAsync(character.Id)).FirstOrDefault(s => s.Id == summonId);
            if (summon is null || !character.Items.Any(i => i.Id == summon.CardItemId && i.Amount > 0
                && i.AccountId is null && i.StorageId is null && i.AuctionId is null)) return ResultCode.NotExist;
            if (string.Equals(name, summon.Name, StringComparison.OrdinalIgnoreCase)) return ResultCode.AlreadyExist;
            summon.Name = name;
            character.Gold = gold;
            try { await repository.SaveChangesAsync(); }
            catch (DbUpdateException) { return ResultCode.DBError; }
            return ResultCode.Success;
        });

    public string NameReformat(string name) =>
        _nameCodePage is 1252 or 1250 or 1254 ? CharacterNameRules.Reformat(name) : null;

    public Task<bool> SaveChatBlockTimeAsync(string name, int remainingSeconds) =>
        RunExclusiveAsync(name, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(name);
            if (character is null) return false;
            character.ChatBlockTime = Math.Clamp(remainingSeconds, 0, 144000 * 60);
            await repository.SaveChangesAsync();
            return true;
        });
}

public static class CharacterNameRules
{
    // The current transport encodes chat and TS_SC_CHANGE_NAME as ASCII. International name support
    // requires changing that established transport as a separate task.
    public static bool Valid(string name) => name is { Length: >= 4 and <= 18 }
        && name.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
    public static string Reformat(string name) => string.IsNullOrEmpty(name) ? name
        : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
}
