using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Weight;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>NPC offers, persistent objectives and atomic quest rewards.</summary>
public sealed class QuestService : IQuestService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<QuestService>();
    private readonly ICharacterService _characters;
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _gate;
    private readonly ILevelingService _leveling;
    private readonly IPlayerVisibilityService _players;
    private readonly IInventoryChangeFeed _feed;
    private readonly IGroundItemService _ground;
    private readonly IMonsterDropCatalog _drops;
    private readonly Dictionary<int, QuestResourceEntity> _resources;
    private readonly Dictionary<int, QuestLinkResourceEntity[]> _links;
    private readonly Dictionary<int, JobResourceEntity> _jobs;
    private readonly Dictionary<int, RandomPoolResourceEntity[]> _pools;
    private readonly ConcurrentDictionary<uint, DateTime> _lastTick = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time;

    public QuestService(ICharacterService characters, IQuestCatalogueRepository catalogue = null,
        DbContextOptions<TelecasterContext> options = null, CharacterGate gate = null,
        ILevelingService leveling = null, IPlayerVisibilityService players = null,
        IInventoryChangeFeed feed = null, IGroundItemService ground = null, IMonsterDropCatalog drops = null,
        TimeProvider timeProvider = null)
    {
        _characters = characters; _options = options; _gate = gate ?? new CharacterGate();
        _leveling = leveling; _players = players; _feed = feed; _ground = ground; _drops = drops;
        _time = timeProvider ?? TimeProvider.System;
        _resources = (catalogue?.GetResources() ?? Array.Empty<QuestResourceEntity>()).ToDictionary(q => q.Id);
        _jobs = (catalogue?.GetJobs() ?? Array.Empty<JobResourceEntity>()).ToDictionary(j => (int)j.Id);
        _pools = (catalogue?.GetRandomPools() ?? Array.Empty<RandomPoolResourceEntity>()).GroupBy(p => p.GroupId).ToDictionary(g => g.Key, g => g.ToArray());
        _links = (catalogue?.GetLinks() ?? Array.Empty<QuestLinkResourceEntity>())
            .GroupBy(l => (l.NpcId, l.QuestId)).Select(g => new QuestLinkResourceEntity
            {
                NpcId = g.Key.NpcId, QuestId = g.Key.QuestId,
                FlagStart = g.Any(l => l.FlagStart == "1") ? "1" : "0",
                FlagProgress = g.Any(l => l.FlagProgress == "1") ? "1" : "0",
                FlagEnd = g.Any(l => l.FlagEnd == "1") ? "1" : "0",
                TextIdStart = g.Select(l => l.TextIdStart).FirstOrDefault(i => i != 0),
                TextIdInProgress = g.Select(l => l.TextIdInProgress).FirstOrDefault(i => i != 0),
                TextIdEnd = g.Select(l => l.TextIdEnd).FirstOrDefault(i => i != 0)
            }).GroupBy(l => l.NpcId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.QuestId).ToArray());
        if (_feed is not null) _feed.Changed += InventoryChanged;
        if (_players is not null && _options is not null) _ = TickAsync();
    }

    public bool HasNpcQuests(int npcId) => _options is not null && _links.ContainsKey(npcId);
    private QuestLinkResourceEntity Link(int npcId, int code) => _links.GetValueOrDefault(npcId)?.FirstOrDefault(l => l.QuestId == code);
    private static bool Timed(QuestResourceEntity resource) => resource.TimeLimit > 0 && resource.TimeLimitType?.Trim() is "1" or "2";
    private int JobDepth(ConnectionInfo player)
    {
        var jobId = player.CharacterJob == 0 ? (player.CharacterRace - 2) * 100 : player.CharacterJob;
        return _jobs.GetValueOrDefault(jobId)?.JobDepth switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => 0 };
    }
    private bool CanStart(QuestResourceEntity resource, ConnectionInfo player, CharacterQuestEntity[] active,
        Dictionary<int, DateTime> completed, Dictionary<int, DateTime> accepted = null, int favor = 0)
    {
        if (accepted is not null && accepted.TryGetValue(resource.Id, out var last)
            && _time.GetUtcNow().UtcDateTime < last.AddSeconds(Math.Max(0, resource.AcceptCoolTime))) return false;
        if (resource.Type == 901 && !TryRandomTargets(resource, out _, false)) return false;
        var job = _jobs.GetValueOrDefault(player.CharacterJob == 0 ? (player.CharacterRace - 2) * 100 : player.CharacterJob);
        // A job's class comes from JobResource; digits in its id do not encode its class.
        var jobClass = job?.JobClass ?? (resource.LimitFighter == "1" && resource.LimitHunter == "1"
            && resource.LimitMagician == "1" && resource.LimitSummoner == "1" ? 1 : 0);
        var depth = job is null ? 0 : job.JobDepth switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => -1 };
        return depth >= 0 && QuestRules.CanStart(resource, player, active, completed, _time.GetUtcNow().UtcDateTime, jobClass, depth, favor);
    }

    public async Task<IReadOnlyList<NpcDialogMenuEntry>> GetNpcOffersAsync(GameClient client, int npcId)
    {
        var menu = new List<NpcDialogMenuEntry>();
        if (!HasNpcQuests(npcId)) return menu;
        await RefreshAsync(client);
        await using var db = new TelecasterContext(_options);
        var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.CharacterName == client.ConnectionInfo.CharacterName);
        if (character is null) return menu;
        var active = await db.CharacterQuests.AsNoTracking().Where(q => q.CharacterId == character.Id).ToArrayAsync();
        var completed = await Completions(db, character.Id);
        var accepted = await Acceptances(db, character.Id);
        foreach (var link in _links[npcId])
        {
            if (!_resources.TryGetValue(link.QuestId, out var resource) || !QuestRules.Supported(resource)) continue;
            var quest = active.FirstOrDefault(q => q.Code == link.QuestId);
            var offered = quest is null ? link.FlagStart == "1" && CanStart(resource, client.ConnectionInfo, active, completed, accepted,
                    await FavorAsync(db, character.Id, resource, npcId))
                : quest.Progress == QuestRules.Finishable ? link.FlagEnd == "1" : link.FlagProgress == "1";
            if (offered) menu.Add(new NpcDialogMenuEntry { Label = $"@{resource.TextIdQuest}", Trigger = $"quest_info({resource.Id})" });
        }
        return menu;
    }

    public async Task<NpcDialogDefinition> GetQuestDialogAsync(GameClient client, int npcId, int code, string title)
    {
        var link = Link(npcId, code);
        if (_options is null || link is null || !_resources.TryGetValue(code, out var resource) || !QuestRules.Supported(resource)) return null;
        await RefreshAsync(client);
        await using var db = new TelecasterContext(_options);
        var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.CharacterName == client.ConnectionInfo.CharacterName);
        if (character is null) return null;
        var active = await db.CharacterQuests.AsNoTracking().Where(q => q.CharacterId == character.Id).ToArrayAsync();
        var quest = active.FirstOrDefault(q => q.Code == code);
        var dialog = new NpcDialogDefinition { Title = title };
        if (quest is null)
        {
            if (link.FlagStart != "1" || !CanStart(resource, client.ConnectionInfo, active,
                await Completions(db, character.Id), await Acceptances(db, character.Id),
                await FavorAsync(db, character.Id, resource, npcId))) return null;
            dialog.Text = $"QUEST|{code}|{link.TextIdStart}";
            dialog.Menu.Add(new NpcDialogMenuEntry { Label = "START", Trigger = $"start_quest({code},{link.TextIdStart})" });
            dialog.Menu.Add(new NpcDialogMenuEntry { Label = "REJECT" });
        }
        else if (quest.Progress == QuestRules.Finishable && link.FlagEnd == "1")
        {
            dialog.Text = $"QUEST|{code}|{link.TextIdEnd}";
            var rewards = Rewards(resource);
            for (var i = 0; i < rewards.Length; i++)
                if (rewards[i].Id > 0 && rewards[i].Count > 0)
                    dialog.Menu.Add(new NpcDialogMenuEntry { Label = "NULL", Trigger = $"end_quest({code},{i})" });
            if (dialog.Menu.Count == 0) dialog.Menu.Add(new NpcDialogMenuEntry { Label = "NULL", Trigger = $"end_quest({code},-1)" });
            dialog.Menu.Add(new NpcDialogMenuEntry { Label = "REWARD" });
        }
        else
        {
            if (link.FlagProgress != "1") return null;
            dialog.Text = $"QUEST|{code}|{link.TextIdInProgress}";
            dialog.Menu.Add(new NpcDialogMenuEntry { Label = "OK" });
        }
        return dialog;
    }

    public async Task StartQuestAsync(GameClient client, int npcId, int code, int textId)
    {
        if (_options is null) return;
        try
        {
            var started = await _gate.RunAsync(client.ConnectionInfo.CharacterName, async () =>
            {
                var link = Link(npcId, code);
                if (link?.FlagStart != "1" || link.TextIdStart != textId || !AtNpc(client, npcId, code)
                    || !_resources.TryGetValue(code, out var resource)) return false;
                await using var db = new TelecasterContext(_options);
                var character = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == client.ConnectionInfo.CharacterName);
                if (character is null) return false;
                var active = await db.CharacterQuests.Where(q => q.CharacterId == character.Id).ToArrayAsync();
                if (!CanStart(resource, client.ConnectionInfo, active, await Completions(db, character.Id),
                    favor: await FavorAsync(db, character.Id, resource, npcId))) return false;
                var last = await db.CharacterQuests.IgnoreQueryFilters().Where(q => q.CharacterId == character.Id && q.Code == code)
                    .OrderByDescending(q => q.CreatedOn).FirstOrDefaultAsync();
                if (last is not null && _time.GetUtcNow().UtcDateTime < last.CreatedOn.AddSeconds(Math.Max(0, resource.AcceptCoolTime))) return false;
                var quest = new CharacterQuestEntity
                {
                    CharacterId = character.Id, Code = code, StartId = textId, Value = new int[6], Status = new int[6],
                    Progress = resource.Type == 401 ? QuestRules.Finishable : QuestRules.InProgress,
                    CreatedOn = _time.GetUtcNow().UtcDateTime,
                    RemainingSeconds = Math.Max(0, resource.TimeLimit),
                    ExpiresAt = resource.TimeLimitType?.Trim() == "2" && resource.TimeLimit > 0 ? _time.GetUtcNow().UtcDateTime.AddSeconds(resource.TimeLimit) : null,
                    TimeLimit = Timed(resource) ? unchecked((int)(ServerClock.Now + (uint)resource.TimeLimit * 100)) : 0
                };
                if (resource.Type == 901)
                {
                    if (!TryRandomTargets(resource, out var targets)) return false;
                    var finished = await db.CharacterQuestCompletions.AsNoTracking().SingleOrDefaultAsync(q => q.CharacterId == character.Id && q.Code == code);
                    // Abandoning and reaccepting keeps the draw until that instance is completed.
                    quest.Value = last?.DeletedOn is not null && (finished is null || finished.CompletedAt < last.CreatedOn)
                        && last.Value?.Length == 6 ? last.Value.ToArray() : targets;
                }
                var items = await db.Items.AsNoTracking().Where(i => i.CharacterId == character.Id).ToArrayAsync();
                QuestRules.ApplyItemCounts(resource, quest, Counts(items));
                QuestRules.ApplyPlayerState(resource, quest, client.ConnectionInfo, items, JobDepth(client.ConnectionInfo));
                quest.Progress = QuestRules.FinishableNow(resource, quest) ? QuestRules.Finishable : QuestRules.InProgress;
                db.CharacterQuests.Add(quest);
                await db.SaveChangesAsync();
                _lastTick.TryAdd(client.ConnectionInfo.CharacterHandle, _time.GetUtcNow().UtcDateTime);
                return true;
            });
            Chat(client, started ? $"START|SUCCESS|{code}" : $"START|FAIL|NOT_STARTABLE|{textId}");
            if (started) await SendQuestListAsync(client);
        }
        catch (Exception exception) { _logger.Error(exception, "Could not accept quest {code}", code); Chat(client, $"START|FAIL|NOT_STARTABLE|{textId}"); }
    }

    public async Task SendQuestListAsync(GameClient client)
    {
        try
        {
            if (_options is not null) { _lastTick.TryAdd(client.ConnectionInfo.CharacterHandle, _time.GetUtcNow().UtcDateTime); await RefreshAsync(client); }
            client.Connection.Send(GameQuestPackets.BuildQuestList(await _characters.GetQuestsAsync(client.ConnectionInfo.CharacterName)));
        }
        catch (Exception exception) { _logger.Error(exception, "Could not read quest list for {clientTag}", client.ClientTag); }
    }

    public async Task DropQuestAsync(GameClient client, GameActionPackets.DropQuestRequest request)
    {
        var verdict = QuestDropRules.CheckRequest(request.Code);
        try
        {
            if (verdict == ResultCode.Success)
                verdict = await _characters.DropQuestAsync(client.ConnectionInfo.CharacterName, request.Code) ? ResultCode.Success : ResultCode.NotActable;
        }
        catch (Exception exception) { _logger.Error(exception, "Could not drop quest {code}", request.Code); verdict = ResultCode.DBError; }
        client.SendResult(603, (ushort)verdict);
        if (verdict == ResultCode.Success) await SendQuestListAsync(client);
    }

    public async Task EndQuestAsync(GameClient client, GameActionPackets.EndQuestRequest request)
    {
        var verdict = QuestEndRules.CheckRequest(request.Code, request.OptionalReward);
        if (verdict != ResultCode.Success) { client.SendResult(605, (ushort)verdict); return; }
        try
        {
            if (_options is null)
            {
                await _characters.GetQuestsAsync(client.ConnectionInfo.CharacterName);
                client.SendResult(605, (ushort)ResultCode.NotActable);
                return;
            }
            await RefreshAsync(client);
            verdict = await _gate.RunAsync(client.ConnectionInfo.CharacterName, async () =>
            {
                var info = client.ConnectionInfo;
                int npcId;
                lock (info.NpcVisibilityLock)
                {
                    if (info.NpcQuestCode != request.Code || !info.SpawnedNpcIdsByHandle.TryGetValue(info.NpcDialogHandle, out var npc)) return ResultCode.NotActable;
                    npcId = (int)npc;
                }
                if (Link(npcId, request.Code)?.FlagEnd != "1" || !_resources.TryGetValue(request.Code, out var resource)) return ResultCode.NotActable;
                await using var db = new TelecasterContext(_options);
                var character = await db.Characters.Include(c => c.Items).SingleOrDefaultAsync(c => c.CharacterName == info.CharacterName);
                if (character is null) return ResultCode.NotActable;
                var quest = await db.CharacterQuests.SingleOrDefaultAsync(q => q.CharacterId == character.Id && q.Code == request.Code);
                if (quest is null || quest.Progress == 100) return ResultCode.NotActable;
                QuestRules.ApplyItemCounts(resource, quest, Counts(character.Items));
                QuestRules.ApplyPlayerState(resource, quest, info, character.Items, JobDepth(info));
                if (!QuestRules.FinishableNow(resource, quest)) return ResultCode.NotActable;
                var optional = Rewards(resource);
                if (request.OptionalReward >= 0 && (optional[request.OptionalReward].Id <= 0 || optional[request.OptionalReward].Count <= 0)) return ResultCode.NotActable;
                var erased = new List<(uint Handle, long Count)>();
                foreach (var requirement in Requirements(resource))
                {
                    var stacks = character.Items.Where(i => i.ItemResourceId == requirement.Key && i.WearInfo == ItemWearType.None
                        && i.EquippedBySummonId == null && i.StorageId == null && i.AuctionId == null && i.Amount > 0).OrderBy(i => i.Idx).ToArray();
                    if (stacks.Sum(i => i.Amount) < requirement.Value) return ResultCode.NotActable;
                    var remaining = requirement.Value;
                    foreach (var item in stacks)
                    {
                        var consumed = Math.Min(item.Amount, remaining);
                        if (consumed == 0) break;
                        item.Amount -= consumed; remaining -= consumed;
                        erased.Add((unchecked((uint)item.Id), consumed));
                        if (item.Amount == 0) db.Items.Remove(item);
                    }
                }
                var added = new List<ItemEntity>();
                // Bag indices are 1-based: the client treats 0 as unset and pushes that item to the end.
                var nextIndex = Math.Max(InventoryArrange.FirstIndex,
                    character.Items.Select(i => i.Idx).DefaultIfEmpty(0).Max() + 1);
                void AddReward(int id, int level, int count)
                {
                    if (id <= 0 || count <= 0) return;
                    var item = new ItemEntity { CharacterId = character.Id, ItemResourceId = id, Amount = count,
                        Level = (uint)Math.Max(1, level), WearInfo = ItemWearType.None, GenerateBySource = ItemGenerateSource.Quest,
                        SocketItemIds = new long[4], Idx = nextIndex++ };
                    db.Items.Add(item); added.Add(item);
                }
                AddReward(resource.DefaultRewardId, resource.DefaultRewardLevel, resource.DefaultRewardQuantity);
                if (request.OptionalReward >= 0) { var r = optional[request.OptionalReward]; AddReward(r.Id, r.Level, r.Count); }
                var factor = resource.Type == 901 ? Enumerable.Range(0, 3).Sum(i => (decimal)QuestRules.Values(resource)[i * 4 + 3] * quest.Value[i * 2 + 1] / 100) : 1;
                var exp = checked((long)(Math.Max(0, resource.Exp) * factor));
                var jp = checked((long)(Math.Max(0, resource.Jp) * factor));
                var gold = checked((long)(Math.Max(0, resource.Gold) * factor));
                // StructPlayer::EndQuest refuses a hand-in whose gold would pass the carried ceiling and says
                // so on the quest line; nothing is consumed, since nothing is saved. It judges no weight.
                if (!GoldRules.Fits(info.CharacterGold, gold, GoldRules.MaxCarried))
                {
                    Chat(client, $"END|TOO_MUCH_MONEY|{request.Code}");
                    return ResultCode.TooMuchMoney;
                }
                // StructPlayer::AddFavor: +favor on favor_group_id, -favor on hate_group_id (999 = this NPC).
                await AddFavorAsync(db, character.Id, QuestRules.FavorId(resource.FavorGroupId, npcId), resource.Favor);
                await AddFavorAsync(db, character.Id, QuestRules.FavorId(resource.HateGroupId, npcId), -resource.Favor);
                character.Exp = checked(info.CharacterExp + exp);
                character.Jp = checked(info.CharacterJp + jp);
                character.Gold = checked(info.CharacterGold + gold);
                character.HuntaholicPoint = checked(character.HuntaholicPoint + resource.HolicPoint);
                var completion = await db.CharacterQuestCompletions.SingleOrDefaultAsync(q => q.CharacterId == character.Id && q.Code == request.Code);
                if (completion is null) db.CharacterQuestCompletions.Add(new CharacterQuestCompletionEntity { CharacterId = character.Id, Code = request.Code, CompletedAt = _time.GetUtcNow().UtcDateTime });
                else completion.CompletedAt = _time.GetUtcNow().UtcDateTime;
                db.CharacterQuests.Remove(quest);
                // The implicit transaction commits consumption, rewards, history and removal together.
                await db.SaveChangesAsync();
                info.CharacterExp += exp; info.CharacterJp += jp; info.AddGold(gold);
                try
                {
                    _leveling?.ApplyExperience(client);
                    if (erased.Count > 0) client.Connection.Send(GameCharacterPackets.BuildEraseItem(erased));
                    if (added.Count > 0) foreach (var packet in GameCharacterPackets.BuildInventory(added.ToArray())) client.Connection.Send(packet);
                    client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
                    client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
                    client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "huntaholicpoint", character.HuntaholicPoint));
                    Chat(client, $"END|EXP|{request.Code}|{exp}|{jp}|{gold}|{resource.HolicPoint}");
                    foreach (var reward in added) Chat(client, $"END|REWARD|{reward.ItemResourceId}");
                }
                catch (Exception exception)
                {
                    // A broken socket after commit cannot turn a paid quest into a database refusal.
                    _logger.Error(exception, "Quest {code} committed but session notifications failed", request.Code);
                }
                return ResultCode.Success;
            });
        }
        catch (Exception exception) { _logger.Error(exception, "Could not finish quest {code}", request.Code); verdict = ResultCode.DBError; }
        client.SendResult(605, (ushort)verdict);
        if (verdict == ResultCode.Success) { _feed?.Publish(client.ConnectionInfo.CharacterName); await SendQuestListAsync(client); }
    }

    public async Task OnMonsterKilledAsync(GameClient client, int monsterId, float x, float y, float z)
    {
        if (_options is null) return;
        try
        {
            await RefreshAsync(client);
            await _gate.RunAsync(client.ConnectionInfo.CharacterName, async () =>
            {
                await using var db = new TelecasterContext(_options);
                var quests = await db.CharacterQuests.Where(q => q.Character.CharacterName == client.ConnectionInfo.CharacterName).ToArrayAsync();
                var changed = new List<CharacterQuestEntity>(); var hunt = new List<int>();
                foreach (var quest in quests)
                {
                    if (quest.Progress == 100 || !_resources.TryGetValue(quest.Code, out var r)) continue;
                    if (QuestRules.ApplyKill(r, quest, monsterId, MatchesMonster)) changed.Add(quest);
                    if (r.Type is 106 or 107 or 109 && !QuestRules.FinishableNow(r, quest) && r.DropGroupId != 0
                        && (r.Type != 106 || QuestRules.Values(r).Skip(6).Any(key => key != 0 && MatchesMonster(key, monsterId)))) hunt.Add(r.DropGroupId);
                }
                await db.SaveChangesAsync();
                foreach (var quest in changed) SendStatus(client, quest);
                foreach (var group in hunt)
                    if (_drops is not null && _ground is not null && DropRoll.TryResolveGroup(group, _drops.Groups, Random.Shared, out var item))
                        _ground.DropQuestItem(client, item, x, y, z);
            });
        }
        catch (Exception exception) { _logger.Error(exception, "Could not count monster {monsterId} for {clientTag}", monsterId, client.ClientTag); }
    }

    public async Task RefreshAsync(GameClient client)
    {
        if (_options is null) return;
        try
        {
            await _gate.RunAsync(client.ConnectionInfo.CharacterName, async () =>
            {
                await using var db = new TelecasterContext(_options);
                var quests = await db.CharacterQuests.Where(q => q.Character.CharacterName == client.ConnectionInfo.CharacterName).ToArrayAsync();
                if (quests.Length == 0) { _lastTick.TryRemove(client.ConnectionInfo.CharacterHandle, out _); return; }
                var characterId = quests[0].CharacterId;
                var items = await db.Items.AsNoTracking().Where(i => i.CharacterId == characterId).ToArrayAsync();
                var counts = Counts(items); var now = _time.GetUtcNow().UtcDateTime;
                var elapsed = _lastTick.TryGetValue(client.ConnectionInfo.CharacterHandle, out var last) ? Math.Max(0, (now - last).TotalSeconds) : 0;
                _lastTick[client.ConnectionInfo.CharacterHandle] = now;
                var changed = new List<CharacterQuestEntity>();
                foreach (var quest in quests)
                {
                    if (!_resources.TryGetValue(quest.Code, out var r) || quest.Progress == 100) continue;
                    var update = false;
                    if (Timed(r) && quest.Progress != QuestRules.Finishable)
                    {
                        if (r.TimeLimitType?.Trim() == "1") quest.RemainingSeconds = Math.Max(0, quest.RemainingSeconds - elapsed);
                        else if (quest.ExpiresAt is not null) quest.RemainingSeconds = Math.Max(0, (quest.ExpiresAt.Value - now).TotalSeconds);
                        if (quest.RemainingSeconds <= 0) { quest.Progress = 100; update = true; }
                        quest.TimeLimit = unchecked((int)(ServerClock.Now + (uint)Math.Ceiling(quest.RemainingSeconds * 100)));
                    }
                    if (quest.Progress != 100)
                    {
                        update |= QuestRules.ApplyItemCounts(r, quest, counts);
                        update |= QuestRules.ApplyPlayerState(r, quest, client.ConnectionInfo, items, JobDepth(client.ConnectionInfo));
                    }
                    if (update) changed.Add(quest);
                }
                await db.SaveChangesAsync();
                foreach (var quest in changed) SendStatus(client, quest);
                // Only a running countdown needs the one-second refresh. Skills (201), job level (501) and
                // chaos (601) change on events that already refresh (learning, job-level-up, the inventory
                // feed), and the quest dialog and the hand-in refresh before they judge; polling them read the
                // database every second for nothing.
                if (!quests.Any(q => q.Progress == QuestRules.InProgress && _resources.TryGetValue(q.Code, out var r)
                    && Timed(r)))
                    _lastTick.TryRemove(client.ConnectionInfo.CharacterHandle, out _);
            });
        }
        catch (Exception exception) { _logger.Error(exception, "Could not refresh quests for {clientTag}", client.ClientTag); }
    }

    private static Dictionary<int, long> Counts(IEnumerable<ItemEntity> items) => items
        .Where(i => i.Amount > 0 && i.DeletedOn == null && i.StorageId == null && i.AuctionId == null && i.WearInfo == ItemWearType.None && i.EquippedBySummonId == null)
        .GroupBy(i => (int)i.ItemResourceId).ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));
    private static async Task<int> FavorAsync(TelecasterContext db, long characterId, QuestResourceEntity resource, int npcId)
    {
        if (resource.LimitFavor <= 0) return 0;
        var id = QuestRules.FavorId(resource.LimitFavorGroupId, npcId);
        return await db.CharacterFavors.AsNoTracking().Where(f => f.CharacterId == characterId && f.FavorId == id)
            .Select(f => f.Value).FirstOrDefaultAsync();
    }
    /// <summary>An unbounded id → value counter; a group id of 0 names nothing.</summary>
    private static async Task AddFavorAsync(TelecasterContext db, long characterId, int favorId, int amount)
    {
        if (favorId == 0 || amount == 0) return;
        var row = db.CharacterFavors.Local.FirstOrDefault(f => f.CharacterId == characterId && f.FavorId == favorId)
                  ?? await db.CharacterFavors.SingleOrDefaultAsync(f => f.CharacterId == characterId && f.FavorId == favorId);
        if (row is null) db.CharacterFavors.Add(new CharacterFavorEntity { CharacterId = characterId, FavorId = favorId, Value = amount });
        else row.Value = (int)Math.Clamp((long)row.Value + amount, int.MinValue, int.MaxValue);
    }
    private static async Task<Dictionary<int, DateTime>> Completions(TelecasterContext db, long id) => await db.CharacterQuestCompletions.AsNoTracking().Where(q => q.CharacterId == id).ToDictionaryAsync(q => q.Code, q => q.CompletedAt);
    private static async Task<Dictionary<int, DateTime>> Acceptances(TelecasterContext db, long id) =>
        await db.CharacterQuests.IgnoreQueryFilters().AsNoTracking().Where(q => q.CharacterId == id)
            .GroupBy(q => q.Code).Select(g => new { Code = g.Key, Last = g.Max(q => q.CreatedOn) })
            .ToDictionaryAsync(q => q.Code, q => q.Last);
    private static IEnumerable<KeyValuePair<int, long>> Requirements(QuestResourceEntity r)
    {
        if (r.Type is not (103 or 106 or 107 or 109)) return Array.Empty<KeyValuePair<int, long>>();
        var v = QuestRules.Values(r);
        return Enumerable.Range(0, r.Type == 103 ? 6 : 3).Where(i => v[i * 2] > 0 && v[i * 2 + 1] > 0)
            .GroupBy(i => v[i * 2]).Select(g => new KeyValuePair<int, long>(g.Key, g.Sum(i => (long)v[i * 2 + 1])));
    }
    private static (int Id, int Level, int Count)[] Rewards(QuestResourceEntity r) => new[]
    {
        (r.OptionalRewardId1, r.OptionalRewardLevel1, r.OptionalRewardQuantity1), (r.OptionalRewardId2, r.OptionalRewardLevel2, r.OptionalRewardQuantity2),
        (r.OptionalRewardId3, r.OptionalRewardLevel3, r.OptionalRewardQuantity3), (r.OptionalRewardId4, r.OptionalRewardLevel4, r.OptionalRewardQuantity4),
        (r.OptionalRewardId5, r.OptionalRewardLevel5, r.OptionalRewardQuantity5), (r.OptionalRewardId6, r.OptionalRewardLevel6, r.OptionalRewardQuantity6)
    };
    private static bool AtNpc(GameClient client, int npcId, int code)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock) return info.NpcQuestCode == code && info.SpawnedNpcIdsByHandle.TryGetValue(info.NpcDialogHandle, out var id) && id == npcId;
    }
    private bool MatchesMonster(int key, int id) => key == id || key < 0
        && _pools.TryGetValue(key, out var members) && members.Any(p => p.QuestTargetId == id);

    private bool TryRandomTargets(QuestResourceEntity resource, out int[] targets, bool randomize = true)
    {
        targets = new int[6];
        var values = QuestRules.Values(resource);
        var used = new HashSet<int>();
        for (var i = 0; i < 3; i++)
        {
            var group = values[i * 4];
            if (group == 0) break;
            var min = values[i * 4 + 1]; var max = values[i * 4 + 2];
            if (min <= 0 || max < min || values[i * 4 + 3] < 0 || !_pools.TryGetValue(group, out var members)) return false;
            var candidates = members.Where(p => Math.Abs((long)p.TargetLevel - resource.LimitLevel) <= 4 && !used.Contains(p.QuestTargetId)).ToArray();
            if (candidates.Length == 0) return false;
            var chosen = candidates[randomize ? Random.Shared.Next(candidates.Length) : 0].QuestTargetId;
            used.Add(chosen);
            targets[i * 2] = chosen;
            targets[i * 2 + 1] = randomize ? (int)Random.Shared.NextInt64(min, (long)max + 1) : min;
        }
        return targets[0] > 0;
    }
    private static void SendStatus(GameClient client, CharacterQuestEntity quest) => client.Connection.Send(GameQuestPackets.BuildQuestStatus(GameQuestPackets.ToQuestListEntry(quest)));
    private static void Chat(GameClient client, string message) => client.Connection.Send(GameChatPackets.BuildChat("@QUEST", 120, message));
    private void InventoryChanged(string name)
    {
        var client = _players?.Registry.Clients.FirstOrDefault(c => c.ConnectionInfo.CharacterName == name);
        if (client is not null) _ = RefreshAsync(client);
    }
    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var clients = _players.Registry.Clients;
                var handles = clients.Select(c => c.ConnectionInfo.CharacterHandle).ToHashSet();
                foreach (var handle in _lastTick.Keys.Where(h => !handles.Contains(h))) _lastTick.TryRemove(handle, out _);
                foreach (var client in clients.Where(c => _lastTick.ContainsKey(c.ConnectionInfo.CharacterHandle))) await RefreshAsync(client);
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        if (_feed is not null) _feed.Changed -= InventoryChanged;
        _stop.Cancel();
    }

    public async Task LeaveWorldAsync(GameClient client)
    {
        await RefreshAsync(client);
        _lastTick.TryRemove(client.ConnectionInfo.CharacterHandle, out _);
    }
}
