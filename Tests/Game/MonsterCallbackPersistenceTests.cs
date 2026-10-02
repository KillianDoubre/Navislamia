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
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class MonsterCallbackPersistenceTests
{
    [Test]
    public async Task Auto_flag_survives_new_contexts_and_clears_the_account_only()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.AddRange(
                new CharacterEntity { Id = 1, AccountName = "A", CharacterName = "One", Race = (int)Navislamia.Game.DataAccess.Entities.Enums.Race.Gaia, Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() },
                new CharacterEntity { Id = 2, AccountName = "A", CharacterName = "Two", AutoUsed = true, Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() },
                new CharacterEntity { Id = 3, AccountName = "B", CharacterName = "Other", AutoUsed = true });
            await db.SaveChangesAsync();
        }
        CharacterService Service() => new(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);
        (await Service().SetAutoUsedAsync("A", "One", true)).Should().BeTrue();
        (await Service().GetCharacterByNameAsync("One")).AutoUsed.Should().BeTrue();
        (await Service().SetAutoUsedAsync("A", "One", false)).Should().BeTrue();
        (await Service().GetCharacterByNameAsync("One")).AutoUsed.Should().BeFalse();
        (await Service().GetCharacterByNameAsync("Two")).AutoUsed.Should().BeFalse();
        (await Service().GetCharacterByNameAsync("Other")).AutoUsed.Should().BeTrue();
        (await Service().SetAutoUsedAsync("A", "Other", false)).Should().BeFalse();
        (await Service().GetCharacterByNameAsync("Other")).AutoUsed.Should().BeTrue();
        (await Service().SetAutoUsedAsync("A", "Two", true)).Should().BeTrue();
        (await Service().GetCharacterForWorldEntryAsync("A", "One")).AutoUsed.Should().BeTrue();
        // Account detection on entry is a session value, not an accidental write of the other row.
        (await Service().GetCharacterByNameAsync("One")).AutoUsed.Should().BeFalse();
    }

    [Test]
    public void Invisible_killing_hit_uses_observer_handles_and_awards_loot_only_once()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
            { new MonsterResourceEntity { Id = 2101, Hp = 10, Level = 1 } });
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
            { Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 100, Y = 100, Count = 1 } } }));
        var killerWire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var observerWire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var killer = StorageTestHarness.NewGameClient(killerWire);
        var observer = StorageTestHarness.NewGameClient(observerWire);
        StorageTestHarness.Session(killer).CharacterHandle = 100;
        StorageTestHarness.Session(observer).CharacterHandle = 101;
        StorageTestHarness.Session(observer).SpawnedMonsters[0] = 700;
        var players = A.Fake<IPlayerVisibilityService>();
        var registry = new PlayerRegistry(); registry.Register(100, killer); registry.Register(101, observer);
        A.CallTo(() => players.Registry).Returns(registry);
        A.CallTo(() => players.Observers(killer)).Returns(Array.Empty<Navislamia.Game.Network.Clients.GameClient>());
        var ground = A.Fake<IGroundItemService>(); var parties = A.Fake<IPartyService>();
        A.CallTo(() => parties.RewardMembers(killer, 100, 100, 0)).Returns(new[] { killer });
        var service = new CombatService(world, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(), ground,
            A.Fake<IRateService>(), A.Fake<IStatService>(), A.Fake<IStateCatalog>(), parties, players: players);
        world.AddState(0, 4001, 0, 1, 100, 500);
        service.ApplyDamage(killer, 0, 0, 100).Should().Be(0);
        service.ApplyDamage(killer, 0, 0, 100).Should().Be(0);
        A.CallTo(() => ground.DropForMonster(killer, 2101, 100, 100, 0, 0, A<double>._)).MustHaveHappenedOnceExactly();
        var status = observerWire.Sent.Single(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4))
            == (ushort)GamePackets.TM_SC_STATUS_CHANGE);
        BinaryPrimitives.ReadUInt32LittleEndian(status.AsSpan(7)).Should().Be(700);
        killerWire.Sent.Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4))
            == (ushort)GamePackets.TM_SC_STATUS_CHANGE).Should().BeEmpty();
        observerWire.Sent.Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 505).Should().ContainSingle();
    }
}
