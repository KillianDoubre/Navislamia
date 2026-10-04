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
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>Riding a summon (320/321), the Mirror of Taming Card, evolution (307) and the creature keeper's pages.</summary>
[TestFixture]
public class SummonMountEvolutionTests
{
    private sealed class FixedRandom : ICombatRandom
    {
        public int Value;
        public int Next(int maxExclusive) => Math.Min(Value, maxExclusive - 1);
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly ICombatService Combat = A.Fake<ICombatService>();
        public readonly Navislamia.Game.Services.Casting.ICastInterrupts Casts = A.Fake<Navislamia.Game.Services.Casting.ICastInterrupts>();
        public readonly FixedRandom Random = new() { Value = 99 };
        public readonly CreatureService Service;
        public readonly CreatureDialogService Dialogs;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public readonly CreatureCard Card;

        public Harness(int level = 50, bool ridable = true)
        {
            var players = A.Fake<IPlayerVisibilityService>();
            A.CallTo(() => players.Registry).Returns(new PlayerRegistry());
            A.CallTo(() => players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(Array.Empty<MonsterResourceEntity>());
            var catalog = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
            {
                Summons =
                {
                    new SummonResourceOptions { Id = 2101, Form = 1, EvolveTarget = 2102, CardId = 540014, StatId = 2101,
                        RunSpeed = 100, RidingSpeed = 280, AttackRange = 0.2f, Size = 2.4f, Scale = 1f,
                        Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 } },
                    new SummonResourceOptions { Id = 2102, Form = 2, EvolveTarget = 2103, CardId = 540014, StatId = 2102,
                        RunSpeed = 100, AttackRange = 0.2f, Size = 2.4f, Scale = 1f,
                        Stats = new float[] { 24, 17, 26, 15, 20, 18, 20 } }
                }
            }));
            Service = new CreatureService(catalog, Characters,
                new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions())), Combat,
                new SummonWorldService(players), players, random: Random, runTicks: false, castInterrupts: Casts);
            Dialogs = new CreatureDialogService(Service);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterLevel = 60;
            Info.CharacterHp = 1000;
            Info.CharacterMaxHp = 1000;
            Card = new CreatureCard
            {
                ItemId = 60, Code = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None),
                SummonId = 9, SummonCode = 2101, SummonName = "RossParr", Level = level, MaxReachedLevel = level,
                SummonHandle = 0x50000001, InfoSent = true
            };
            if (ridable)
            {
                Card.Skills[CreatureService.CreatureRidingSkill] = 1;
            }

            Info.CreatureCards[60] = Card;
            Info.SummonSlots = new long[] { 60, 0, 0, 0, 0, 0 };
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public byte[] Last(GamePackets id) =>
            Sent.LastOrDefault(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)id);
    }

    [Test]
    public void A_ridable_summon_out_in_the_world_is_mounted_and_sets_the_speed()
    {
        var h = new Harness();
        h.Service.Summon(h.Client, 60).Should().BeTrue();

        h.Service.Mount(h.Client, h.Card.SummonHandle).Should().BeTrue();
        h.Info.RideHandle.Should().Be(h.Card.SummonHandle);
        h.Last(GamePackets.TM_SC_MOUNT_SUMMON)[23].Should().Be(1, "success");
        // 280 / 7 × min(1, 0.9 + 0.1 + 0.01) = 40.
        h.Service.RidingSpeed(h.Info).Should().Be(40);

        h.Service.Unmount(h.Client);
        h.Info.RideHandle.Should().Be(0);
        var unmount = h.Last(GamePackets.TM_SC_UNMOUNT_SUMMON);
        unmount[15].Should().Be((byte)CreatureService.UnmountNormal);
        h.Service.RidingSpeed(h.Info).Should().BeNull();
    }

    [Test]
    public void A_summon_without_creature_riding_or_kept_in_its_card_cannot_be_mounted()
    {
        var plain = new Harness(ridable: false);
        plain.Service.Summon(plain.Client, 60);
        plain.Service.Mount(plain.Client, plain.Card.SummonHandle).Should().BeFalse();
        plain.Last(GamePackets.TM_SC_MOUNT_SUMMON)[23].Should().Be(0);

        var kept = new Harness();
        kept.Service.Mount(kept.Client, kept.Card.SummonHandle).Should().BeFalse();
    }

    [Test]
    public void A_hit_throws_the_rider_off_three_times_in_ten_and_a_fall_costs_hp()
    {
        var h = new Harness();
        h.Service.Summon(h.Client, 60);
        h.Service.Mount(h.Client, h.Card.SummonHandle);

        Fake.ClearRecordedCalls(h.Combat);
        h.Random.Value = 50;
        h.Service.OnPlayerDamaged(h.Client, 10, false);
        h.Info.RideHandle.Should().NotBe(0, "50 is above the 30 % chance");

        h.Random.Value = 10;
        h.Service.OnPlayerDamaged(h.Client, 10, false);
        h.Info.RideHandle.Should().Be(0);
        h.Last(GamePackets.TM_SC_UNMOUNT_SUMMON)[15].Should().Be((byte)CreatureService.UnmountFall);
        A.CallTo(() => h.Combat.DamagePlayer(h.Client, 50)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Casts.ApplyState(h.Client, 9001, 1, 300)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Combat.StopAttack(h.Client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Fall_prevents_remounting_until_expiry_and_handles_clock_wrap()
    {
        var h = new Harness(); h.Service.Summon(h.Client, 60);
        var now = ServerClock.Now;
        h.Info.ActiveBuffs.Add(new Navislamia.Game.Services.Buffs.ActiveBuff(1, 9001, 0, 1, now, now + 300));
        h.Service.Mount(h.Client, h.Card.SummonHandle).Should().BeFalse();
        SummonFall.IsActive(h.Info, now + 299).Should().BeTrue();
        SummonFall.IsActive(h.Info, now + 300).Should().BeFalse();
        h.Info.ActiveBuffs.Clear(); h.Service.Mount(h.Client, h.Card.SummonHandle).Should().BeTrue();
        h.Info.ActiveBuffs.Add(new Navislamia.Game.Services.Buffs.ActiveBuff(1, 9001, 0, 1, uint.MaxValue - 99, 200));
        SummonFall.IsActive(h.Info, uint.MaxValue - 50).Should().BeTrue();
        SummonFall.IsActive(h.Info, 200).Should().BeFalse();
    }

    [Test]
    public void A_mount_that_dies_or_is_sent_back_drops_its_rider()
    {
        var dead = new Harness();
        dead.Service.Summon(dead.Client, 60);
        dead.Service.Mount(dead.Client, dead.Card.SummonHandle);
        dead.Service.DamageSummon(dead.Client, dead.Card.SummonHandle, 1_000_000);
        dead.Info.RideHandle.Should().Be(0);
        dead.Last(GamePackets.TM_SC_UNMOUNT_SUMMON)[15].Should().Be((byte)CreatureService.UnmountFall);

        var back = new Harness();
        back.Service.Summon(back.Client, 60);
        back.Service.Mount(back.Client, back.Card.SummonHandle);
        back.Service.Unsummon(back.Client, 60);
        back.Last(GamePackets.TM_SC_UNMOUNT_SUMMON)[15].Should().Be((byte)CreatureService.UnmountUnsummon);
    }

    [Test]
    public void A_summon_evolves_kept_in_its_card_at_the_official_level()
    {
        var young = new Harness(level: 49);
        young.Service.Evolve(young.Client, young.Card).Should().BeFalse();

        var outside = new Harness();
        outside.Service.Summon(outside.Client, 60);
        outside.Service.Evolve(outside.Client, outside.Card).Should().BeFalse("it must be sent back first");

        var h = new Harness();
        h.Service.Evolve(h.Client, h.Card).Should().BeTrue();
        h.Card.SummonCode.Should().Be(2102);
        h.Card.PreviousSummonIds[0].Should().Be(2101);
        h.Card.PreviousLevels[0].Should().Be(50);
        h.Card.Level.Should().Be(50, "the level stays");
        BinaryPrimitives.ReadInt32LittleEndian(h.Last(GamePackets.TM_SC_SUMMON_EVOLUTION).AsSpan(34, 4))
            .Should().Be(2102);
        A.CallTo(() => h.Characters.SaveSummonProgressAsync("Ana", A<IReadOnlyList<SummonProgress>>.That.Matches(p =>
            p.Single().SummonResourceId == 2102 && p.Single().PreviousSummonResourceIds[0] == 2101)))
            .MustHaveHappened();
    }

    [Test]
    public void The_keeper_revives_a_dead_summon_for_the_lua_price()
    {
        var h = new Harness(level: 10);
        h.Card.Hp = 0;
        h.Card.HpKnown = true;
        h.Info.CharacterGold = 10_000;

        var care = h.Dialogs.Select(h.Client, 1001, "Creature_Care_Deva", "Creature_Care_Deva( 60 )").Page;
        care.Text.Should().StartWith("@90010019", "the dead summon's text");
        var prices = CreatureDialogService.CarePrices(10, 0, 0);
        prices.Revive.Should().Be(420 + 42);
        care.Menu.Select(m => m.Trigger).Should().Contain($"Creature_Care_RecoverEx_HP_Deva( 60, {prices.Revive}, 30 )");

        var done = h.Dialogs.Select(h.Client, 1001, "Creature_Care_RecoverEx_HP_Deva",
            $"Creature_Care_RecoverEx_HP_Deva( 60, {prices.Revive}, 30 )").Page;
        done.Text.Should().Be("@90010031");
        h.Info.CharacterGold.Should().Be(10_000 - prices.Revive);
        h.Card.IsDead.Should().BeFalse();
        h.Card.Hp.Should().Be((int)Math.Floor(h.Service.VitalsOf(h.Client, h.Card).MaxHp * 30 / 100.0));
    }

    [Test]
    public void The_keeper_refuses_a_recovery_without_the_gold()
    {
        var h = new Harness();
        h.Info.CharacterGold = 5;
        var page = h.Dialogs.Select(h.Client, 2001, "Creature_Care_Recover_ALL_Asura",
            "Creature_Care_Recover_ALL_Asura( 60, 100 )").Page;
        page.Text.Should().Be("@90010005");
        page.Title.Should().Be("@90200101");
        h.Info.CharacterGold.Should().Be(5);
    }

    [Test]
    public void The_keeper_lists_the_formed_summons_and_the_evolvable_ones()
    {
        var h = new Harness();
        var management = h.Dialogs.Select(h.Client, 4001, "Creature_Management_Gaia", "Creature_Management_Gaia()").Page;
        management.Title.Should().Be("@90400101");
        management.Menu.Select(m => m.Trigger).Should().Contain("Creature_Care_Gaia( 60 )");

        var menu = h.Dialogs.Select(h.Client, 4001, CreatureDialogService.EvolutionMenu, "NPC_Creature_Evolution_Menu()").Page;
        menu.Text.Should().Be("@90400113");
        menu.Menu.Select(m => m.Trigger).Should().Contain("Creature_Evolution_sub( 60 )");

        var sub = h.Dialogs.Select(h.Client, 4001, CreatureDialogService.EvolutionSub, "Creature_Evolution_sub( 60 )").Page;
        sub.Menu.Select(m => m.Trigger).Should().Contain("Creature_Evolution_exe( 60 )");

        var exe = h.Dialogs.Select(h.Client, 4001, CreatureDialogService.EvolutionExe, "Creature_Evolution_exe( 60 )").Page;
        exe.Text.Should().Be("@90400117");
        h.Card.SummonCode.Should().Be(2102);

        var none = new Harness(level: 10).Dialogs;
        var empty = none.Select(new Harness(level: 10).Client, 1001, CreatureDialogService.EvolutionMenu,
            "NPC_Creature_Evolution_Menu()").Page;
        empty.Text.Should().Be("@90100114");
    }

    [Test]
    public void The_dialog_parser_knows_the_keeper_functions_and_their_arguments()
    {
        CreatureDialogService.Handles("Creature_Care_RecoverEx_HP_Deva").Should().BeTrue();
        CreatureDialogService.Handles("Creature_Management_Asura").Should().BeTrue();
        CreatureDialogService.Handles("Creature_name_change_Menu").Should().BeFalse();
        CreatureDialogService.Handles("NPC_Creature_Evolution_Menu").Should().BeTrue();
        CreatureDialogService.Arguments("Creature_Care_Recover_HP_Deva( 60, 120 )").Should().Equal(60, 120);
        CreatureDialogService.Call("Creature_Care_Deva", 60).Should().Be("Creature_Care_Deva( 60 )");
        GmCommandCatalog.TryResolve("ride", 0, out var ride).Should().BeTrue("/ride is a player command (NORMAL2)");
        ride.Privileged.Should().BeFalse();
        GmCommandCatalog.TryResolve("unride", 0, out _).Should().BeTrue();
    }

    [Test]
    public async Task A_mirror_breaks_at_the_draw_and_keeps_the_card_from_a_failure()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity
            {
                Id = 1, AccountId = 4, AccountName = "A", CharacterName = "Ana",
                Items = new List<ItemEntity>
                {
                    new() { Id = 50, ItemResourceId = 540014, Amount = 2, Idx = 1, WearInfo = ItemWearType.None },
                    new() { Id = 70, ItemResourceId = 960019, Amount = 2, Idx = 2, WearInfo = ItemWearType.None }
                },
                Skills = new List<CharacterSkillEntity>()
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);
        var failed = await service.CommitTamingAsync("Ana", 50, false, 2101, null, 0, 0);
        failed.RemainingAmount.Should().Be(2, "the mirror protected the card");
        failed.MirrorItemId.Should().Be(70);
        failed.MirrorRemaining.Should().Be(1);

        var tamed = await service.CommitTamingAsync("Ana", 50, true, 2101, "RossParr", 100, 50);
        tamed.RemainingAmount.Should().Be(1, "a success consumes the card even with a mirror");
        tamed.MirrorRemaining.Should().Be(0, "the mirror breaks on a success too");

        (await service.CommitTamingAsync("Ana", 50, false, 2101, null, 0, 0)).RemainingAmount
            .Should().Be(0, "no mirror left: the failure burns the card");
    }
}
