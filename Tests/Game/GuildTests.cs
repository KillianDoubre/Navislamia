using System.Buffers.Binary;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Guilds;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Progression;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class GuildTests
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Harness
    {
        public readonly DbContextOptions<TelecasterContext> Options;
        public readonly Clock Time = new();
        public readonly IPlayerVisibilityService Players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        public readonly GuildRuntime Runtime;
        public readonly GuildCombatEvents Events = new();
        public readonly CharacterGate Gate = new();
        public readonly DungeonCatalog Catalog = new(Microsoft.Extensions.Options.Options.Create(new DungeonOptions()));
        public readonly MonsterWorldState World;
        public readonly DungeonRooms Rooms;
        public readonly PartyService Parties;
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly Dictionary<uint, StorageTestHarness.FrameConnection> Frames = new();
        public GuildService Guilds;
        public Harness(DbContextOptions<TelecasterContext> options = null)
        {
            Options = options ?? new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
            Runtime = new GuildRuntime(Time);
            var monsters = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => monsters.GetByIds(A<IReadOnlyCollection<int>>._)).ReturnsLazily(call =>
                call.GetArgument<IReadOnlyCollection<int>>(0)!.Select(id => new MonsterResourceEntity { Id = id, Level = 50, Hp = 100, Race = 1 }).ToArray());
            World = new MonsterWorldState(monsters, Microsoft.Extensions.Options.Options.Create(new MonsterSpawnOptions()));
            Rooms = new DungeonRooms(World);
            Parties = new PartyService(Players, Stats, A.Fake<IBannedWordsRepository>(), Runtime);
            Guilds = Service();
        }
        public GuildService Service() => new(Options, Gate, Players, Runtime, Events, Catalog, Rooms,
            Microsoft.Extensions.Options.Options.Create(new DungeonOptions()), World, Stats, time: Time, parties: Parties);
        public TelecasterContext Db() => new(Options);
        public async Task<GameClient> Player(uint id, long? guild = null, byte rank = 0, long gold = 500000)
        {
            var frame = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var player = StorageTestHarness.NewGameClient(frame, playerVisibilityService: Players, partyService: Parties);
            var info = StorageTestHarness.Session(player);
            info.CharacterHandle = id; info.CharacterName = "P" + id; info.CharacterLevel = 50; info.CharacterHp = 100;
            info.CharacterMaxHp = 100; info.CharacterGold = gold; info.CharacterMp = 100; info.GuildId = guild;
            info.X = 100000; info.Y = 120000;
            Frames[id] = frame; Players.Registry.Register(id, player);
            await using var db = Db();
            var character = new CharacterEntity { Id = id, CharacterName = info.CharacterName, Lv = 50, Gold = gold, GuildId = guild, GuildPermission = rank };
            foreach (var property in typeof(CharacterEntity).GetProperties().Where(p => p.CanWrite))
            {
                if (property.PropertyType == typeof(string) && property.GetValue(character) is null) property.SetValue(character, "");
                if (property.PropertyType.IsArray && property.GetValue(character) is null) property.SetValue(character, Array.CreateInstance(property.PropertyType.GetElementType()!, 0));
            }
            db.Characters.Add(character); await db.SaveChangesAsync();
            return player;
        }
        public async Task<long> SeedGuild(GameClient leader, string name)
        {
            await using var db = Db();
            var guild = new GuildEntity { Name = name, NormalizedName = GuildRules.Normalize(name), LeaderId = Info(leader).CharacterHandle,
                PermissionSets = new GuildPermissions[6], PermissionNames = Enumerable.Repeat("", 6).ToArray(),
                Notice = "", Url = "", Icon = "", Banner = "", AdvertiseComment = "", AdvertiseEndTime = Time.Now.UtcDateTime };
            db.Guilds.Add(guild); await db.SaveChangesAsync();
            var member = await db.Characters.FindAsync((long)Info(leader).CharacterHandle);
            member!.GuildId = guild.Id; member.GuildPermission = 7;
            await db.SaveChangesAsync(); await Guilds.OnWorldEntryAsync(leader); return guild.Id;
        }
        public async Task<bool> Create(GameClient leader, string name, bool alliance = false)
        {
            Info(leader).SpawnedNpcIdsByHandle[77] = 1012;
            Guilds.Contact(leader, 77, 1012);
            Guilds.Select(leader, 77, alliance ? "show_alliance_create()" : "show_guild_create()");
            if (!await Guilds.ExecuteCommandAsync(leader, (alliance ? "/gacreate " : "/gcreate ") + name)) return false;
            var trigger = Info(leader).NpcDialogTriggers.Single();
            return await Guilds.CreateConfirmedAsync(leader, 77, trigger, name, alliance);
        }
        public string[] Invitation(GameClient target, bool alliance = false) => Frames[Info(target).CharacterHandle].Sent
            .Where(p => p.Length > 31 && p[30] == (byte)(alliance ? ChatType.AllianceSystem : ChatType.GuildSystem))
            .Select(p => Encoding.ASCII.GetString(p.AsSpan(31)).TrimEnd('\0')).Last(t => t.StartsWith("INVITE|")).Split('|');
        public async Task Join(GameClient leader, GameClient member)
        {
            (await Guilds.ExecuteCommandAsync(leader, "/ginvite " + Info(member).CharacterName)).Should().BeTrue();
            var invite = Invitation(member);
            (await Guilds.ExecuteCommandAsync(member, $"/gjoin {invite[3]} {invite[4]}")).Should().BeTrue();
        }
        public async Task JoinAlliance(GameClient leader, GameClient allied)
        {
            await using var db = Db();
            var target = await db.Guilds.SingleAsync(g => g.Id == Info(allied).GuildId);
            (await Guilds.ExecuteCommandAsync(leader, "/gainvite " + target.Name)).Should().BeTrue();
            var invite = Invitation(allied, true);
            (await Guilds.ExecuteCommandAsync(allied, $"/gajoin {invite[3]} {invite[4]}")).Should().BeTrue();
        }
        public DungeonService Dungeons()
        {
            var warp = A.Fake<IWarpService>();
            A.CallTo(() => warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).Invokes(call =>
            { var info = Info(call.GetArgument<GameClient>(0)!); info.X = call.GetArgument<float>(1); info.Y = call.GetArgument<float>(2); info.Layer = call.GetArgument<byte>(3); });
            return new DungeonService(Catalog, Rooms, warp, Parties, new DungeonGuildRepository(Options), A.Fake<ICharacterService>(),
                Microsoft.Extensions.Options.Options.Create(new DungeonOptions()), World, Time, Guilds);
        }
    }
    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);
    private static MonsterInstance Monster(int code, int dungeon, byte layer, DungeonCatalog catalog, long id = 999) => new(id, code,
        catalog.Dungeons[dungeon].X, catalog.Dungeons[dungeon].Y, 0, 50, 100, 1, 0, false, 100, 100, 100, 1, 1, 0, 0, Layer: layer);

    [Test]
    public async Task Creation_uses_official_window_confirmation_fee_and_survives_service_reload()
    {
        var h = new Harness(); var leader = await h.Player(1);
        (await h.Guilds.ExecuteCommandAsync(leader, "/gcreate NoNpc")).Should().BeFalse();
        (await h.Create(leader, "First Guild")).Should().BeTrue();
        h.Frames[1].Sent.Should().Contain(p => BitConverter.ToUInt16(p, 4) == 650 && p.Length == 7);
        Info(leader).CharacterGold.Should().Be(400000); Info(leader).GuildPermission.Should().Be(7);
        await using var db = h.Db();
        var guild = await db.Guilds.SingleAsync(); guild.LeaderId.Should().Be(1); guild.AllianceId.Should().BeNull();
        (await db.Characters.SingleAsync()).Gold.Should().Be(400000);
        h.Guilds = h.Service(); Info(leader).GuildId = null;
        await h.Guilds.OnWorldEntryAsync(leader); Info(leader).GuildId.Should().Be(guild.Id);
    }

    [Test]
    public async Task Creation_refuses_duplicate_case_missing_gold_and_stale_npc_context_without_charging()
    {
        var h = new Harness(); var leader = await h.Player(1); var poor = await h.Player(2, gold: 1); var duplicate = await h.Player(3);
        (await h.Create(leader, "Unique")).Should().BeTrue();
        (await h.Create(duplicate, "unique")).Should().BeFalse(); Info(duplicate).CharacterGold.Should().Be(500000);
        (await h.Create(poor, "Poor")).Should().BeFalse(); Info(poor).CharacterGold.Should().Be(1);
        Info(duplicate).SpawnedNpcIdsByHandle[77] = 1012; h.Guilds.Contact(duplicate, 77, 1012);
        h.Guilds.Select(duplicate, 77, "show_guild_create()"); await h.Guilds.ExecuteCommandAsync(duplicate, "/gcreate Stale");
        var trigger = Info(duplicate).NpcDialogTriggers.Single(); Info(duplicate).ClearNpcDialog();
        (await h.Guilds.CreateConfirmedAsync(duplicate, 77, trigger, "Stale", false)).Should().BeFalse();
        await using var db = h.Db(); (await db.Guilds.CountAsync()).Should().Be(1);
    }

    [TestCase("bad|name"), TestCase("bad'name"), TestCase("bad\\name"), TestCase("bad\nname"), TestCase(" name")]
    public void Delimiters_and_code_are_never_guild_names(string name) => GuildRules.ValidName(name).Should().BeFalse();

    [Test]
    public async Task Invitations_are_bound_to_recipient_expire_and_are_consumed_once()
    {
        var h = new Harness(); var leader = await h.Player(1); await h.SeedGuild(leader, "One");
        var target = await h.Player(2); var outsider = await h.Player(3);
        await h.Guilds.ExecuteCommandAsync(leader, "/ginvite P2"); var invite = h.Invitation(target);
        (await h.Guilds.ExecuteCommandAsync(outsider, $"/gjoin {invite[3]} {invite[4]}")).Should().BeFalse();
        h.Time.Now = h.Time.Now.AddMinutes(3);
        (await h.Guilds.ExecuteCommandAsync(target, $"/gjoin {invite[3]} {invite[4]}")).Should().BeFalse();
        await h.Join(leader, target);
        (await h.Guilds.ExecuteCommandAsync(target, $"/gjoin {invite[3]} {invite[4]}")).Should().BeFalse();
        Info(target).GuildPermission.Should().Be(1);
    }

    [Test]
    public async Task Ranks_protect_the_leader_and_higher_members_then_transfer_authority()
    {
        var h = new Harness(); var leader = await h.Player(1); await h.SeedGuild(leader, "One"); var member = await h.Player(2); await h.Join(leader, member);
        (await h.Guilds.ExecuteCommandAsync(member, "/ginvite P3")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gpermissionset 1 14")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(member, "/gkick P1")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(member, "/gpermission P2 6")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gpermission P2 7")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gpromote P2")).Should().BeTrue();
        Info(leader).GuildPermission.Should().Be(1); Info(member).GuildPermission.Should().Be(7);
        (await h.Guilds.ExecuteCommandAsync(leader, "/gdestroy")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(member, "/gleave")).Should().BeFalse();
    }

    [Test]
    public async Task Kicking_sets_previous_guild_and_seven_day_cooldown()
    {
        var h = new Harness(); var leader = await h.Player(1); var guild = await h.SeedGuild(leader, "One"); var member = await h.Player(2); await h.Join(leader, member);
        (await h.Guilds.ExecuteCommandAsync(leader, "/gkick P2")).Should().BeTrue(); Info(member).GuildId.Should().BeNull();
        await using var db = h.Db(); var stored = await db.Characters.FindAsync(2L);
        stored!.PreviousGuildId.Should().Be(guild); stored.GuildBlockTime.Should().Be(h.Time.Now.UtcDateTime.AddDays(7));
        (await h.Guilds.ExecuteCommandAsync(leader, "/ginvite P2")).Should().BeFalse();
        h.Time.Now = h.Time.Now.AddDays(7); await h.Join(leader, member);
    }

    [Test]
    public async Task Guild_capacity_is_checked_before_accepting_a_pending_invitation()
    {
        var h = new Harness(); var leader = await h.Player(1); var guild = await h.SeedGuild(leader, "One"); var target = await h.Player(2);
        await h.Guilds.ExecuteCommandAsync(leader, "/ginvite P2"); var invite = h.Invitation(target);
        await using var db = h.Db();
        for (var i = 10; i < 209; i++) db.Characters.Add(new CharacterEntity { Id = i, CharacterName = "Offline" + i, GuildId = guild, GuildPermission = 1 });
        await db.SaveChangesAsync();
        (await h.Guilds.ExecuteCommandAsync(target, $"/gjoin {invite[3]} {invite[4]}")).Should().BeFalse();
    }

    [Test]
    public async Task Alliance_supports_three_guilds_shared_identity_and_leave_cooldown()
    {
        var h = new Harness(); var one = await h.Player(1); var two = await h.Player(2); var three = await h.Player(3); var four = await h.Player(4);
        var leading = await h.SeedGuild(one, "One"); await h.SeedGuild(two, "Two"); await h.SeedGuild(three, "Three"); await h.SeedGuild(four, "Four");
        (await h.Create(one, "Allies", true)).Should().BeTrue(); Info(one).CharacterGold.Should().Be(500000);
        await h.JoinAlliance(one, two); await h.JoinAlliance(one, three);
        h.Guilds.EffectiveGuild(Info(two).GuildId).Should().Be(leading); Info(two).AllianceId.Should().Be(Info(one).AllianceId);
        (await h.Guilds.ExecuteCommandAsync(one, "/gainvite Four")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(one, "/galeave")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(one, "/gadestroy")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(two, "/galeave")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(one, "/gainvite Two")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(one, "/gakick Three")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(one, "/gadestroy")).Should().BeTrue();
    }

    [Test]
    public async Task Allied_owner_access_and_ownership_block_alliance_departure()
    {
        var h = new Harness(); var one = await h.Player(1); var two = await h.Player(2);
        var owner = await h.SeedGuild(one, "One"); var ally = await h.SeedGuild(two, "Two");
        await h.Create(one, "Allies", true); await h.JoinAlliance(one, two);
        await using var db = h.Db(); db.Dungeons.Add(new DungeonEntity { Id = 130300, OwnerGuildId = owner, TaxRate = 1 });
        (await db.Guilds.FindAsync(owner))!.DungeonId = 130300; await db.SaveChangesAsync();
        (await new DungeonGuildRepository(h.Options).OwnedDungeonAsync(ally)).Should().Be(130300);
        (await h.Guilds.ExecuteCommandAsync(two, "/galeave")).Should().BeFalse();
    }

    [Test]
    public async Task Taxes_conserve_gold_and_chaos_persist_and_withdraw_once()
    {
        var h = new Harness(); var owner = await h.Player(1); var guild = await h.SeedGuild(owner, "One"); var killer = await h.Player(2);
        await using (var db = h.Db()) { db.Dungeons.Add(new DungeonEntity { Id = 130000, OwnerGuildId = guild, TaxRate = 10 }); await db.SaveChangesAsync(); }
        var reward = new MonsterKillReward(1000, 100, 101, 109);
        var received = await h.Guilds.OnMonsterKilledAsync(killer, Monster(12, 130000, 0, h.Catalog), 999, reward);
        received.Gold.Should().Be(91); received.Chaos.Should().Be(99); received.Exp.Should().Be(1000);
        await using (var db = h.Db()) { var treasury = await db.Guilds.SingleAsync(); treasury.Gold.Should().Be(10); treasury.Chaos.Should().Be(10); }
        (await h.Guilds.ExecuteCommandAsync(owner, "/gtax 0")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(owner, "/gtax 11")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(owner, "/gtax 5")).Should().BeTrue();
        var withdrawals = await Task.WhenAll(h.Guilds.ExecuteCommandAsync(owner, "/gwithdraw gold"), h.Guilds.ExecuteCommandAsync(owner, "/gwithdraw gold"));
        withdrawals.Count(x => x).Should().Be(1); Info(owner).CharacterGold.Should().Be(500010);
        (await h.Guilds.ExecuteCommandAsync(killer, "/gwithdraw gold")).Should().BeFalse();
        await using (var db = h.Db()) { (await db.Guilds.SingleAsync()).Gold.Should().Be(0); (await db.Characters.FindAsync(1L))!.Gold.Should().Be(500010); }
    }

    [Test]
    public async Task Raid_requires_registration_and_attack_team_and_both_bosses_before_recording()
    {
        var h = new Harness(); var leader = await h.Player(1); var guild = await h.SeedGuild(leader, "One");
        (await h.Guilds.ExecuteCommandAsync(leader, "/rpcreate Raid")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/graid 130000")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(leader, "/rpcreate Raid")).Should().BeTrue();
        var dungeons = h.Dungeons(); var action = PropScript.Parse("begin_dungeon_raid(130000)");
        (await dungeons.ExecuteAsync(leader, action)).Should().Be(ResultCode.Success);
        var room = h.Rooms.Find(new DungeonRoomKey(DungeonRoomKind.Raid, 130000, guild))!;
        room.Monsters.Should().HaveCount(2); Info(leader).Layer.Should().BeGreaterThan(1);
        h.Time.Now = h.Time.Now.AddSeconds(20);
        var boss1 = room.Monsters.Select(id => { h.World.TryGetInstance(id, out var monster); return monster; }).First(m => m.MonsterId == h.Catalog.Dungeons[130000].Boss1);
        await h.Guilds.OnMonsterKilledAsync(leader, boss1, boss1.InstanceId, default);
        await using (var db = h.Db()) { (await db.GuildRaids.SingleAsync()).BestTime.Should().Be(0); }
        h.Time.Now = h.Time.Now.AddSeconds(10);
        var boss2 = room.Monsters.Select(id => { h.World.TryGetInstance(id, out var monster); return monster; }).First(m => m.MonsterId == h.Catalog.Dungeons[130000].Boss2);
        await h.Guilds.OnMonsterKilledAsync(leader, boss2, boss2.InstanceId, default);
        await using (var db = h.Db()) { var raid = await db.GuildRaids.SingleAsync(); raid.BestTime.Should().Be(3000); raid.StartedAt.Should().BeNull(); }
        room.Ended.Should().BeTrue(); await dungeons.SweepAsync(); Info(leader).Layer.Should().Be(0);
    }

    [Test]
    public async Task Weekly_ranking_selects_fastest_after_restart_and_wraps_only_once()
    {
        var h = new Harness(); var one = await h.Player(1); var two = await h.Player(2); var a = await h.SeedGuild(one, "One"); var b = await h.SeedGuild(two, "Two");
        await h.Guilds.ExecuteCommandAsync(one, "/graid 130000"); await h.Guilds.ExecuteCommandAsync(two, "/graid 130000");
        await using (var db = h.Db()) { var raids = await db.GuildRaids.ToArrayAsync(); raids.Single(r => r.GuildId == a).BestTime = 3000; raids.Single(r => r.GuildId == b).BestTime = 2000; await db.SaveChangesAsync(); }
        h.Time.Now = DateTimeOffset.Parse("2026-10-02T01:00:00Z"); h.Guilds = h.Service(); await h.Guilds.TickAsync(); await h.Guilds.TickAsync();
        await using (var db = h.Db())
        { var state = await db.Dungeons.SingleAsync(); state.RaidGuildId.Should().Be(b); state.BestRaidTime.Should().Be(2000); (await db.GuildRaids.CountAsync(r => r.WrappedUp)).Should().Be(2); (await db.Guilds.FindAsync(a))!.DungeonId.Should().BeNull(); }
    }

    private static async Task<(Harness H, GameClient Defender, GameClient Attacker, DungeonRoom Room)> Siege(bool neutral = false)
    {
        var h = new Harness(); var defender = await h.Player(1); var attacker = await h.Player(2);
        var d = await h.SeedGuild(defender, "Defender"); var a = await h.SeedGuild(attacker, "Attacker");
        await using (var db = h.Db())
        {
            db.Dungeons.Add(new DungeonEntity { Id = 130000, OwnerGuildId = neutral ? null : d, RaidGuildId = a, TaxRate = 1 });
            (await db.Guilds.FindAsync(a))!.DungeonId = 130000; if (!neutral) (await db.Guilds.FindAsync(d))!.DungeonId = 130000; await db.SaveChangesAsync();
        }
        h.Time.Now = DateTimeOffset.Parse("2026-10-03T18:30:00Z");
        if (!neutral) (await h.Guilds.ExecuteCommandAsync(defender, "/rpcreate Defence")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(attacker, "/rpcreate Attack")).Should().BeTrue();
        var dungeons = h.Dungeons(); var action = PropScript.Parse("warp_to_siege_dungeon(130000)");
        (await dungeons.ExecuteAsync(attacker, action)).Should().Be(ResultCode.Success);
        if (!neutral) (await dungeons.ExecuteAsync(defender, action)).Should().Be(ResultCode.Success);
        return (h, defender, attacker, h.Rooms.Find(new DungeonRoomKey(DungeonRoomKind.Siege, 130000, 0))!);
    }
    private static MonsterInstance Objective(Harness h, DungeonRoom room, bool core) => room.Monsters
        .Select(id => { h.World.TryGetInstance(id, out var monster); return monster; })
        .Last(m => m.MonsterId == (core ? h.Catalog.Dungeons[130000].Core : h.Catalog.Dungeons[130000].Connector));

    [Test]
    public async Task Siege_core_swaps_ownership_connector_finishes_and_titles_credit_offline_participants_once()
    {
        var (h, defender, attacker, room) = await Siege();
        room.Monsters.Should().HaveCount(2); h.Runtime.WarEnemy(Info(defender), Info(attacker)).Should().BeTrue();
        var core = Objective(h, room, true); var connector = Objective(h, room, false);
        h.Runtime.CanDamage(defender, core.InstanceId).Should().BeFalse(); h.Runtime.CanDamage(attacker, connector.InstanceId).Should().BeFalse();
        await h.Guilds.OnMonsterKilledAsync(attacker, core, core.InstanceId, default);
        await using (var db = h.Db()) { (await db.Dungeons.SingleAsync()).OwnerGuildId.Should().Be(Info(attacker).GuildId); (await db.GuildSieges.SingleAsync()).FinishedAt.Should().BeNull(); }
        h.Runtime.CanDamage(attacker, connector.InstanceId).Should().BeTrue();
        h.Players.Registry.Unregister(Info(defender).CharacterHandle, defender);
        await h.Guilds.OnMonsterKilledAsync(attacker, connector, connector.InstanceId, default);
        await h.Guilds.TickAsync(); await h.Guilds.TickAsync();
        await using (var db = h.Db())
        {
            var siege = await db.GuildSieges.SingleAsync(); siege.WinnerId.Should().Be(Info(attacker).GuildId); siege.FinishedAt.Should().NotBeNull();
            (await db.GuildSiegeParticipants.ToArrayAsync()).Should().OnlyContain(p => p.StartCredited && p.EndCredited);
            var lost = await db.CharacterTitleStates.SingleAsync(s => s.CharacterId == 1);
            lost.ConditionCounts[Array.IndexOf(lost.ConditionIds, 5202004)].Should().Be(1);
            var won = await db.CharacterTitleStates.SingleAsync(s => s.CharacterId == 2);
            won.ConditionCounts[Array.IndexOf(won.ConditionIds, 5202001)].Should().Be(1);
        }
        room.Ended.Should().BeTrue(); h.Runtime.WarEnemy(Info(defender), Info(attacker)).Should().BeFalse();
    }

    [Test]
    public async Task Neutral_dungeon_is_awarded_when_core_falls()
    {
        var (h, _, attacker, room) = await Siege(true); var core = Objective(h, room, true);
        await h.Guilds.OnMonsterKilledAsync(attacker, core, core.InstanceId, default);
        await using var db = h.Db(); (await db.Dungeons.SingleAsync()).OwnerGuildId.Should().Be(Info(attacker).GuildId); (await db.GuildSieges.SingleAsync()).FinishedAt.Should().NotBeNull();
    }

    [Test]
    public async Task Siege_clock_settles_defence_and_rejoining_never_farms_start_titles()
    {
        var (h, defender, attacker, room) = await Siege();
        (await h.Guilds.SiegeEnteredAsync(attacker, room)).Should().BeTrue();
        h.Time.Now = DateTimeOffset.Parse("2026-10-03T20:00:01Z"); h.Guilds = h.Service(); await h.Guilds.TickAsync(); await h.Guilds.TickAsync();
        await using var db = h.Db();
        (await db.GuildSieges.SingleAsync()).WinnerId.Should().Be(Info(defender).GuildId);
        var state = await db.CharacterTitleStates.SingleAsync(s => s.CharacterId == 2);
        state.ConditionCounts[Array.IndexOf(state.ConditionIds, 5201001)].Should().Be(1);
        state.ConditionCounts[Array.IndexOf(state.ConditionIds, 5202003)].Should().Be(1);
    }

    [Test]
    public void Alliance_is_friendly_in_pk_but_duel_still_overrides_it()
    {
        var one = new ConnectionInfo { GuildId = 1, AllianceId = 9, PkMode = true }; var two = new ConnectionInfo { GuildId = 2, AllianceId = 9, PkMode = true };
        CombatService.IsPlayerEnemy(one, two, false, true).Should().BeFalse(); CombatService.IsPlayerEnemy(one, two, true, false).Should().BeTrue();
    }

    [Test]
    public async Task Siege_player_kill_has_no_immorality_or_death_penalty()
    {
        var (h, defender, attacker, _) = await Siege(); var levels = A.Fake<ILevelingService>();
        var combat = new CombatService(h.World, A.Fake<IMonsterSpawnService>(), levels, A.Fake<IGroundItemService>(),
            A.Fake<Navislamia.Game.Services.Rates.IRateService>(), h.Stats, A.Fake<IStateCatalog>(), h.Parties,
            players: h.Players, runTicks: false, guilds: h.Runtime);
        combat.ArePlayerEnemies(attacker, defender).Should().BeTrue();
        combat.DamagePlayerByPlayer(attacker, defender, 1000).Should().Be(0);
        Info(attacker).ImmoralPoint.Should().Be(0); Info(attacker).PkCount.Should().Be(0);
        A.CallTo(() => levels.ApplyDeathPenalty(defender)).MustNotHaveHappened();
    }
}
