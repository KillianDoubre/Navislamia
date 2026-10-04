using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The official StructFieldProp/FieldPropManager life of a world prop (socle-props.md).</summary>
[TestFixture]
public class FieldPropLifecycleTests
{
    /// <summary>Prop 1 is always there; 2 has two uses and returns after 10 s; 3 lives 5 s and returns after 2 s.</summary>
    private static FieldPropCatalog Catalog(params FieldPropActivationOptions[] activations) => new(new FieldPropOptions
    {
        Templates =
        {
            new FieldPropTemplateOptions { Id = 1, ActivateSkillId = 6904, Script = "common_warp_gate(100, 100)" },
            new FieldPropTemplateOptions
            {
                Id = 2, ActivateSkillId = 6904, UseCount = 2, RegenTime = 1_000, CastingTime = 300,
                Activations = activations.ToList(),
                Drops = { new FieldPropDropOptions { ItemId = 1100304, Ratio = FieldPropUse.DropScale, CountMin = 2, CountMax = 2 } },
                LuaScript = "quest_prop_wastes()"
            },
            new FieldPropTemplateOptions { Id = 3, ActivateSkillId = 6904, LifeTime = 500, RegenTime = 200 }
        },
        Spawns =
        {
            new FieldPropSpawnOptions { PropId = 1, X = 10, Y = 10 },
            new FieldPropSpawnOptions { PropId = 2, X = 20, Y = 20 },
            new FieldPropSpawnOptions { PropId = 3, X = 30, Y = 30 }
        }
    });

    private uint _now = 10_000;

    [Test]
    public void A_prop_with_a_regen_time_first_appears_after_it_and_an_untracked_one_is_always_there()
    {
        var states = new FieldPropStates(Catalog(), () => _now);

        states.IsPresent(0).Should().BeTrue();
        states.IsPresent(1).Should().BeFalse("RegisterFieldProp pends every prop for its regen time");
        states.Tick(_now + 999).Select(p => p.InstanceId).Should().NotContain(1);
        states.Tick(_now + 1_000).Select(p => p.InstanceId).Should().Contain(1);
        states.IsPresent(1).Should().BeTrue();
    }

    [Test]
    public void A_limited_prop_leaves_at_its_last_use_and_returns_after_its_regen_time()
    {
        var states = new FieldPropStates(Catalog(), () => _now);
        states.Tick(_now += 1_000);

        states.TryUse(1, out var removed).Should().BeTrue();
        removed.Should().BeFalse();
        states.TryUse(1, out removed).Should().BeTrue();
        removed.Should().BeTrue();
        states.IsPresent(1).Should().BeFalse();
        states.TryUse(1, out _).Should().BeFalse("gone until it returns");

        states.Tick(_now + 999).Select(p => p.InstanceId).Should().NotContain(1);
        states.Tick(_now + 1_000).Select(p => p.InstanceId).Should().Contain(1);
        states.TryUse(1, out _).Should().BeTrue("back with its two uses");
    }

    [Test]
    public void A_prop_with_a_life_time_expires_then_comes_back()
    {
        var states = new FieldPropStates(Catalog(), () => _now);
        states.Tick(_now += 200);
        states.IsPresent(2).Should().BeTrue();

        states.Tick(_now + 500).Should().BeEmpty("the life time ends strictly after 500 ticks");
        states.Tick(_now + 501).Select(p => p.InstanceId).Should().Equal(2);
        states.IsPresent(2).Should().BeFalse();
        states.Tick(_now + 701).Select(p => p.InstanceId).Should().Equal(2);
    }

    [Test]
    public void One_player_at_a_time_casts_at_a_prop()
    {
        var states = new FieldPropStates(Catalog(), () => _now);
        states.Tick(_now += 1_000);
        var ana = Client();
        var bob = Client();

        states.TryBeginCast(1, ana, _now + 300).Should().BeTrue();
        states.TryBeginCast(1, bob, _now + 300).Should().BeFalse("m_bIsCasting");
        states.EndCast(1, ana);
        states.TryBeginCast(1, bob, _now + 300).Should().BeTrue();
        _now += 301;
        states.TryBeginCast(1, ana, _now + 300).Should().BeTrue("a cast that never ended stops holding the prop");
    }

