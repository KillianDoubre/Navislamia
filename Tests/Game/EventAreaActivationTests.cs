using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// <c>EVENT_AREA_INFO::IsActivatable</c> (<c>GameContent.cpp:72-230</c>) condition by condition (Trello qnvX3BU6,
/// socle-zones-evenement-lua.md).
/// </summary>
[TestFixture]
public class EventAreaActivationTests
{
    private GameClient _client;
    private ConnectionInfo _info;
    private CharacterEntity _character;
    private Dictionary<string, string> _flags;
    private readonly DateTimeOffset _noon = new(2026, 10, 5, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 5, 12, 0, 0)));

    [SetUp]
    public void SetUp()
    {
        _client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        _info = StorageTestHarness.Session(_client);
        _info.CharacterLevel = 20; _info.CharacterHp = 100; _info.CharacterRace = 4; _info.CharacterJob = 200;
        _character = new CharacterEntity { Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() };
        _flags = new Dictionary<string, string>();
    }

    private static EventAreaResourceEntity Row(int condition = 0, int code = 0, int value = 0) => new()
    {
        Id = 1, MinLevel = 1, MaxLevel = 300,
        Conditions = condition == 0 ? Array.Empty<int>() : new[] { condition },
        Values = condition == 0 ? Array.Empty<int>() : new[] { code, value }
    };

    private bool Allows(EventAreaResourceEntity row, IQuestService quests = null, DateTimeOffset? at = null) =>
        EventAreaActivation.Allows(row, _client, _character, _flags, at ?? _noon, quests);

    [Test]
    public void The_time_window_is_seconds_in_the_local_day()
    {
        var row = Row();
        row.BeginTime = 13 * 3600; row.EndTime = 14 * 3600;
        Allows(row).Should().BeFalse("noon is before 13:00");
        Allows(row, at: _noon.AddMinutes(90)).Should().BeTrue();
        Allows(row, at: _noon.AddHours(3)).Should().BeFalse("15:00 is after 14:00");
        row.BeginTime = 0; row.EndTime = 0;
        Allows(row).Should().BeTrue("0 means no bound");
    }

    [Test]
    public void Quest_status_must_equal_the_value_the_only_condition_of_the_epic_7_data()
    {
        var quests = A.Fake<IQuestService>();
        A.CallTo(() => quests.GetQuestProgressAsync(_client, 2011)).Returns(Task.FromResult(1));
        Allows(Row(2, 2011, 1), quests).Should().BeTrue();
        Allows(Row(2, 2011, 2), quests).Should().BeFalse("GetQuestProgress(code) != value refuses");
    }

    [Test]
    public void Skill_level_is_at_least_the_value()
    {
        _character.Skills.Add(new CharacterSkillEntity { SkillId = 1011, Level = 3 });
        Allows(Row(3, 1011, 3)).Should().BeTrue();
        Allows(Row(3, 1011, 4)).Should().BeFalse();
        Allows(Row(3, 1012, 1)).Should().BeFalse("a skill not learned");
    }

    [Test]
    public void Worn_item_summon_and_state_read_1_as_present_and_2_as_absent()
    {
        _character.Items.Add(new ItemEntity { ItemResourceId = 500, Amount = 1, WearInfo = ItemWearType.Weapon });
        Allows(Row(4, 500, 1)).Should().BeTrue();
        Allows(Row(4, 500, 2)).Should().BeFalse();
        Allows(Row(4, 501, 2)).Should().BeTrue();

        Allows(Row(5, 2201, 1)).Should().BeFalse("no summon out");
        _info.Summons = new[] { new SummonPresence(9, new SummonWorldEntry { Code = 2201, Hp = 10, MaxHp = 10 }, 0, 0, 0) };
        Allows(Row(5, 2201, 1)).Should().BeTrue();
        Allows(Row(5, 2201, 2)).Should().BeFalse();

        _info.ActiveBuffs.Add(new ActiveBuff(1, 1011, 0, 1, 0, uint.MaxValue));
        Allows(Row(6, 1011, 1)).Should().BeTrue();
        Allows(Row(6, 1011, 2)).Should().BeFalse();
        Allows(Row(6, 1012, 2)).Should().BeTrue();
    }

    [Test]
    public void Item_count_reads_the_first_stack_and_an_unknown_condition_is_ignored()
    {
        _character.Items.Add(new ItemEntity { ItemResourceId = 7, Amount = 1, Idx = 1 });
        _character.Items.Add(new ItemEntity { ItemResourceId = 7, Amount = 5, Idx = 2 });
        Allows(Row(1, 7, 2)).Should().BeFalse("FindItem returns the first stack, 1 < 2");
        Allows(Row(9, 7, 2)).Should().BeTrue("IsActivatable has no case for 9");
    }

    [Test]
    public void A_race_or_job_outside_the_official_lists_is_not_refused()
    {
        var row = Row();
        row.RaceJobLimit = 2L | 1L << 17; // Deva, and the Deva guide (job 200 is the 15th listed job)
        Allows(row).Should().BeTrue();
        _info.CharacterJob = 201;
        Allows(row).Should().BeFalse("201 is listed and its bit is not set");
        _info.CharacterJob = 999;
        Allows(row).Should().BeTrue("the job switch has no default");
        _info.CharacterRace = 1;
        Allows(row).Should().BeTrue("the race switch has no default");
    }
}
