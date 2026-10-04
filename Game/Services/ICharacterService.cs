using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// What a skill reset or a race change writes, all absolute (docs/packet-specs/socle-changement-metier.md §8): the skills
/// kept, the JP, the talent points and the job level, and as the case may be the gold, the reset counter and the race.
/// </summary>
public sealed record SkillResetWrite(IReadOnlyDictionary<int, byte> Skills, long Jp, int TalentPoint, int JobLevel,
    long? Gold = null, int? ResetCount = null, RaceChangeWrite Race = null);

/// <summary>A race change: the new race and its base job, and the stone it takes (one unit).</summary>
public sealed record RaceChangeWrite(int Race, int Job, int StoneResourceId);

/// <summary>Whether it was saved, and the race stone's stack afterwards (amount 0 when it is gone).</summary>
public sealed record SkillResetCommit(bool Saved, ItemEntity Stone = null);

public interface ICharacterService
{
    /// <summary>Writes a skill reset or a race change in one save; refused when the race stone is missing.</summary>
    Task<SkillResetCommit> ApplySkillResetAsync(string characterName, SkillResetWrite write) =>
        Task.FromResult(new SkillResetCommit(false));

    /// <summary>The character's script flags (<c>get_flag</c>, <c>Characters.FlagList</c>).</summary>
    Task<IReadOnlyDictionary<string, string>> GetFlagsAsync(string characterName) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    Task<IReadOnlyList<ItemEntity>> ConsumeEtherealAsync(string characterName, Func<ItemEntity, int> amount) =>
        Task.FromResult<IReadOnlyList<ItemEntity>>(Array.Empty<ItemEntity>());
    Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false);

    /// <summary>
    /// The character entering the world, with its items and skills, or <c>null</c> when it does not
    /// belong to the account.
    /// </summary>
    Task<CharacterEntity> GetCharacterForWorldEntryAsync(string accountName, string characterName);

    Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false);

    Task<Navislamia.Game.Network.Packets.ResultCode> RenameCharacterAsync(string oldName, string newName) => Task.FromResult(Navislamia.Game.Network.Packets.ResultCode.AccessDenied);
    Task<bool> SaveChatBlockTimeAsync(string name, int remainingSeconds) => Task.FromResult(false);

    Task<bool> CharacterExistsAsync(string characterName);

    Task<int> CharacterCountAsync(int accountId);

    Task<CharacterEntity> GetCharacterByNameAsync(string characterName);

    /// <summary>Marks this character, or clears every character of its account (Lua set_auto_user).</summary>
    Task<bool> SetAutoUsedAsync(string accountName, string characterName, bool value);

    Task DeleteCharacterByNameAsync(string characterName);

    Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo);

    Task<bool> UpdateHideEquipFlagAsync(string characterName, int hideEquipFlag);

    Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp);

    /// <summary>A talent skill's level: the skill, the JP and the talent points left, in one save.</summary>
    Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp, int remainingTalentPoint) =>
        Task.FromResult(false);

    /// <summary>
    /// The character's carried quests, ordered by code: the state <c>TM_SC_QUEST_LIST</c> (600) exposes
    /// and <c>TM_CS_DROP_QUEST</c> (603) erases.
    /// </summary>
    Task<CharacterQuestEntity[]> GetQuestsAsync(string characterName);

    /// <summary>
    /// Erases the quest <paramref name="code"/> from the character's state and reports whether a row was
    /// removed — the equivalent of NGemity's <c>CHARACTER_DEL_QUEST</c>. The character's own list is the
    /// only condition.
    /// </summary>
    Task<bool> DropQuestAsync(string characterName, int code);

    Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position);

    Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position);

    /// <summary>
    /// The items of <paramref name="itemIds"/> worn by the character go back to the bag (<c>DB_Login</c>: a worn
    /// item that no longer passes its requirements is not worn), in one save.
    /// </summary>
    Task UnwearItemsAsync(string characterName, IReadOnlyCollection<long> itemIds) => Task.CompletedTask;

    /// <summary>
    /// <c>StructCreature::Puton</c> on a summon: the bag item of <paramref name="itemHandle"/> goes to the slot
    /// <paramref name="chooseSlot"/> picks from the summon's worn items (null refuses), displacing what that slot
    /// held, in one save.
    /// </summary>
    Task<SummonEquipResult> EquipSummonItemAsync(string characterName, uint itemHandle, long summonId,
        Func<ItemEntity, IReadOnlyList<ItemEntity>, int?> chooseSlot) =>
        Task.FromResult(new SummonEquipResult(Navislamia.Game.Network.Packets.ResultCode.NotActable, null, null));

    /// <summary><c>StructSummon::putoffItem</c>: the item the summon wears at <paramref name="slot"/> goes back to the bag.</summary>
    Task<ItemEntity> UnequipSummonItemAsync(string characterName, long summonId, int slot) =>
        Task.FromResult<ItemEntity>(null);

    /// <summary>Every item the character's summons wear.</summary>
    Task<IReadOnlyList<ItemEntity>> GetSummonEquipmentAsync(string characterName) =>
        Task.FromResult<IReadOnlyList<ItemEntity>>(Array.Empty<ItemEntity>());

    /// <summary>
    /// <c>onSwapEquip</c> (223): the weapon, shield and their decorations change places with the spare set. Every
    /// spare item that would come to a main slot is judged by <paramref name="mayWearMain"/> first, and one refusal
    /// leaves everything as it was (null). Returns the character and the items that moved.
    /// </summary>
    Task<(CharacterEntity Character, IReadOnlyList<ItemEntity> Moved)?> SwapEquipAsync(string characterName,
        Func<ItemEntity, bool> mayWearMain) =>
        Task.FromResult<(CharacterEntity, IReadOnlyList<ItemEntity>)?>(null);

    Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog);

    Task<ItemEntity> GetItemByHandleAsync(string characterName, uint itemHandle);

    /// <summary>
    /// Sockets the card <paramref name="cardHandle"/> into the item worn at <paramref name="position"/>,
    /// both resolved and written inside the database gate: a socketing judged and applied in one gate
    /// cannot be invalidated by a packet handled in between (the card erased or dropped, the equipment
    /// unequipped). The verdict of <paramref name="catalog"/> is applied there too, for the same reason
    /// as <c>ArrangeInventoryAsync</c>'s own catalog. <see cref="CardSocketResult.Target"/> is
    /// <c>null</c> when nothing is worn at that position, <see cref="CardSocketResult.Card"/> when the
    /// handle resolves to no item of the character.
    /// </summary>
    Task<CardSocketResult> SocketCardAsync(string characterName, ItemWearType position, uint cardHandle,
        ICardSocketCatalog catalog);

    /// <summary>
    /// Writes the character's id into the bearer socket of one of its skill cards, judgement and write inside
    /// the character's gate: an unknown handle is <c>NotFound</c>, another target <c>NotActable</c>, and a
    /// card of another group, worn or already bound <c>AccessDenied</c>; a refusal leaves the item untouched.
    /// </summary>
    Task<SkillCardBindAttempt> BindSkillCardAsync(string characterName, uint itemHandle, uint targetHandle,
        IItemGroupCatalog itemGroups);

    /// <summary>
    /// Clears the bearer socket of one of the character's skill cards, both judgements and write inside
    /// the database gate: the handle is resolved, the unbind rules run in the order of the reference and
    /// the socket is written only when they all pass — a refusal leaves the item untouched. An item
    /// resource <paramref name="catalog"/> does not know cannot be judged and is left ungated.
    /// </summary>
    Task<SkillCardBindResult> UnbindSkillCardAsync(string characterName, uint itemHandle, uint targetHandle,
        IItemGroupCatalog catalog);

    /// <summary>
    /// Takes <paramref name="count"/> units off one of the character's stacks and returns the amount
    /// left, <c>0</c> when the stack ran out and was deleted, or <c>null</c> when the handle resolves to
    /// none of the character's items.
    /// </summary>
    Task<long?> ConsumeItemAsync(string characterName, uint itemHandle, long count);

    /// <summary>
    /// Sockets the stones of one <c>TM_CS_SOULSTONE_CRAFT</c> (260) request: writes the code of every assigned
    /// stone into its chassis of the crafted item, erases each stone, then saves. Everything moves in one write
    /// under the character's own lock, so a request whose items vanished leaves the character exactly as it was.
    /// Handles that resolve to none of the character's items are reported instead of thrown at. The cost is not
    /// charged here: the purse of a character in the world is <c>ConnectionInfo.CharacterGold</c>, not the row,
    /// which only catches up at the session save.
    /// </summary>
    Task<SoulstoneCraftResult> SocketSoulstonesAsync(string characterName, uint craftItemHandle,
        IReadOnlyList<SoulstoneSlotAssignment> assignments);

    /// <summary>
    /// The pet stored in a cage item: its row, created on the first call with <paramref name="defaultName"/>
    /// and <c>WasNameChanged = false</c>. One row per cage (<c>Pets.ItemId</c>, cascading from the item).
    /// </summary>
    Task<PetRecord> GetOrCreatePetAsync(string characterName, long characterId, int accountId, long cageItemId,
        int petResourceId, string defaultName);

    /// <summary>Renames the pet of a cage item and marks it named. False when the cage has no pet row.</summary>
    Task<bool> RenamePetAsync(string characterName, long cageItemId, string name);

    /// <summary>
    /// Takes one unit off the first carried item, in bag order, that <paramref name="match"/> accepts, and
    /// reports it with the amount left (<c>0</c> when the stack ran out and was deleted). <c>null</c> when
    /// no carried item matches. Worn items are never considered. The search and the removal share one
    /// gated operation, so two requests cannot both take the last unit.
    /// </summary>
    Task<(ItemEntity Item, long Remaining)?> ConsumeFirstAsync(string characterName, Func<ItemEntity, bool> match);

    /// <summary>
    /// Resolves one of the character's items and removes the units <paramref name="resolveCount"/>
    /// returns for it, both inside the database gate: a rule judged by <paramref name="resolveCount"/>
    /// cannot be invalidated by a packet handled in between (an equip racing a drop). A count of zero or
    /// less refuses. <see cref="ItemRemoval.Item"/> is <c>null</c> for an unknown handle.
    /// </summary>
    Task<ItemRemoval> RemoveItemAsync(string characterName, uint itemHandle, Func<ItemEntity, long> resolveCount);

    /// <summary>
    /// Applies a decided craft in one save under the character's gate: every consumed stack is checked and
    /// taken, and the target — still in the state the craft was decided on — gets its new enhance and flag
    /// or is destroyed. Nothing applies unless everything does.
    /// </summary>
    /// <summary>
    /// <see cref="ApplyCraftAsync(string, IReadOnlyList{CraftConsumption}, CraftTargetChange?)"/> that also adds the
    /// items a <c>MIX_CREATE_ITEM</c> makes, in the same save.
    /// </summary>
    Task<CraftCommitResult> ApplyCraftWithCreationAsync(string characterName, IReadOnlyList<CraftConsumption> consumed,
        IReadOnlyList<CraftCreation> created) => ApplyCraftAsync(characterName, consumed, null);

    /// <summary>
    /// A whole <see cref="CraftPlan"/> in one save: the stacks consumed, the target change, the mutations of items that
    /// stay, the items made (copies first) and the crafter's ethereal stone. Nothing applies when one stack or item is
    /// not as the plan saw it.
    /// </summary>
    Task<CraftCommitResult> ApplyMixAsync(string characterName, CraftPlan plan) =>
        ApplyCraftAsync(characterName, plan.Consumed, plan.Change);

    Task<CraftCommitResult> ApplyCraftAsync(string characterName, IReadOnlyList<CraftConsumption> consumed,
        CraftTargetChange? change);

    Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2);

    Task<ItemEntity> AddItemAsync(string characterName, int itemResourceId, long count);

    /// <summary>
    /// Moves items from one character's bag to another's and writes both gold balances, in one save under
    /// both characters' gates: every line applies or none does. A whole stack keeps its row (and so its
    /// enhance, sockets and pet); part of a stack becomes a new row with the same attributes.
    /// </summary>
    Task<ItemTransferResult> TransferItemsAsync(ItemTransfer transfer);

    /// <summary>
    /// A two-way <see cref="TransferItemsAsync"/>: both sides' lines are judged before anything moves, then
    /// applied with both balances in one save under both characters' gates.
    /// </summary>
    Task<ItemExchangeResult> ExchangeItemsAsync(ItemExchange exchange);

    /// <summary>Every item row of the character's bag, worn ones included: a read, to weigh what it carries.</summary>
    Task<ItemEntity[]> GetCarriedItemsAsync(string characterName);

    Task<IReadOnlyList<(uint Handle, long Count)>> EraseItemsAsync(string characterName,
        IReadOnlyList<GameActionPackets.EraseItemRequest> requests);

    /// <summary>
    /// Persists the progress a session accumulated. <paramref name="pkMode"/> is the PK mode of the
    /// session, written back to the pre-existing <c>Characters.PkMode</c> column. When supplied,
    /// <paramref name="pvp"/> also saves immoral points and PK/DK counts in their existing columns.
    /// Omitting it preserves those values for callers that do not own a session snapshot.
    /// </summary>
    Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold,
        int chaos, float x, float y, bool pkMode, PvpProgress? pvp = null, int? stamina = null,
        Huntaholic.HuntaholicProgress? huntaholic = null);

    /// <summary>
    /// Writes the return point as the script flags <c>rx</c>/<c>ry</c> of <c>Characters.FlagList</c>, keeping every
    /// other flag (docs/packet-specs/socle-point-de-retour.md). Written when it changes, not with the progress.
    /// </summary>
    Task SaveReturnPointAsync(string characterName, ReturnPoints.ReturnPoint point) => Task.CompletedTask;

    /// <summary>
    /// The character's creature cards (items whose resource is a summon card) with the summon row of each, the
    /// main summon and the six formation slots (docs/packet-specs/socle-apprivoisement-invocation.md §15).
    /// </summary>
    Task<CreatureState> GetCreatureStateAsync(string characterName, IReadOnlyCollection<int> cardIds) =>
        Task.FromResult<CreatureState>(null);

    /// <summary>
    /// <c>ProcTame</c>'s commit: one card of the taming stack is consumed; on success a new bound card (summon flag,
    /// <c>BY_TAMING</c>) and its summon row are created. Null when the character or the card is gone.
    /// </summary>
    Task<TamingCommit> CommitTamingAsync(string characterName, long cardItemId, bool success, int summonCode,
        string summonName, int hp, int mp) => Task.FromResult<TamingCommit>(null);

    /// <summary><c>AllocNewSummon</c> for a bound card that has no summon row yet; null when it cannot be created.</summary>
    Task<SummonEntity> CreateSummonAsync(string characterName, long cardItemId, int summonCode, string summonName,
        int hp, int mp) => Task.FromResult<SummonEntity>(null);

    /// <summary>The six formation slots (card item ids) and the main summon, written together.</summary>
    Task<bool> SaveCreatureFormationAsync(string characterName, long[] slots, long? mainSummonId) =>
        Task.FromResult(false);

    /// <summary>
    /// <c>DB_UpdateSummon</c>: each summon's level, exp, JP, max reached level, vitals, resource (an evolution) and
    /// former forms, written in one save. Rows of another character are ignored.
    /// </summary>
    Task<bool> SaveSummonProgressAsync(string characterName, IReadOnlyList<SummonProgress> summons) =>
        Task.FromResult(false);

    /// <summary>Every skill the character's summons learned (<c>smp_read_summon_skill_list</c>).</summary>
    Task<IReadOnlyList<SummonSkillRecord>> GetSummonSkillsAsync(string characterName) =>
        Task.FromResult<IReadOnlyList<SummonSkillRecord>>(Array.Empty<SummonSkillRecord>());

    /// <summary>
    /// A summon learned a skill level: the row is inserted or raised and the summon's JP set to what is left, in one
    /// save (<c>StructSummon::onRegisterSkill</c>). False when the summon is not the character's.
    /// </summary>
    Task<bool> SaveSummonSkillAsync(string characterName, long summonId, int skillId, byte level, int remainingJp) =>
        Task.FromResult(false);

    /// <summary>
    /// Persists a job change (docs/packet-specs/socle-changement-metier.md): the new job at job level 1, the jobs
    /// left behind with the job level each reached (<c>job_N</c>/<c>jlv_N</c>), the depth flag, and the talent
    /// points the master class grants. Returns the character's talent points afterwards, null when it does not exist.
    /// </summary>
    Task<int?> ChangeJobAsync(string characterName, int job, IReadOnlyList<(int Job, int JobLevel)> previousJobs,
        int talentPoints);

}

