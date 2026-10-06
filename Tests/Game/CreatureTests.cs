using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>Taming, formation and summoning (docs/packet-specs/socle-apprivoisement-invocation.md §15).</summary>
[TestFixture]
public partial class CreatureTests
{
    private const int SummonId = 2201;
    private const int CardId = 540015;
    private const int MonsterId = 3001;

    private static CreatureCatalog Catalog() => new(Options.Create(new CreatureCatalogOptions
    {
        Summons =
        {
            new SummonResourceOptions { Id = SummonId, Form = 1, CardId = CardId, StatId = 1, RunSpeed = 100,
                AttackRange = 0.2f, Size = 3.3f, Scale = 1f, Stats = new float[] { 50, 60, 30, 20, 40, 30, 20 } },
            new SummonResourceOptions { Id = 2202, Form = 2, CardId = CardId, StatId = 2, RunSpeed = 100 }
        },
        NamePrefixes = { "Ross" },
        NamePostfixes = { "Parr" },
        TamableMonsterNames = { [MonsterId.ToString()] = "Tortus" }
    }));

    private sealed class SequenceRandom : ICombatRandom
    {
        private readonly int _value;
        public SequenceRandom(int value) => _value = value;
        public int Next(int maxExclusive) => Math.Min(_value, maxExclusive - 1);
    }

    [Test]
    public void The_taming_chance_is_the_percentage_times_the_skill_factor()
    {
        // 0.25 × (0.06 × 1 + 1) = 26.5 % → fixed 265 000 against XRandom(1, 1 000 000).
        CreatureRules.TamingSucceeds(0.25m, 0.06m, 0.03m, 1, 0, 265_000).Should().BeTrue();
        CreatureRules.TamingSucceeds(0.25m, 0.06m, 0.03m, 1, 0, 265_001).Should().BeFalse();
        CreatureRules.TamingSucceeds(0m, 0.06m, 0.03m, 5, 0, 1).Should().BeFalse("a monster with no chance never tames");
        CreatureRules.SlotCount(9).Should().Be(6);
        CreatureRules.SlotCount(2).Should().Be(2);
        CreatureRules.TrimName("ABCDEFGHIJKLMNOPQRSTUV").Should().HaveLength(18);
    }

    [Test]
    public void The_catalogue_resolves_cards_names_and_the_first_form()
    {
        var catalog = Catalog();
        catalog.IsCard(CardId).Should().BeTrue();
        catalog.FirstSummonForCard(CardId).Id.Should().Be(SummonId);
        catalog.RandomName(_ => 0).Should().Be("RossParr");
        catalog.MonsterName(MonsterId).Should().Be("Tortus");
        CreatureRules.SummonStats(catalog.FirstSummonForCard(CardId), 1).MaxHp.Should().BeGreaterThan(0);
    }

    [Test]
    public void A_formation_keeps_bound_cards_binds_eligible_ones_and_refuses_to_unbind_a_summon_in_the_world()
    {
        var current = new long[] { 10, 0, 0, 0, 0, 0 };
        var slots = CreatureRules.ResolveFormation(current, new uint[] { 0, 10, 20, 30, 20, 0 }, 4,
            card => card == 20, _ => false);
        slots.Should().Equal(0, 10, 20, 0, 0, 0);

        CreatureRules.ResolveFormation(current, new uint[6], 4, _ => true, card => card == 10)
            .Should().BeNull("the summon of card 10 is out");

        CreatureRules.ResolveFormation(current, new uint[] { 10, 20, 0, 0, 0, 0 }, 1, _ => true, _ => false)
            .Should().Equal(10, 0, 0, 0, 0, 0);
    }

