using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Services.Weight;

namespace Tests.Game;

[TestFixture]
public class MonsterRewardIntegrationTests
{
    private sealed class RewardConnection : Connection
    {
        public RewardConnection() : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)) { }
        public List<byte[]> Sent { get; } = new();
        public GamePackets? FailPacket { get; set; }
        public override void Send(byte[] buffer)
        {
            lock (Sent)
            {
                if (Harness.Id(buffer) == FailPacket) throw new InvalidOperationException("Simulated send failure");
                Sent.Add(buffer);
            }
        }
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly IMonsterResourceRepository Repository = A.Fake<IMonsterResourceRepository>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly ILevelingService Leveling = A.Fake<ILevelingService>();
        public readonly ICarriedWeightService Weights = A.Fake<ICarriedWeightService>();
        public readonly PlayerVisibilityService Players = new(A.Fake<ILogger<PlayerVisibilityService>>());
        public readonly PartyService Parties;
        public readonly GroundItemService Ground;
        public readonly CombatService Combat;
        public readonly MonsterWorldState World;
        public readonly Dictionary<GameClient, RewardConnection> Connections = new();
        public readonly MonsterResourceEntity Resource;
        public Harness(MonsterResourceEntity resource = null, bool secondary = false, RatesOptions rates = null,
            bool alias = false, bool raid = false)
        {
            Resource = resource ?? new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100,
                Exp = 731, Jp = 119, GoldDropPercentage = 100, GoldMin = 101, GoldMax = 101,
                ChaosDropPercentage = 100, ChaosMin = 11, ChaosMax = 11 };
            A.CallTo(() => Repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[] { Resource });
            World = new MonsterWorldState(Repository, Options.Create(new MonsterSpawnOptions
            {
                UseSecondaryRewards = secondary,
                Spawns = { new MonsterSpawnPoint { MonsterId = alias ? 12 : (int)Resource.Id,
                    ResourceId = (int)Resource.Id, X = 1000, Y = 1000, Count = 2, IsDungeonRaidMonster = raid } }
            }));
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
                new StatBlock { MaxChaos = 500, MaxHp = 1000, MaxMp = 100 }, new StatBlock()));
            Parties = new PartyService(Players, Stats, A.Fake<IBannedWordsRepository>());
            var rateService = new RateService(new StaticOptionsMonitor<RatesOptions>(rates ?? new RatesOptions { EventStatePath = "" }));
            Ground = new GroundItemService(A.Fake<IMonsterDropCatalog>(), Characters,
                A.Fake<IItemGroupCatalog>(), rateService, Players, Weights, Parties, clock: () => Now);
            var random = A.Fake<ICombatRandom>(); A.CallTo(() => random.Next(A<int>._)).Returns(0);
            Combat = new CombatService(World, A.Fake<IMonsterSpawnService>(), Leveling, Ground, rateService,
                Stats, A.Fake<IStateCatalog>(), Parties, random: random, players: Players);
        }
        /// <summary>The ground items' clock: a pet collects its master's loot past 30 s (SGameItem::IsPickable).</summary>
        public uint Now = 1_000_000;

        public GameClient Player(uint handle, string name, float x = 1000, byte layer = 0)
        {
            var connection = new RewardConnection();
            var client = StorageTestHarness.NewGameClient(connection);
            Connections[client] = connection;
            var info = StorageTestHarness.Session(client);
            info.CharacterHandle = handle; info.CharacterName = name; info.CharacterLevel = 5;
            info.CharacterHp = 1000; info.CharacterMaxHp = 1000; info.X = x; info.Y = 1000; info.Layer = layer;
            info.DestinationX = x; info.DestinationY = 1000;
            info.SpawnedMonsters[0] = 0x40000000 + handle;
            info.SpawnedMonsters[1] = 0x50000000 + handle;
            foreach (var other in Players.Registry.Clients)
            {
                info.SpawnedPlayers[StorageTestHarness.Session(other).CharacterHandle] = StorageTestHarness.Session(other).CharacterHandle;
                StorageTestHarness.Session(other).SpawnedPlayers[handle] = handle;
            }
            Players.Registry.Register(handle, client);
            return client;
        }
        public void Join(GameClient leader, params GameClient[] members)
        {
            Parties.TryHandleCommand(leader, "/pcreate Rewards");
            foreach (var member in members)
            {
                var wire = Connections[member]; wire.Sent.Clear();
                Parties.TryHandleCommand(leader, "/pinvite " + StorageTestHarness.Session(member).CharacterName);
                var invite = PartyLine(wire.Sent.Single()).Split('|');
                Parties.TryHandleCommand(member, $"/pjoin {invite[3]} {invite[4]}");
            }
        }
        private static string PartyLine(byte[] packet) => System.Text.Encoding.ASCII.GetString(
            packet, 31, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28))).TrimEnd('\0');
        public void Kill(GameClient killer, long id = 0) => Combat.ApplyDamage(killer, id,
            StorageTestHarness.Session(killer).GetMonsterHandle(id), 100_000).Should().Be(0);
        public byte[] GoldEnter(GameClient client) => Connections[client].Sent.Single(p =>
            Id(p) == GamePackets.TM_SC_ENTER && p[25] == 2);
        public static GamePackets Id(byte[] p) => (GamePackets)BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4));
    }

    [Test]
    public async Task Solo_kill_uses_resource_rates_and_gold_is_credited_only_after_pickup()
    {
        var h = new Harness(rates: new RatesOptions { Exp = 2, Jp = 3, EventStatePath = "" });
        var player = h.Player(1, "Solo"); h.Kill(player); h.Kill(player);
        var info = StorageTestHarness.Session(player);
        info.CharacterExp.Should().Be(1462); info.CharacterJp.Should().Be(357);
        info.CharacterChaos.Should().Be(11); info.CharacterGold.Should().Be(0);
        A.CallTo(() => h.Leveling.ApplyExperience(player)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Repository.GetByIds(A<IReadOnlyCollection<int>>._)).MustHaveHappenedOnceExactly();
        var enter = h.GoldEnter(player);
        enter.AsSpan(26, 8).ToArray().Should().Equal(new byte[8]);
        BinaryPrimitives.ReadInt64LittleEndian(enter.AsSpan(34)).Should().Be(101);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8));
        await h.Ground.TakeAsync(player, handle); await h.Ground.TakeAsync(player, handle);
        info.CharacterGold.Should().Be(101);
        Fake.GetCalls(h.Characters).Should().BeEmpty("gold is not an inventory row");
        Fake.GetCalls(h.Weights).Should().BeEmpty("gold has no carried item weight");
        var ids = h.Connections[player].Sent.Select(Harness.Id).ToArray();
        Array.IndexOf(ids, GamePackets.TM_SC_TAKE_ITEM_RESULT).Should().BeLessThan(Array.IndexOf(ids, GamePackets.TM_SC_LEAVE));
    }

    [Test]
    public void Replacement_resource_snapshot_and_secondary_rewards_are_used()
    {
        var row = new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100, Exp = 1, Jp = 2,
            Exp2 = 903, Jp2 = 75, GoldDropPercentage = 100, GoldMin = 1, GoldMax = 1,
            GoldMin2 = 203, GoldMax2 = 203, ChaosDropPercentage = 100, ChaosMin2 = 17, ChaosMax2 = 17 };
        var h = new Harness(row, secondary: true, alias: true); var player = h.Player(1, "Alias");
        row.Exp2 = 9999; // The world freezes its loaded resource, no database lookup at death.
        h.Kill(player);
        h.World.TryGetInstance(0, out var instance).Should().BeTrue(); instance.MonsterId.Should().Be(12);
        StorageTestHarness.Session(player).CharacterExp.Should().Be(903); StorageTestHarness.Session(player).CharacterJp.Should().Be(75);
        StorageTestHarness.Session(player).CharacterChaos.Should().Be(17);
        BinaryPrimitives.ReadInt64LittleEndian(h.GoldEnter(player).AsSpan(34)).Should().Be(203);
    }

    [TestCase("monopoly")] [TestCase("random")] [TestCase("linear")]
    public async Task Party_gold_is_shared_in_every_loot_mode_and_distant_members_do_not_receive_it(string mode)
    {
        var h = new Harness(); var ana = h.Player(1, "Ana"); var bo = h.Player(2, "Bo", 1050);
        var far = h.Player(3, "Cy", 1451); h.Join(ana, bo, far);
        h.Parties.TryHandleCommand(ana, "/pshare " + mode);
        h.Kill(ana);
        var enter = h.GoldEnter(bo); var handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8));
        await h.Ground.TakeAsync(bo, handle);
        StorageTestHarness.Session(ana).CharacterGold.Should().Be(51); StorageTestHarness.Session(bo).CharacterGold.Should().Be(50);
        StorageTestHarness.Session(far).CharacterGold.Should().Be(0);
        (StorageTestHarness.Session(ana).CharacterExp + StorageTestHarness.Session(bo).CharacterExp + StorageTestHarness.Session(far).CharacterExp).Should().Be(731);
        (StorageTestHarness.Session(ana).CharacterChaos + StorageTestHarness.Session(bo).CharacterChaos + StorageTestHarness.Session(far).CharacterChaos).Should().Be(11);
    }

    [Test]
    public async Task Gold_at_wallet_limit_stays_on_ground_and_a_party_refusal_credits_nobody()
    {
        var h = new Harness(); var ana = h.Player(1, "Ana"); var bo = h.Player(2, "Bo"); h.Join(ana, bo);
        StorageTestHarness.Session(bo).CharacterGold = GoldRules.MaxCarried;
        h.Kill(ana); var handle = BinaryPrimitives.ReadUInt32LittleEndian(h.GoldEnter(ana).AsSpan(8));
        await h.Ground.TakeAsync(ana, handle);
        StorageTestHarness.Session(ana).CharacterGold.Should().Be(0); StorageTestHarness.Session(bo).CharacterGold.Should().Be(GoldRules.MaxCarried);
        h.Now += GroundItemPickupRules.FirstDeadlineTicks + 1;
        h.Ground.TryFindNearest(ana, 1000, 1000, 0, 300, out _).Should().BeTrue();
        var result = h.Connections[ana].Sent.Last(p => Harness.Id(p) == GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9)).Should().Be((ushort)ResultCode.TooMuchMoney);
        StorageTestHarness.Session(bo).CharacterGold -= 50;
        await h.Ground.TakeAsync(ana, handle);
        StorageTestHarness.Session(ana).CharacterGold.Should().Be(51); StorageTestHarness.Session(bo).CharacterGold.Should().Be(GoldRules.MaxCarried);
        h.Ground.TryFindNearest(ana, 1000, 1000, 0, 300, out _).Should().BeFalse();
    }

    [Test]
    public async Task Concurrent_pickups_and_concurrent_kills_do_not_duplicate_or_lose_rewards()
    {
        var h = new Harness(); var player = h.Player(1, "Solo");
        await Task.WhenAll(Task.Run(() => h.Kill(player, 0)), Task.Run(() => h.Kill(player, 1)));
        StorageTestHarness.Session(player).CharacterExp.Should().Be(1462); StorageTestHarness.Session(player).CharacterJp.Should().Be(238);
        StorageTestHarness.Session(player).CharacterChaos.Should().Be(22);
        var handles = h.Connections[player].Sent.Where(p => Harness.Id(p) == GamePackets.TM_SC_ENTER && p[25] == 2)
            .Select(p => BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8))).ToArray();
        handles.Should().HaveCount(2);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => h.Ground.TakeAsync(player, handles[0]))));
        StorageTestHarness.Session(player).CharacterGold.Should().Be(101);
    }

    [Test]
    public void Chaos_caps_at_effective_capacity_and_uses_each_viewers_corpse_handle()
    {
        var h = new Harness(); var player = h.Player(1, "Solo"); var viewer = h.Player(2, "Viewer");
        StorageTestHarness.Session(player).CharacterChaos = 497;
        h.Kill(player);
        StorageTestHarness.Session(player).CharacterChaos.Should().Be(500); StorageTestHarness.Session(viewer).CharacterChaos.Should().Be(0);
        var own = h.Connections[player].Sent.Single(p => Harness.Id(p) == GamePackets.TM_SC_GET_CHAOS);
        var peer = h.Connections[viewer].Sent.Single(p => Harness.Id(p) == GamePackets.TM_SC_GET_CHAOS);
        own.Length.Should().Be(25); own[6].Should().Be(StorageTestHarness.Checksum(own));
        BinaryPrimitives.ReadUInt32LittleEndian(peer.AsSpan(7)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(peer.AsSpan(11)).Should().Be(0x40000002);
        BinaryPrimitives.ReadInt32LittleEndian(peer.AsSpan(15)).Should().Be(3);
        peer.AsSpan(19, 6).ToArray().Should().Equal(new byte[6]);
        h.Kill(player, 1);
        h.Connections[player].Sent.Count(p => Harness.Id(p) == GamePackets.TM_SC_GET_CHAOS).Should().Be(1);
    }

    [Test]
    public void Highest_damage_attacker_receives_the_largest_reward_even_if_another_player_finishes()
    {
        var h = new Harness(); var first = h.Player(1, "First"); var last = h.Player(2, "Last");
        h.Combat.ApplyDamage(first, 0, StorageTestHarness.Session(first).GetMonsterHandle(0), 160).Should().Be(40);
        h.Kill(last);
        StorageTestHarness.Session(first).CharacterExp.Should().Be(584);
        StorageTestHarness.Session(last).CharacterExp.Should().Be(146);
        StorageTestHarness.Session(first).CharacterChaos.Should().Be(8);
        StorageTestHarness.Session(last).CharacterChaos.Should().Be(2);
        h.Kill(last);
        StorageTestHarness.Session(last).CharacterExp.Should().Be(146, "death credit is claimed once");
    }

    [TestCase(0, 0)] [TestCase(12, 0)] [TestCase(13, 1)]
    public void Raid_regular_monsters_skip_gold_and_bosses_keep_it(int rank, int goldDrops)
    {
        var h = new Harness(new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100,
            Exp = 100, MonsterType = rank, GoldDropPercentage = 100, GoldMin = 10, GoldMax = 10 }, raid: true);
        var player = h.Player(1, "Raid"); h.Kill(player);
        StorageTestHarness.Session(player).CharacterExp.Should().Be(100);
        h.Connections[player].Sent.Count(p => Harness.Id(p) == GamePackets.TM_SC_ENTER && p[25] == 2).Should().Be(goldDrops);
    }

    [TestCase(false)] [TestCase(true)]
    public void Stamina_bonus_is_paid_on_kill_and_saver_preserves_zero_stamina(bool saver)
    {
        var h = new Harness(new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100, Exp = 100, Jp = 10 });
        var player = h.Player(1, "Stamina"); var info = StorageTestHarness.Session(player);
        info.CharacterStamina = saver ? 0 : 100000;
        if (saver) info.ActiveBuffs.Add(new Navislamia.Game.Services.Buffs.ActiveBuff(1, 4003, 0, 1, ServerClock.Now, 0));
        h.Kill(player);
        info.CharacterExp.Should().Be(200); info.CharacterJp.Should().Be(20);
        info.CharacterStamina.Should().Be(saver ? 0 : 93007);
    }

    [Test]
    public async Task A_pet_can_pick_up_gold_for_its_owner_without_creating_an_inventory_item()
    {
        var h = new Harness(); var player = h.Player(1, "Solo"); h.Kill(player);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(h.GoldEnter(player).AsSpan(8));
        (await h.Ground.TakeForPetAsync(player, handle, 987)).Should().BeFalse("the pet waits 30 s, even for its master");
        h.Now += GroundItemPickupRules.FirstDeadlineTicks + 1;
        (await h.Ground.TakeForPetAsync(player, handle, 987)).Should().BeTrue();
        StorageTestHarness.Session(player).CharacterGold.Should().Be(101);
        var taken = h.Connections[player].Sent.Single(p => Harness.Id(p) == GamePackets.TM_SC_TAKE_ITEM_RESULT);
        BinaryPrimitives.ReadUInt32LittleEndian(taken.AsSpan(11)).Should().Be(987);
        Fake.GetCalls(h.Characters).Should().BeEmpty();
        (await h.Ground.TakeForPetAsync(player, handle, 987)).Should().BeFalse();
    }

    [Test]
    public async Task A_notification_failure_after_gold_credit_does_not_allow_another_pickup()
    {
        var h = new Harness(); var player = h.Player(1, "Solo"); h.Kill(player);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(h.GoldEnter(player).AsSpan(8));
        h.Connections[player].FailPacket = GamePackets.TM_SC_TAKE_ITEM_RESULT;
        await h.Ground.TakeAsync(player, handle);
        h.Connections[player].FailPacket = null;
        await h.Ground.TakeAsync(player, handle);
        StorageTestHarness.Session(player).CharacterGold.Should().Be(101);
        h.Ground.TryFindNearest(player, 1000, 1000, 0, 300, out _).Should().BeFalse();
    }

    [Test]
    public void Scripted_reinforcements_keep_the_selected_resource_reward_set()
    {
        var row = new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100, Exp = 10, Jp = 5,
            Exp2 = 901, Jp2 = 77, ChaosDropPercentage = 100, ChaosMin2 = 19, ChaosMax2 = 19 };
        var h = new Harness(row, secondary: true); var player = h.Player(1, "Solo");
        var reinforcement = h.World.RespawnNearMonster(0, 2101, 1).Single().Instance;
        reinforcement.Rewards.Exp.Should().Be(901);
        StorageTestHarness.Session(player).SpawnedMonsters[reinforcement.InstanceId] = 654;
        h.Kill(player, reinforcement.InstanceId);
        StorageTestHarness.Session(player).CharacterExp.Should().Be(901);
        StorageTestHarness.Session(player).CharacterJp.Should().Be(77);
        StorageTestHarness.Session(player).CharacterChaos.Should().Be(19);
    }

    [Test]
    public void Server_configuration_selects_secondary_rewards_when_positions_come_from_the_catalog()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Navislamia-rewards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var catalogPath = Path.Combine(folder, "monster-spawns.73.json");
        try
        {
            File.WriteAllText(catalogPath, """
                {"MonsterSpawnCatalog":{"Spawns":[{"MonsterId":2101,"X":1000,"Y":1000,"Count":1}]}}
                """);
            var environment = A.Fake<IHostEnvironment>();
            A.CallTo(() => environment.ContentRootPath).Returns(folder);
            var context = new HostBuilderContext(new Dictionary<object, object>())
            {
                HostingEnvironment = environment,
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    { ["MonsterSpawns:UseSecondaryRewards"] = "true" }).Build()
            };
            var services = new ServiceCollection();
            typeof(DevConsole.Program).GetMethod("ConfigureMonsterSpawns", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { services, context });
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<MonsterSpawnOptions>>().Value;
            var monster = MonsterInstanceFactory.Build(options,
                new[] { new MonsterResourceEntity { Id = 2101, Hp = 100, Exp = 10, Exp2 = 901 } }).Single();
            monster.Rewards.Exp.Should().Be(901);
            monster.X.Should().Be(1000); monster.Y.Should().Be(1000);
        }
        finally
        {
            File.Delete(catalogPath);
            Directory.Delete(folder);
        }
    }
}