/// <summary>What a cage's pet is called, and whether its master has named it yet.</summary>
public readonly record struct PetRecord(string Name, bool WasNameChanged);

/// <summary>A creature card of the bag and its summon row, detached.</summary>
public sealed record CreatureCardRecord(ItemEntity Card, SummonEntity Summon);

/// <summary>What world entry needs of the creatures: the cards, the formation slots and the main summon.</summary>
public sealed record CreatureState(IReadOnlyList<CreatureCardRecord> Cards, long[] Slots, long? MainSummonId);

public sealed record SummonSkillRecord(long SummonId, int SkillId, byte Level);

/// <summary>What <see cref="ICharacterService.SaveSummonProgressAsync"/> writes for one summon.</summary>
public sealed record SummonProgress(long SummonId, int SummonResourceId, int Level, long Exp, int Jp, int MaxLevel,
    int Hp, int Mp, long LastDecreasedExp, long[] PreviousSummonResourceIds, int[] PreviousLevels, string Name);

/// <summary>
/// A taming committed: the taming card left with <see cref="RemainingAmount"/> units (0 = deleted), and on success
/// the new bound card and its summon.
/// </summary>
public sealed record TamingCommit(long ConsumedItemId, long RemainingAmount, ItemEntity NewCard, SummonEntity Summon)
{
    /// <summary>The Mirror of Taming Card the draw broke, if one was in the bag, and what is left of its stack.</summary>
    public long? MirrorItemId { get; init; }
    public long MirrorRemaining { get; init; }
}

/// <summary>A summon's put-on: the result code, the item worn, the item it took the place of.</summary>
public sealed record SummonEquipResult(Navislamia.Game.Network.Packets.ResultCode Code, ItemEntity Equipped,
    ItemEntity Displaced);
