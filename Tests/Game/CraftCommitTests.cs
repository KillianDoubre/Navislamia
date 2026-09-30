using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// <see cref="CharacterService.ApplyCraftAsync"/>: a decided craft applies whole — stacks taken, target
/// raised, flagged or destroyed — in one save, or not at all.
/// </summary>
[TestFixture]
public class CraftCommitTests
{
    private CharacterEntity _character = null!;
    private ICharacterRepository _repository = null!;
    private CharacterService _service = null!;
    private ItemEntity _sword = null!;
    private ItemEntity _cubes = null!;

    [SetUp]
    public void SetUp()
    {
        _sword = new ItemEntity { Id = 100, ItemResourceId = 101221, Amount = 1, Enhance = 4, Idx = 1, WearInfo = ItemWearType.None };
        _cubes = new ItemEntity { Id = 200, ItemResourceId = 3620137, Amount = 3, Idx = 2, WearInfo = ItemWearType.None };
        _character = new CharacterEntity { CharacterName = "Crafter", Items = new List<ItemEntity> { _sword, _cubes } };

        _repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => _repository.GetCharacterByNameWithItemsAsync("Crafter")).Returns(_character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(_repository);
        _service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());
    }

    private static CraftTargetChange Change(long newEnhance, int newFlag = 0, bool destroy = false) =>
        new(100, ExpectedEnhance: 4, ExpectedFlag: 0, newEnhance, newFlag, destroy);

    [Test]
    public async Task ASuccess_TakesTheCubeAndRaisesTheTarget()
    {
        var result = await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(200, 1) }, Change(6));

        result.Outcome.Should().Be(CraftCommitOutcome.Success);
        result.Consumed.Should().Equal((200u, 2L));
        result.Target.Should().BeSameAs(_sword);
        _sword.Enhance.Should().Be(6);
        _cubes.Amount.Should().Be(2);
        A.CallTo(() => _repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ADestroyingFailure_DeletesTheTarget_AndTheLastCubeLeavesTheBag()
    {
        _cubes.Amount = 1;

        var result = await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(200, 1) },
            Change(4, destroy: true));

        result.Outcome.Should().Be(CraftCommitOutcome.Success);
        result.Target.Should().BeNull();
        result.Consumed.Should().Equal((200u, 0L));
        _character.Items.Should().BeEmpty();
        A.CallTo(() => _repository.DeleteItem(_sword)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _repository.DeleteItem(_cubes)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ATargetThatChangedSinceTheDecision_AppliesNothing()
    {
        _sword.Enhance = 5;

        var result = await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(200, 1) }, Change(6));

        result.Outcome.Should().Be(CraftCommitOutcome.TargetChanged);
        _cubes.Amount.Should().Be(3);
        A.CallTo(() => _repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task AMissingOrShortStack_AppliesNothing()
    {
        (await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(200, 4) }, Change(6)))
            .Outcome.Should().Be(CraftCommitOutcome.ItemMissing);
        (await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(999, 1) }, Change(6)))
            .Outcome.Should().Be(CraftCommitOutcome.ItemMissing);

        _sword.Enhance.Should().Be(4);
        _cubes.Amount.Should().Be(3);
        A.CallTo(() => _repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task AFlagChange_WritesTheNewBitset()
    {
        var result = await _service.ApplyCraftAsync("Crafter", new[] { new CraftConsumption(200, 1) },
            Change(4, newFlag: 1));

        result.Outcome.Should().Be(CraftCommitOutcome.Success);
        ((int)_sword.Flag).Should().Be(1);
    }
}