    private static GameClient Client()
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Ana";
        info.CharacterHandle = 7;
        return client;
    }

    [Test]
    public async Task A_use_gives_the_drops_runs_the_script_and_streams_the_spot_again_when_used_up()
    {
        var catalog = Catalog();
        var states = new FieldPropStates(catalog, () => _now);
        states.Tick(_now += 1_000);
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.AddItemAsync("Ana", 1100304, 2)).Returns(new ItemEntity { Id = 55, ItemResourceId = 1100304, Amount = 2 });
        var fieldProps = A.Fake<IFieldPropService>();
        var scripts = A.Fake<INpcScriptService>();
        var use = new FieldPropUse(states, catalog, characters, fieldProps, scripts: scripts, random: new Random(1));
        catalog.TryGetTemplate(2, out var template);
        var client = Client();

        use.Use(client, 1, template).Should().BeTrue();
        use.Use(client, 1, template).Should().BeTrue();
        use.Use(client, 1, template).Should().BeFalse("used up");
        await Task.Delay(50);

        A.CallTo(() => characters.AddItemAsync("Ana", 1100304, 2)).MustHaveHappenedTwiceExactly();
        A.CallTo(() => scripts.RunPropScriptAsync(client, "quest_prop_wastes()")).MustHaveHappenedTwiceExactly();
        A.CallTo(() => fieldProps.Refresh(20, 20)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task The_awaited_conditions_follow_IsUsable()
    {
        var catalog = Catalog(
            new FieldPropActivationOptions { Condition = FieldPropUse.ConditionItem, Value1 = 540073, Value2 = 2 },
            new FieldPropActivationOptions { Condition = FieldPropUse.ConditionQuest, Value1 = 3218, Value2 = 1 },
            new FieldPropActivationOptions { Condition = FieldPropUse.ConditionWorn, Value1 = 101, Value2 = 1 });
        var characters = A.Fake<ICharacterService>();
        var quests = A.Fake<IQuestService>();
        var items = new List<ItemEntity>
        {
            new() { ItemResourceId = 540073, Amount = 2, Idx = 1, WearInfo = ItemWearType.None },
            new() { ItemResourceId = 101, Amount = 1, Idx = 2, WearInfo = ItemWearType.Weapon }
        };
        A.CallTo(() => characters.GetCarriedItemsAsync("Ana")).ReturnsLazily(() => items.ToArray());
        A.CallTo(() => quests.GetQuestProgressAsync(A<GameClient>._, 3218)).Returns(1);
        var use = new FieldPropUse(new FieldPropStates(catalog, () => _now), catalog, characters, quests: quests);
        catalog.TryGetTemplate(2, out var template);
        var client = Client();

        use.NeedsConditions(template).Should().BeTrue();
        (await use.CheckConditionsAsync(client, template)).Should().BeTrue();

        A.CallTo(() => quests.GetQuestProgressAsync(A<GameClient>._, 3218)).Returns(255);
        (await use.CheckConditionsAsync(client, template)).Should().BeFalse("the quest progress must be the value");
        A.CallTo(() => quests.GetQuestProgressAsync(A<GameClient>._, 3218)).Returns(1);
        items[0].Amount = 1;
        (await use.CheckConditionsAsync(client, template)).Should().BeFalse("FindItem(code)->GetCount() < 2");
        items[0].Amount = 2;
        items[1].WearInfo = ItemWearType.None;
        (await use.CheckConditionsAsync(client, template)).Should().BeFalse("the item must be worn");
    }

    [Test]
    public void The_skill_condition_wants_at_least_the_level()
    {
        var template = new FieldPropTemplate(9, 6904, 0, 0, 0, 0, 0, PropAction.None,
            new[] { new PropActivation(3, 1001, 2) });
        var info = new ConnectionInfo();
        info.LearnedSkills[1001] = 3;
        FieldPropUsage.IsUsable(template, info).Should().BeTrue("CHECK_TYPE_SKILL: base level >= value");
        info.LearnedSkills[1001] = 1;
        FieldPropUsage.IsUsable(template, info).Should().BeFalse();
    }
}
