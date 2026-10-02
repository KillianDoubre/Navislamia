using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Death;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// A player's death and the ways back (docs/packet-specs/socle-mort-joueur.md): the retained experience and the
/// share each resurrection gives back, the official potions, the resurrection of another player, and the items a
/// PK server drops.
/// </summary>
[TestFixture]
public class DeathResurrectionTests
{
    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static (LevelingService Leveling, GameClient Client, ConnectionInfo Info,
        StorageTestHarness.FrameConnection Connection) Leveling()
    {
        var levels = A.Fake<ILevelResourceRepository>();
        A.CallTo(() => levels.GetAll()).Returns(Enumerable.Range(1, 10)
            .Select(level => new LevelResourceEntity { Level = level, NormalExp = level * 100L, JLvs = new int[4] })
            .ToList());
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxHp = 500, MaxMp = 50 }, new StatBlock()));
        var leveling = new LevelingService(levels, stats, A.Fake<IRateService>());

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 7;
        info.CharacterLevel = 5;
        info.CharacterExp = 405;
        return (leveling, client, info, connection);
    }

    [Test]
    public void The_death_keeps_its_loss_and_a_resurrection_gives_a_share_back_once()
    {
        var (leveling, client, info, connection) = Leveling();

        leveling.ApplyDeathPenalty(client).Should().Be(19);
        info.DeathExpLoss.Should().Be(19);
        info.CharacterLevel.Should().Be(4);

        leveling.RestoreDeathExperience(client, 1m).Should().Be(19);
        info.CharacterExp.Should().Be(405);
        info.CharacterLevel.Should().Be(5, "the level comes back with the experience");
        info.DeathExpLoss.Should().Be(0);
        connection.Sent.Select(Id).Last().Should().Be((ushort)GamePackets.TM_SC_EXP_UPDATE);

        leveling.RestoreDeathExperience(client, 1m).Should().Be(0, "the loss is given back once");
    }

    [Test]
    public void A_partial_ratio_gives_back_its_share_and_forgets_the_rest()
    {
        var (leveling, client, info, _) = Leveling();
        leveling.ApplyDeathPenalty(client);

        leveling.RestoreDeathExperience(client, 0.3m).Should().Be(5, "19 x 0.3 = 5.7, floored");
        info.DeathExpLoss.Should().Be(0);
    }

    [Test]
    public void A_pk_server_takes_twice_the_experience()
    {
        var (_, client, info, _) = Leveling();
        var levels = A.Fake<ILevelResourceRepository>();
        A.CallTo(() => levels.GetAll()).Returns(Enumerable.Range(1, 10)
            .Select(level => new LevelResourceEntity { Level = level, NormalExp = level * 100L, JLvs = new int[4] })
            .ToList());
        var rules = A.Fake<IOptionsMonitor<GameRuleOptions>>();
        A.CallTo(() => rules.CurrentValue).Returns(new GameRuleOptions { PkServer = true });
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxHp = 500, MaxMp = 50 }, new StatBlock()));
        var pk = new LevelingService(levels, stats, A.Fake<IRateService>(), rules);

        // 500 x (0.3 / 4 + 0.001) = 38.
        pk.ApplyDeathPenalty(client).Should().Be(38);
        info.DeathExpLoss.Should().Be(38);
    }

    // ---- the rules ----

    [Test]
    public void The_skills_give_back_var2_or_var4_plus_var5_of_the_experience()
    {
        var vars = new decimal[] { 0.2m, 0.1m, 0.09m, 0.05m, 0.15m, 0.01m };

        ResurrectionRules.SkillExpRatio(SkillEffectType.Resurrection, vars, 2).Should().Be(0.18m);
        ResurrectionRules.SkillExpRatio(SkillEffectType.ResurrectionWithRecover, vars, 5).Should().Be(0.20m);
    }

    [Test]
    public void A_state_gives_back_value_4_plus_value_5_and_a_potion_its_share_of_the_hp()
    {
        ResurrectionStateValues.From(new[] { 0.1m, 0m, 0m, 0m, 0.5m, 0.1m }).ExpRatio(2).Should().Be(0.7m);
        ResurrectionRules.PotionHp(1m, 1234f).Should().Be(1234);
        ResurrectionRules.PotionHp(0.001m, 100f).Should().Be(1, "at least one HP");
    }

    [Test]
    public void The_potions_are_the_four_official_codes_in_their_order()
    {
        ItemEffectFields Potion(int id, decimal hp, decimal exp) => new(id, default, new short[4], new decimal[4],
            new decimal[4], new short[] { 114, 0, 0, 0 }, new[] { hp, 0m, 0m, 0m }, new[] { exp, 0m, 0m, 0m });

        var potions = ResurrectionItemCatalog.BuildPotions(new[]
        {
            Potion(910004, 1m, 0.3m), Potion(2010454, 1m, 1m), Potion(555, 1m, 1m)
        });

        potions.Select(p => p.ItemResourceId).Should().Equal(2010454, 910004);
        potions[1].ExpRatio.Should().Be(0.3m);
    }

    [Test]
    public void A_resurrection_on_a_character_is_its_own_kind_and_the_creature_scroll_is_not()
    {
        CastableSkillRow Row(bool onCharacter) => new(6001, 504, false, 1, null, 0, new decimal[20], 0m, 0m, 0, 0m,
            0, 0, 0m, 0m, 0m, 0m, 0m, 0, UseOnCharacter: onCharacter);

        BuffCatalog.TryClassify(Row(true), out var fields).Should().BeTrue();
        fields.Kind.Should().Be(SkillCastKind.Resurrection);
        BuffCatalog.TryClassify(Row(false), out _).Should().BeFalse("6013 resurrects summons only");
    }

    [Test]
    public void The_rebirth_hit_carries_hp_mp_and_experience()
    {
        var hit = new SkillHit(SkillHitType.Rebirth, 42, 100, 100, IncMp: 20, RecoveryExp: 7, TargetMp: 30);
        var packet = GameSkillPackets.BuildSkill(6001, 1, 1, 42, 0f, 0f, 0f, 0, SkillPacketType.Fire, 0, 0, 10, 10,
            0, 0, hit);

        var record = packet.AsSpan(7 + 41 + 9);
        record[0].Should().Be(23);
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(1, 4)).Should().Be(42u);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(5, 4)).Should().Be(100);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(13, 4)).Should().Be(20);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(17, 4)).Should().Be(7);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(21, 4)).Should().Be(30);
    }

    // ---- another player ----

    private static (SkillCastService Service, GameClient Caster, GameClient Dead, ILevelingService Leveling)
        Resurrector(CastableBuffFields fields)
    {
        var catalog = A.Fake<IBuffCatalog>();
        A.CallTo(() => catalog.Count).Returns(1);
        CastableBuffFields ignored;
        A.CallTo(() => catalog.TryGet(fields.SkillId, out ignored)).Returns(true).AssignsOutAndRefParameters(fields);
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxHp = 1000f, MaxMp = 200f }, new StatBlock()));

        var players = A.Fake<IPlayerVisibilityService>();
        var registry = new PlayerRegistry();
        A.CallTo(() => players.Registry).Returns(registry);
        var leveling = A.Fake<ILevelingService>();
        A.CallTo(() => leveling.RestoreDeathExperience(A<GameClient>._, A<decimal>._)).Returns(12L);

        var repository = A.Fake<IMonsterResourceRepository>();
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
        var service = new SkillCastService(catalog, stats, A.Fake<IStateCatalog>(), world, A.Fake<ICombatService>(),
            A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), players, runTicks: false, leveling: leveling);

        GameClient Player(uint handle, int hp)
        {
            var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            var info = StorageTestHarness.Session(client);
            info.CharacterHandle = handle;
            info.CharacterHp = hp;
            info.CharacterMp = 10;
            registry.Register(handle, client);
            return client;
        }

        var caster = Player(1, 500);
        var dead = Player(2, 0);
        lock (StorageTestHarness.Session(caster).PlayerVisibilityLock)
        {
            StorageTestHarness.Session(caster).SpawnedPlayers[2] = 2;
        }

        return (service, caster, dead, leveling);
    }

    private static CastableBuffFields Scroll() =>
        new(6001, SkillCastKind.Resurrection, 0, 0, new[] { 0.1m, 0m, 0.05m, 0m, 0m, 0m }, 0m, 0m, 0, 0m, 0, 0, 0m,
            0m, 0m, 0m, 0m, 0, CastRange: 20, EffectType: 504);

    [Test]
    public void The_scroll_brings_a_dead_player_in_sight_back()
    {
        var (service, caster, dead, leveling) = Resurrector(Scroll());

        service.CheckItemSkillTarget(caster, 6001, 2).Should().Be(ResultCode.Success);
        service.ApplyItemSkill(caster, 6001, 1, 2).Should().BeTrue();

        StorageTestHarness.Session(dead).CharacterHp.Should().Be(100, "10 % of 1 000");
        A.CallTo(() => leveling.RestoreDeathExperience(dead, 0.05m)).MustHaveHappenedOnceExactly();
        var fire = ((StorageTestHarness.FrameConnection)caster.Connection).Sent
            .First(p => Id(p) == (ushort)GamePackets.TM_SC_SKILL);
        fire.AsSpan(7 + 41 + 9)[0].Should().Be((byte)SkillHitType.Rebirth);
    }

    [Test]
    public void A_living_unseen_or_own_target_is_refused_before_the_scroll_is_spent()
    {
        var (service, caster, dead, _) = Resurrector(Scroll());

        service.CheckItemSkillTarget(caster, 6001, 1).Should().Be(ResultCode.NotExist, "oneself");
        service.CheckItemSkillTarget(caster, 6001, 99).Should().Be(ResultCode.NotExist, "unseen");
        StorageTestHarness.Session(dead).CharacterHp = 5;
        service.CheckItemSkillTarget(caster, 6001, 2).Should().Be(ResultCode.NotActable, "alive");
    }

    // ---- the PK server's items ----

    [Test]
    public void Only_a_pk_server_rolls_the_items_and_the_protection_item_spares_them()
    {
        DeathDropService.Rolls(29).Should().BeTrue();
        DeathDropService.Rolls(30).Should().BeFalse();
    }

    [Test]
    public async Task A_regular_server_drops_nothing()
    {
        var characters = A.Fake<ICharacterService>();
        var rules = A.Fake<IOptionsMonitor<GameRuleOptions>>();
        A.CallTo(() => rules.CurrentValue).Returns(new GameRuleOptions());
        var ground = A.Fake<IGroundItemService>();
        var service = new DeathDropService(characters, A.Fake<IEquipmentService>(), ground, rules);

        await service.DropOnDeathAsync(StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>())));

        A.CallTo(() => characters.GetCarriedItemsAsync(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_pk_server_drops_one_bag_item_unless_the_protection_item_is_carried()
    {
        var characters = A.Fake<ICharacterService>();
        var bag = new[] { new ItemEntity { Id = 50, ItemResourceId = 1000, Amount = 3, WearInfo = ItemWearType.None } };
        A.CallTo(() => characters.GetCarriedItemsAsync(A<string>._)).Returns(bag);
        var rules = A.Fake<IOptionsMonitor<GameRuleOptions>>();
        A.CallTo(() => rules.CurrentValue).Returns(new GameRuleOptions { PkServer = true });
        var ground = A.Fake<IGroundItemService>();
        var random = A.Fake<ICombatRandom>();
        // Worn roll fails (998 + 1), bag roll succeeds (0 + 1), first bag item.
        A.CallTo(() => random.Next(A<int>._)).ReturnsNextFromSequence(998, 0, 0);
        var service = new DeathDropService(characters, A.Fake<IEquipmentService>(), ground, rules, random);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));

        await service.DropOnDeathAsync(client);
        A.CallTo(() => ground.DropFromInventoryAsync(client, 50u, 3)).MustHaveHappenedOnceExactly();

        A.CallTo(() => characters.GetCarriedItemsAsync(A<string>._)).Returns(bag.Append(
            new ItemEntity { Id = 51, ItemResourceId = DeathDropService.ProtectionItem, Amount = 1 }).ToArray());
        A.CallTo(() => random.Next(A<int>._)).ReturnsNextFromSequence(0, 0, 0, 0);
        await service.DropOnDeathAsync(client);
        A.CallTo(() => ground.DropFromInventoryAsync(A<GameClient>._, A<uint>._, A<int>._)).MustHaveHappenedOnceExactly();
    }
}
