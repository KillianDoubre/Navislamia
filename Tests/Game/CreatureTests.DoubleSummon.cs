using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Stats;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The double summon (306) and the cards entering and leaving the bag (301/302), socle-duree-invocations.md.</summary>
public partial class CreatureTests
{
    private const int InfiniteSummonState = 314012;
    private const int CreatureAmpState = 12682;

    private sealed class DoubleSummonStates : IStateCatalog
    {
        public IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel) => Array.Empty<StatEffect>();
        public bool IsEraseOnRequest(int stateId) => false;
        public bool Exists(int stateId) => true;

        public bool TryGetResurrection(int stateId, out ResurrectionStateValues values)
        {
            values = default;
            return false;
        }

        // Epic 7 StateResource: 314012 is EF_INFINITE_SUMMON_TIME with value_0 = 3, 12682 EF_CREATURE_PARAMETER_AMP
        // with value_4 = 10.
        public StateRule GetRule(int stateId) => stateId switch
        {
            InfiniteSummonState => new StateRule(stateId, Array.Empty<int>(), 0, 0, 3121, new[] { 3m, 0m, 0m, 0m, 0m, 0m }),
            CreatureAmpState => new StateRule(stateId, Array.Empty<int>(), 0, 0, 112, new[] { 0m, 0m, 0m, 0m, 10m, 0m }),
            _ => StateRule.None
        };
    }

    private sealed class TechnicalControlPassives : ISkillPassiveCatalog
    {
        public IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon) =>
            Array.Empty<StatEffect>();

        // Epic 7 SkillResource 1881: var1 = 7.
        public decimal FirstVar(int skillId) => skillId == DoubleSummonRules.TechnicalCreatureControlSkill ? 7m : 0m;
    }

    private static CreatureService DoubleSummonService(Harness h) =>
        new(Catalog(), h.Characters, h.World, h.Combat, new SummonWorldService(h.Players), h.Players,
            random: new SequenceRandom(0), runTicks: false, passives: new TechnicalControlPassives(),
            states: new DoubleSummonStates());

    /// <summary>Two bound cards, formed in slots 0 and 1, and a third formed in slot 2.</summary>
    private static void BindPair(Harness h, long first = 60, long second = 61)
    {
        foreach (var (id, summonId) in new[] { (60L, 9L), (61L, 10L), (62L, 11L) })
        {
            h.Info.CreatureCards[id] = new CreatureCard { ItemId = id, Code = CardId, Amount = 1,
                Flag = CreatureRules.WithSummonFlag(ItemFlag.None), SummonId = summonId, SummonCode = SummonId,
                SummonName = "RossParr" };
        }

        h.Info.SummonSlots = new[] { first, second, 62, 0, 0, 0 };
        h.Info.LearnedSkills[CreatureRules.CreatureControlSkill] = 3;
    }

    private static byte[] LastOf(Harness h, GamePackets id) =>
        h.Sent.Last(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)id);

    [Test]
    public void The_double_summon_time_is_ten_seconds_lengthened_by_the_skill_and_the_creature_states()
    {
        DoubleSummonRules.DoubleSummonTime(0, 7m, null).Should().Be(1000u, "GameRule::DEFAULT_DOUBLE_SUMMON_TIME");
        DoubleSummonRules.DoubleSummonTime(3, 7m, null).Should().Be(3100u, "1881: level × var0 × 100");
        DoubleSummonRules.DoubleSummonTime(0, 0m, new[] { (2, 10m), (0, 10m) }).Should()
            .Be(3000u, "EF_CREATURE_PARAMETER_AMP: level × value_4 × 100, a level-0 state adds nothing");
        DoubleSummonRules.NoticeDuration(true, 3100).Should().Be(0u, "an infinite state announces 0");
        DoubleSummonRules.NoticeDuration(false, 3100).Should().Be(3100u);
    }

    [Test]
    public void The_main_summon_is_the_one_formed_first_among_the_controlled_slots()
    {
        var slots = new long[] { 61, 60, 0, 0, 0, 0 };
        DoubleSummonRules.Order(slots, 60, 61, 3).Should().Be((61L, 60L), "slot 0 holds the new summon");
        DoubleSummonRules.Order(slots, 60, 61, 1).Should().Be((60L, 61L), "no slot searched: the pair is kept");
        DoubleSummonRules.Order(new long[] { 0, 0, 60, 61, 0, 0 }, 60, 61, 3).Should().Be((60L, 61L));
    }

    [Test]
    public void The_deadline_and_the_time_left_survive_the_tick_wrap()
    {
        DoubleSummonRules.IsDue(100, 100).Should().BeFalse("m_nNextUnSummonTime < t");
        DoubleSummonRules.IsDue(101, 100).Should().BeTrue();
        DoubleSummonRules.IsDue(5, uint.MaxValue - 5).Should().BeTrue();
        DoubleSummonRules.Remaining(100, 50).Should().Be(-50);
        DoubleSummonRules.LoginNotice(-50).Should().Be(0u);
        DoubleSummonRules.LoginNotice(300).Should().Be(300u);
    }

    [Test]
    public void A_second_summon_joins_the_main_one_with_its_notice_then_goes_back_when_its_time_is_up()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        BindPair(h);
        h.Info.LearnedSkills[DoubleSummonRules.TechnicalCreatureControlSkill] = 2;

        service.Summon(h.Client, 60).Should().BeTrue();
        h.Ids.Should().NotContain((ushort)GamePackets.TM_SC_UNSUMMON_NOTICE, "a lone summon has no time limit");
        service.Summon(h.Client, 61).Should().BeTrue();

        h.Info.Summons.Should().HaveCount(2, "the main summon stays out (NGemity sent it back)");
        h.Info.MainSummonCardId.Should().Be(60);
        h.Info.SubSummonCardId.Should().Be(61);
        var notice = LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE);
        notice.Should().HaveCount(15);
        BinaryPrimitives.ReadUInt32LittleEndian(notice.AsSpan(7)).Should().Be(h.Info.CreatureCards[61].SummonHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(notice.AsSpan(11)).Should().Be(2400u, "1000 + 2 × 7 × 100");
        service.CheckSummon(h.Info, 62, true).Should().Be(ResultCode.NotActable, "no third summon while a second is out");

        var subHandle = h.Info.CreatureCards[61].SummonHandle;
        service.ProcessSubSummons(h.Info.NextUnsummonTick);
        h.Info.Summons.Should().HaveCount(2, "not yet: the deadline itself is not past");
        service.ProcessSubSummons(h.Info.NextUnsummonTick + 1);

        h.Info.Summons.Should().ContainSingle().Which.Handle.Should().Be(h.Info.CreatureCards[60].SummonHandle);
        h.Info.SubSummonCardId.Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON).AsSpan(7)).Should().Be(subHandle);
        service.CheckSummon(h.Info, 62, true).Should().Be(ResultCode.Success);
    }

    [Test]
    public void The_summon_formed_first_becomes_the_main_one_and_the_other_gets_the_countdown()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        BindPair(h, first: 61, second: 60);

        service.Summon(h.Client, 60);
        service.Summon(h.Client, 61);

        h.Info.MainSummonCardId.Should().Be(61, "SetMainAndSubSummon: slot 0 wins");
        h.Info.SubSummonCardId.Should().Be(60);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE).AsSpan(7))
            .Should().Be(h.Info.CreatureCards[60].SummonHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE).AsSpan(11))
            .Should().Be(1000u);
    }

    [Test]
    public void The_main_summon_going_back_leaves_the_second_one_as_main_without_a_time_limit()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        BindPair(h);
        service.Summon(h.Client, 60);
        service.Summon(h.Client, 61);

        service.Unsummon(h.Client, 60).Should().BeTrue();
        h.Info.MainSummonCardId.Should().Be(61);
        h.Info.SubSummonCardId.Should().Be(0);

        service.ProcessSubSummons(h.Info.NextUnsummonTick + 100_000);
        h.Info.Summons.Should().ContainSingle("the former second summon is now the main one and stays");
    }

    [Test]
    public void An_infinite_summon_state_holds_the_second_summon_and_its_end_leaves_its_seconds()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        BindPair(h);
        h.Info.ActiveBuffs.Add(new ActiveBuff(1, InfiniteSummonState, 0, 1, 0, uint.MaxValue));
        service.Summon(h.Client, 60);
        service.Summon(h.Client, 61);

        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE).AsSpan(11))
            .Should().Be(0u, "an infinite state announces 0");
        var now = h.Info.NextUnsummonTick + 50_000;
        service.ProcessSubSummons(now);
        h.Info.Summons.Should().HaveCount(2, "the state holds it past its deadline");

        h.Info.ActiveBuffs.Clear();
        service.ProcessSubSummons(now);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE).AsSpan(11))
            .Should().Be(300u, "onAfterRemoveState: value_0 × 100");
        h.Info.NextUnsummonTick.Should().Be(now + 300);
        service.ProcessSubSummons(now + 300);
        h.Info.Summons.Should().HaveCount(2);
        service.ProcessSubSummons(now + 301);
        h.Info.Summons.Should().ContainSingle();
    }

    [Test]
    public void A_creature_parameter_state_lengthens_the_double_summon()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        BindPair(h);
        h.Info.ActiveBuffs.Add(new ActiveBuff(1, CreatureAmpState, 0, 2, 0, uint.MaxValue));
        service.Summon(h.Client, 60);
        service.Summon(h.Client, 61);

        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE).AsSpan(11))
            .Should().Be(3000u, "1000 + 2 × 10 × 100");
    }

    [Test]
    public async Task World_entry_brings_the_second_summon_back_with_what_is_left_of_its_time()
    {
        var h = new Harness();
        var service = DoubleSummonService(h);
        ItemEntity Card(long id) => new() { Id = id, ItemResourceId = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
        SummonEntity Summon(long id, long card) => new() { Id = id, SummonResourceId = SummonId, CardItemId = card,
            Name = "RossParr", Lv = 1 };
        A.CallTo(() => h.Characters.GetCreatureStateAsync("Ana", A<IReadOnlyCollection<int>>._))
            .Returns(new CreatureState(new[]
                {
                    new CreatureCardRecord(Card(60), Summon(9, 60)), new CreatureCardRecord(Card(61), Summon(10, 61))
                }, new long[] { 60, 61, 0, 0, 0, 0 }, 9) { SubSummonId = 10, RemainSummonTime = 500 });
        h.Info.CreatureCards.Clear();

        await service.OnWorldEntryAsync(h.Client);

        h.Info.Summons.Should().HaveCount(2);
        h.Info.MainSummonCardId.Should().Be(60, "the main summon brought back is the main one again");
        h.Info.SubSummonCardId.Should().Be(61);
        var notice = LastOf(h, GamePackets.TM_SC_UNSUMMON_NOTICE);
        BinaryPrimitives.ReadUInt32LittleEndian(notice.AsSpan(7)).Should().Be(h.Info.CreatureCards[61].SummonHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(notice.AsSpan(11)).Should().Be(500u);

        service.OnWorldExit(h.Client);
        A.CallTo(() => h.Characters.SaveSubSummonAsync("Ana", 10, A<int>.That.Matches(t => t > 0 && t <= 500)))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_bound_card_leaving_the_bag_sends_302_and_one_coming_in_sends_301()
    {
        var h = new Harness();
        Bind(h);
        var incoming = new ItemEntity { Id = 70, ItemResourceId = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
        var blank = new ItemEntity { Id = 50, ItemResourceId = CardId, Amount = 2 };

        h.Service.ApplyCardChanges(h.Client, new[]
        {
            new CreatureCardRecord(blank, null),
            new CreatureCardRecord(incoming, new SummonEntity { Id = 12, SummonResourceId = SummonId, CardItemId = 70,
                Name = "Traded", Lv = 4 })
        }, new[] { new SummonSkillRecord(12, 40011, 3) });

        var removal = LastOf(h, GamePackets.TM_SC_REMOVE_SUMMON_INFO);
        removal.Should().HaveCount(11);
        BinaryPrimitives.ReadUInt32LittleEndian(removal.AsSpan(7)).Should().Be(60u, "302 names the card that left");
        h.Info.CreatureCards.Should().NotContainKey(60);
        h.Info.SummonSlots.Should().Equal(0, 0, 0, 0, 0, 0);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_EQUIP_SUMMON).AsSpan(8)).Should().Be(0u);

        h.Info.CreatureCards[50].Amount.Should().Be(2, "a blank stack only changes its amount");
        h.Info.CreatureCards[70].Skills.Should().ContainKey(40011);
        BinaryPrimitives.ReadUInt32LittleEndian(LastOf(h, GamePackets.TM_SC_ADD_SUMMON_INFO).AsSpan(7))
            .Should().Be(70u, "301 brings the summon of the card that came in");
        h.Ids.Count(id => id == (ushort)GamePackets.TM_SC_REMOVE_SUMMON_INFO).Should().Be(1, "the blank card has no summon");
    }

    [Test]
    public async Task A_traded_card_brings_its_summon_row_and_the_second_summon_is_saved()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, AccountId = 4, AccountName = "A", CharacterName = "Ana",
                Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() });
            // Bob holds the card (a trade moved the item row), the summon row still names Ana.
            db.Characters.Add(new CharacterEntity { Id = 2, AccountId = 5, AccountName = "B", CharacterName = "Bob",
                Items = new List<ItemEntity> { new() { Id = 70, ItemResourceId = CardId, Amount = 1, Idx = 1 } },
                Skills = new List<CharacterSkillEntity>() });
            db.Summons.Add(new SummonEntity { Id = 12, AccountId = 4, CharacterId = 1, CardItemId = 70,
                SummonResourceId = SummonId, Name = "Traded" });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);

        var state = await service.GetCreatureStateAsync("Bob", new[] { CardId });
        state.Cards.Single().Summon.Should().NotBeNull("the summon follows its card (onAdd -> AddSummon)");
        await using (var db = new TelecasterContext(options))
        {
            var row = await db.Summons.SingleAsync(s => s.Id == 12);
            row.CharacterId.Should().Be(2);
            row.AccountId.Should().Be(5);
        }

        (await service.SaveSubSummonAsync("Bob", 12, 420)).Should().BeTrue();
        state = await service.GetCreatureStateAsync("Bob", new[] { CardId });
        state.SubSummonId.Should().Be(12);
        state.RemainSummonTime.Should().Be(420);
        (await service.SaveSubSummonAsync("Bob", null, 420)).Should().BeTrue();
        (await service.GetCreatureStateAsync("Bob", new[] { CardId })).RemainSummonTime.Should().Be(0);
    }
}
