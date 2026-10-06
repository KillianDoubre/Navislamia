using System;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>Which NPCs a server shows: the official onNPCData and NPCRespawnObject (socle-pnj-pays-periodes.md).</summary>
[TestFixture]
public class NpcSpawnRulesTests
{
    private const int Korea = 1;
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static NpcResourceEntity Npc(long id, int localFlag = 0, bool periodic = false, DateTime begin = default,
        DateTime end = default) => new()
    {
        Id = id, X = 1000, Y = 1000, LocalFlag = localFlag, IsPeriodic = periodic, BeginOfPeriod = begin, EndOfPeriod = end
    };

    [Test]
    public void The_local_flag_excludes_the_countries_it_names()
    {
        NpcSpawnRules.IsLoaded(Npc(1), Korea, true, Now).Should().BeTrue("0 is every country");
        NpcSpawnRules.IsLoaded(Npc(2, localFlag: 0xFFFFF), Korea, true, Now).Should().BeFalse("excluded everywhere");
        NpcSpawnRules.IsLoaded(Npc(3, localFlag: 0xFFFFE), Korea, true, Now).Should().BeTrue("Korea alone keeps it");
        NpcSpawnRules.IsLoaded(Npc(4, localFlag: 0xFFFEF), Korea, true, Now).Should().BeFalse("Japan alone keeps it");
    }

    [Test]
    public void The_server_kind_bits_keep_test_npcs_off_a_live_server_and_the_reverse()
    {
        var notOnLive = Npc(1, NpcSpawnRules.ExcludeServiceServer);
        var notOnTest = Npc(2, NpcSpawnRules.ExcludeTestServer);
        NpcSpawnRules.IsLoaded(notOnLive, Korea, serviceServer: true, Now).Should().BeFalse();
        NpcSpawnRules.IsLoaded(notOnLive, Korea, serviceServer: false, Now).Should().BeTrue();
        NpcSpawnRules.IsLoaded(notOnTest, Korea, serviceServer: true, Now).Should().BeTrue();
        NpcSpawnRules.IsLoaded(notOnTest, Korea, serviceServer: false, Now).Should().BeFalse();
    }

    [Test]
    public void A_periodic_npc_stands_only_inside_its_period()
    {
        var over = Npc(1, periodic: true, begin: new DateTime(2011, 3, 30), end: new DateTime(2011, 5, 3));
        NpcSpawnRules.IsLoaded(over, Korea, true, Now).Should().BeFalse("an elapsed event is not loaded at all");

        var event_ = Npc(2, periodic: true, begin: Now.AddDays(1), end: Now.AddDays(8));
        NpcSpawnRules.IsLoaded(event_, Korea, true, Now).Should().BeTrue();
        NpcSpawnRules.IsPresent(event_, Now).Should().BeFalse("not yet begun");
        NpcSpawnRules.IsPresent(event_, Now.AddDays(2)).Should().BeTrue();
        NpcSpawnRules.IsPresent(event_, Now.AddDays(8)).Should().BeFalse("over");
        NpcSpawnRules.IsPresent(Npc(3), Now).Should().BeTrue("a regular NPC always stands");
    }

    [Test]
    public void The_client_flags_retire_the_events_the_server_table_still_enables()
    {
        // 11808 (2012 Pepero event) is a 9.4-only row with local_flag 0 in Arcadia; the 7.3 client excludes it everywhere.
        NpcSpawnRules.ClientFlags.Should().HaveCount(1185);
        var pepero = Npc(11808);
        NpcSpawnRules.IsLoaded(pepero, Korea, true, Now).Should().BeTrue("the server table alone keeps it");
        pepero.LocalFlag = NpcSpawnRules.EffectiveFlag(pepero, NpcSpawnRules.ClientFlags);
        NpcSpawnRules.IsLoaded(pepero, Korea, true, Now).Should().BeFalse();

        var yurie = Npc(7005);
        yurie.LocalFlag = NpcSpawnRules.EffectiveFlag(yurie, NpcSpawnRules.ClientFlags);
        NpcSpawnRules.IsLoaded(yurie, Korea, true, Now).Should().BeTrue("the Hidden Village teleporter stays");
    }

    [Test]
    public void The_spawn_service_streams_only_the_npcs_the_rules_allow()
    {
        var repository = A.Fake<INpcResourceRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new[]
        {
            Npc(1), Npc(2, localFlag: 0xFFFFF), Npc(3, NpcSpawnRules.ExcludeServiceServer),
            Npc(4, periodic: true, begin: Now.AddDays(-1), end: Now.AddHours(1)),
            Npc(5, periodic: true, begin: new DateTime(2010, 1, 1), end: new DateTime(2010, 2, 1)),
        });
        var clock = Now;
        var service = new NpcSpawnService(repository, Options.Create(new GameRuleOptions()), () => clock);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.X = 1000; info.Y = 1000;

        service.Sync(client);
        info.SpawnedNpcs.Keys.Should().BeEquivalentTo(new long[] { 1, 4 });

        clock = Now.AddHours(2);
        service.Sync(client);
        info.SpawnedNpcs.Keys.Should().BeEquivalentTo(new long[] { 1 }, "the event NPC leaves the view once its period is over");
    }
}