    [Test]
    public void A_card_left_out_of_a_formation_says_why()
    {
        // 2026-10-04: two tamed cards (170, 173) with Creature Control at level 1 — one slot.
        var tamed = new HashSet<long> { 170, 173 };
        var requested = new uint[] { 170, 173, 0, 0, 0, 0 };
        var resolved = CreatureRules.ResolveFormation(new long[6], requested, 1, tamed.Contains, _ => false);
        CreatureRules.FormationRefusals(requested, resolved, 1, tamed.Contains).Should().ContainSingle()
            .Which.Should().Be((173u, "slot 2 is beyond the 1 slot(s) of Creature Control"));

        // 2026-10-06: an empty card (146) next to a tamed one, three slots.
        requested = new uint[] { 173, 146, 0, 0, 0, 0 };
        resolved = CreatureRules.ResolveFormation(new long[] { 173, 0, 0, 0, 0, 0 }, requested, 3, tamed.Contains,
            _ => false);
        resolved.Should().Equal(173, 0, 0, 0, 0, 0);
        CreatureRules.FormationRefusals(requested, resolved, 3, tamed.Contains).Should().ContainSingle()
            .Which.Should().Be((146u, "the card is not tamed (no ITEM_FLAG_SUMMON)"));
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly ICombatService Combat = A.Fake<ICombatService>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly PlayerRegistry Registry = new();
        public readonly MonsterWorldState World;
        public readonly CreatureService Service;
        public readonly CreatureEvents Events = new();
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public const uint MonsterHandle = 0x40000001;

        public Harness(int roll = 0, int monsterTamingId = SummonId)
        {
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
            {
                new MonsterResourceEntity { Id = MonsterId, Level = 5, Hp = 100, TamingId = monsterTamingId,
                    TamingPercentage = 0.25m, Size = 10, Scale = 1 }
            });
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
                { Spawns = { new MonsterSpawnPoint { MonsterId = MonsterId, X = 100, Y = 100, Count = 1, Radius = 0 } } }));
            World.WithinRange(100, 100, 10);
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            Service = new CreatureService(Catalog(), Characters, World, Combat, new SummonWorldService(Players), Players,
                events: Events, random: new SequenceRandom(roll), runTicks: false);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterHp = 100;
            Info.X = 100;
            Info.Y = 100;
            Info.DestinationX = 100;
            Info.DestinationY = 100;
            Info.SpawnedMonsters[0] = MonsterHandle;
            Registry.Register(7, Client);
            Info.CreatureCards[50] = new CreatureCard { ItemId = 50, Code = CardId, Amount = 3, Flag = ItemFlag.None };
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public IEnumerable<ushort> Ids => Sent.Select(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)));

        public sbyte LastTamingMode => (sbyte)Sent.Last(p =>
            BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_TAMING_INFO)[7];
    }

    [Test]
    public void Taming_is_refused_with_the_official_codes_in_their_order()
    {
        var h = new Harness(monsterTamingId: 0);
        h.Service.CheckTaming(h.Info, 0).Should().Be(ResultCode.NotTamable);

        h = new Harness();
        h.World.ApplyDamage(0, 1);
        h.Service.CheckTaming(h.Info, 0).Should().Be(ResultCode.NotEnoughTargetHP);

        h = new Harness();
        h.Info.CreatureCards.Clear();
        h.Service.CheckTaming(h.Info, 0).Should().Be(ResultCode.NotEnoughSummonCard);

        h = new Harness();
        h.Service.CheckTaming(h.Info, 0).Should().Be(ResultCode.Success);
        h.Service.StartTaming(h.Client, 0, 1).Should().BeTrue();
        h.Service.CheckTaming(h.Info, 0).Should().Be(ResultCode.TargetAlreadyBeingTamed);
        h.LastTamingMode.Should().Be(GameSummonPackets.TamingModeStart);
        h.Ids.Last().Should().Be((ushort)GamePackets.TM_SC_CHAT);
        h.Info.TamingTargetInstanceId.Should().Be(0);
    }

    [Test]
    public async Task A_successful_draw_takes_the_loot_and_turns_one_card_into_a_bound_one()
    {
        var h = new Harness(roll: 0);
        var bound = new ItemEntity { Id = 60, ItemResourceId = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
        A.CallTo(() => h.Characters.CommitTamingAsync("Ana", 50, true, SummonId, A<string>._, A<int>._, A<int>._))
            .Returns(new TamingCommit(50, 2, bound, new SummonEntity { Id = 9, SummonResourceId = SummonId,
                CardItemId = 60, Name = "RossParr", Lv = 1 }));
        h.Service.StartTaming(h.Client, 0, 1);
        h.World.TryKill(0, DateTime.UtcNow.AddMinutes(1));

        h.Events.MonsterKilled(0).Should().BeTrue("roll 1 is under 26.5 %");
        await Task.Delay(50);

        h.LastTamingMode.Should().Be(GameSummonPackets.TamingModeSuccess);
        h.Info.CreatureCards[50].Amount.Should().Be(2);
        h.Info.CreatureCards[60].IsBound.Should().BeTrue();
        h.Info.CreatureCards[60].SummonCode.Should().Be(SummonId);
        h.Info.TamingTargetInstanceId.Should().Be(-1);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT).And.Contain((ushort)GamePackets.TM_SC_INVENTORY);
    }

    [Test]
    public async Task A_failed_draw_burns_the_card_and_keeps_the_loot()
    {
        var h = new Harness(roll: 999_999);
        A.CallTo(() => h.Characters.CommitTamingAsync("Ana", 50, false, SummonId, A<string>._, A<int>._, A<int>._))
            .Returns(new TamingCommit(50, 2, null, null));
        h.Service.StartTaming(h.Client, 0, 1);
        h.Events.MonsterKilled(0).Should().BeFalse();
        await Task.Delay(50);
        h.LastTamingMode.Should().Be(GameSummonPackets.TamingModeFailed);
    }

    [Test]
    public void A_tamer_too_far_fails_without_any_draw()
    {
        var h = new Harness(roll: 0);
        h.Service.StartTaming(h.Client, 0, 1);
        h.Info.X = h.Info.DestinationX = 2000;
        h.Events.MonsterKilled(0).Should().BeFalse();
        h.LastTamingMode.Should().Be(GameSummonPackets.TamingModeFailed);
        A.CallTo(() => h.Characters.CommitTamingAsync(A<string>._, A<long>._, A<bool>._, A<int>._, A<string>._,
            A<int>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public void The_window_closes_when_the_tamer_stops_hitting()
    {
        var h = new Harness();
        h.Service.StartTaming(h.Client, 0, 1);
        h.Service.ProcessTamings(unchecked(ServerClock.Now + CreatureRules.TamingIntervalTicks - 100));
        h.Info.TamingTargetInstanceId.Should().Be(0, "still within five minutes");

        h.Events.MonsterDamaged(h.Client, 0);
        h.Service.ProcessTamings(unchecked(ServerClock.Now + CreatureRules.TamingIntervalTicks + 100));
        h.Info.TamingTargetInstanceId.Should().Be(-1);
        h.LastTamingMode.Should().Be(GameSummonPackets.TamingModeAbandon);
    }

    private static void Bind(Harness h, bool slotted = true)
    {
        h.Info.CreatureCards[60] = new CreatureCard { ItemId = 60, Code = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None), SummonId = 9, SummonCode = SummonId,
            SummonName = "RossParr" };
        h.Info.SummonSlots = slotted ? new long[] { 60, 0, 0, 0, 0, 0 } : new long[6];
    }

    [Test]
    public async Task A_formation_announces_the_summon_once_and_saves_the_slots()
    {
        var h = new Harness();
        Bind(h, slotted: false);
        h.Info.LearnedSkills[CreatureRules.CreatureControlSkill] = 2;

        await h.Service.EquipAsync(h.Client, new uint[] { 60, 50, 0, 0, 0, 0 }, true);

        h.Info.SummonSlots.Should().Equal(60, 0, 0, 0, 0, 0);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_ADD_SUMMON_INFO);
        var reply = h.Sent.Last();
        BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_EQUIP_SUMMON);
        BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(8, 4)).Should().Be(60u);
        A.CallTo(() => h.Characters.SaveCreatureFormationAsync("Ana",
            A<long[]>.That.Matches(s => s[0] == 60), A<long?>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_formed_card_is_summoned_once_and_sent_back()
    {
        var h = new Harness();
        Bind(h);
        h.Service.CheckSummon(h.Info, 60, true).Should().Be(ResultCode.Success);
        h.Service.CheckSummon(h.Info, 50, true).Should().Be(ResultCode.NotExist, "an empty card holds no creature");

        h.Service.Summon(h.Client, 60).Should().BeTrue();
        h.Info.Summons.Should().ContainSingle();
        h.Info.MainSummonCardId.Should().Be(60);
        h.Service.CheckSummon(h.Info, 60, true).Should().Be(ResultCode.NotActable, "already out");
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_ENTER);

        var handle = h.Info.Summons[0].Handle;
        h.Service.OwnsSummon(h.Client, handle).Should().BeTrue();
        h.Service.Unsummon(h.Client, 60).Should().BeTrue();
        h.Info.Summons.Should().BeEmpty();
        h.Info.MainSummonCardId.Should().Be(0);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_UNSUMMON);
    }

    [Test]
    public async Task World_entry_announces_the_formed_summons_then_the_formation_and_brings_the_main_one_back()
    {
        var h = new Harness();
        var card = new ItemEntity { Id = 60, ItemResourceId = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
        A.CallTo(() => h.Characters.GetCreatureStateAsync("Ana", A<IReadOnlyCollection<int>>._))
            .Returns(new CreatureState(new[] { new CreatureCardRecord(card, new SummonEntity
                { Id = 9, SummonResourceId = SummonId, CardItemId = 60, Name = "RossParr", Lv = 1 }) },
                new long[] { 60, 0, 0, 0, 0, 0 }, 9));
        h.Info.CreatureCards.Clear();

        await h.Service.OnWorldEntryAsync(h.Client);

        var ids = h.Ids.ToList();
        ids.IndexOf((ushort)GamePackets.TM_SC_ADD_SUMMON_INFO).Should()
            .BeLessThan(ids.IndexOf((ushort)GamePackets.TM_EQUIP_SUMMON), "SendCharacterInfo: 301, then 303");
        var formation = h.Sent.First(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2))
                                          == (ushort)GamePackets.TM_EQUIP_SUMMON);
        BinaryPrimitives.ReadUInt32LittleEndian(formation.AsSpan(8, 4)).Should().Be(60u, "the 303 carries card handles");
        h.Info.Summons.Should().ContainSingle("the main summon comes back with its master");
    }

    [Test]
    public async Task A_failed_creature_load_still_sends_the_empty_formation()
    {
        var h = new Harness();
        A.CallTo(() => h.Characters.GetCreatureStateAsync(A<string>._, A<IReadOnlyCollection<int>>._))
            .Throws(new InvalidOperationException("database down"));
        await h.Service.OnWorldEntryAsync(h.Client);
        h.Ids.Should().ContainSingle(id => id == (ushort)GamePackets.TM_EQUIP_SUMMON);
    }

    [Test]
    public void A_card_out_of_the_formation_cannot_be_summoned()
    {
        var h = new Harness();
        Bind(h, slotted: false);
        h.Service.CheckSummon(h.Info, 60, true).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void A_summon_hits_a_monster_in_reach_and_the_master_gets_the_credit()
    {
        var h = new Harness();
        Bind(h);
        h.Service.Summon(h.Client, 60);
        var handle = h.Info.Summons[0].Handle;
        A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock { Avoid = 0, Defence = 0 });
        A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle, A<int>._, A<int>._)).Returns(40);

        h.Service.SummonAttack(h.Client, handle, Harness.MonsterHandle);
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));

        A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle, A<int>._, A<int>._))
            .MustHaveHappenedOnceExactly();
        var swing = h.Sent.Last(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2))
                                     == (ushort)GamePackets.TM_SC_ATTACK_EVENT);
        BinaryPrimitives.ReadUInt32LittleEndian(swing.AsSpan(7, 4)).Should().Be(handle, "the summon is the attacker");
    }

    [Test]
    public void A_summon_out_of_reach_tells_its_master_too_far_so_that_the_client_walks_it_in()
    {
        var h = new Harness();
        Bind(h);
        h.Service.Summon(h.Client, 60);
        var handle = h.Info.Summons[0].Handle;
        A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock());
        // The client put the summon far from the monster.
        var far = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(far.AsSpan(0, 4), h.Info.X + 3000);
        BinaryPrimitives.WriteSingleLittleEndian(far.AsSpan(4, 4), h.Info.Y + 3000);
        h.Service.MoveSummon(h.Client, handle, h.Info.X + 3000, h.Info.Y + 3000, 0, 0, far);

        h.Service.SummonAttack(h.Client, handle, Harness.MonsterHandle);
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(2));

        // StructSummon::onCantAttack: 102 TOO_FAR, summon as attacker, the master's handle of the monster as target,
        // once per 100 ticks — the 7.3 client re-issues the attack on it (SCreatureStateMachine::OnNetInput).
        var tooFar = h.Sent.Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2))
                                       == (ushort)GamePackets.TM_SC_CANT_ATTACK).ToList();
        tooFar.Should().ContainSingle("the second check comes within the same second");
        BinaryPrimitives.ReadUInt32LittleEndian(tooFar[0].AsSpan(7, 4)).Should().Be(handle);
        BinaryPrimitives.ReadUInt32LittleEndian(tooFar[0].AsSpan(11, 4)).Should().Be(Harness.MonsterHandle);
        BinaryPrimitives.ReadInt32LittleEndian(tooFar[0].AsSpan(15, 4)).Should().Be((int)ResultCode.TooFar);
        A.CallTo(() => h.Combat.ApplyDamage(h.Client, A<long>._, A<uint>._, A<int>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public void A_summon_enters_with_its_stats_and_swings_with_its_buffed_ones()
    {
        var h = new Harness();
        Bind(h);
        h.Service.Summon(h.Client, 60);
        var presence = h.Info.Summons[0];
        presence.Entry.BaseStats.Should().NotBeNull("the summon buffs fold into the summon's own stats");
        presence.Stats.MaxHp.Should().Be(presence.Entry.BaseStats.MaxHp);

        // A buff raised the attack (SummonBuffStats.Refresh replaces Stats): the swing reads it.
        presence.Stats = presence.Stats.Copy();
        presence.Stats.AttackPointRight = 100_000;
        A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock());
        h.Service.SummonAttack(h.Client, presence.Handle, Harness.MonsterHandle);
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));

        A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle,
            A<int>.That.IsGreaterThan(10_000), A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_card_on_sale_reads_its_summon_skills_from_the_database()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            // An auction lot is an item row without character or account (socle-encheres-mecanique.md).
            db.Items.Add(new ItemEntity { Id = 70, ItemResourceId = CardId, Amount = 1 });
            db.Items.Add(new ItemEntity { Id = 71, ItemResourceId = CardId, Amount = 1 });
            db.Items.Add(new ItemEntity { Id = 72, ItemResourceId = CardId, Amount = 0 });
            db.Summons.Add(new SummonEntity { Id = 9, CharacterId = 1, CardItemId = 70, SummonResourceId = SummonId });
            db.SummonSkills.Add(new SummonSkillEntity { Id = 1, SummonId = 9, SkillId = 40012, Level = 1 });
            db.SummonSkills.Add(new SummonSkillEntity { Id = 2, SummonId = 9, SkillId = 40011, Level = 3 });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);

        (await service.GetCardSkillsAsync(70)).Should().Equal(new KeyValuePair<int, byte>(40011, 3),
            new KeyValuePair<int, byte>(40012, 1));
        (await service.GetCardSkillsAsync(71)).Should().BeEmpty("a card without a summon is a 451 with no entry");
        (await service.GetCardSkillsAsync(72)).Should().BeNull("a used-up stack is no item");
        (await service.GetCardSkillsAsync(73)).Should().BeNull();
    }

    [Test]
    public async Task A_taming_commit_consumes_one_card_and_creates_the_bound_card_and_its_summon()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity
            {
                Id = 1, AccountId = 4, AccountName = "A", CharacterName = "Ana",
                Items = new List<ItemEntity> { new() { Id = 50, ItemResourceId = CardId, Amount = 2, Idx = 1 } },
                Skills = new List<CharacterSkillEntity>()
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);
        var commit = await service.CommitTamingAsync("Ana", 50, true, SummonId, "RossParr", 300, 100);

        commit.RemainingAmount.Should().Be(1);
        commit.NewCard.Flag.Should().Be(CreatureRules.WithSummonFlag(ItemFlag.None));
        commit.Summon.CardItemId.Should().Be(commit.NewCard.Id);

        var state = await service.GetCreatureStateAsync("Ana", new[] { CardId });
        state.Cards.Should().HaveCount(2);
        state.Cards.Single(c => c.Summon is not null).Summon.SummonResourceId.Should().Be(SummonId);

        (await service.SaveCreatureFormationAsync("Ana", new long[] { commit.NewCard.Id, 0, 0, 0, 0, 0 },
            commit.Summon.Id)).Should().BeTrue();
        state = await service.GetCreatureStateAsync("Ana", new[] { CardId });
        state.Slots[0].Should().Be(commit.NewCard.Id);
        state.MainSummonId.Should().Be(commit.Summon.Id);
        await using (var db = new TelecasterContext(options))
        {
            (await db.Characters.SingleAsync()).SummonSlotItemIds[0].Should()
                .Be(commit.Summon.Id, "the official row keeps summon sids (DB_Login: GetSummon(bindSummon[i]))");
        }

        (await service.CommitTamingAsync("Ana", 50, false, SummonId, null, 0, 0)).RemainingAmount.Should().Be(0);
    }
}
