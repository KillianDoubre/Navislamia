using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The unbind judgement of <c>TM_CS_UNBIND_SKILLCARD</c> (285) and the write it drives. The reference
/// answers <c>NotExist</c> for a handle the character does not own, <c>NotActable</c> for a target that
/// is not the character, and <c>AccessDenied</c> for everything the item itself fails
/// (NGemity <c>WorldSession.cpp:1706-1717</c>). The bind state is the bearer socket of the item.
/// </summary>
[TestFixture]
public class SkillCardUnbindTests
{
    private const uint CharacterHandle = 4242u;
    private static readonly IItemGroupCatalog SkillCardCatalog =
        new GroupCatalog((900001, ItemGroup.Skillcard), (900002, ItemGroup.Weapon));

    private sealed class GroupCatalog : IItemGroupCatalog
    {
        private readonly Dictionary<long, ItemGroup> _groups;

        public GroupCatalog(params (long ResourceId, ItemGroup Group)[] groups)
        {
            _groups = groups.ToDictionary(entry => entry.ResourceId, entry => entry.Group);
        }

        public bool TryGetGroup(long resourceId, out ItemGroup group)
        {
            return _groups.TryGetValue(resourceId, out group);
        }
    }

    private static ItemEntity BoundCard(long resourceId = 900001, uint bearer = CharacterHandle)
    {
        return new ItemEntity
        {
            Id = 7,
            ItemResourceId = resourceId,
            WearInfo = ItemWearType.None,
            SocketItemIds = new long[] { bearer, 0, 0, 0 }
        };
    }

    private static (CharacterService, ICharacterRepository) ServiceWithItems(params ItemEntity[] items)
    {
        var character = new CharacterEntity
        {
            Id = CharacterHandle,
            CharacterName = "Character",
            Items = items.ToList()
        };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItems("Character")).Returns(character);
        return (new CharacterService(A.Fake<IStarterItemsRepository>(), repository,
            A.Fake<ILogger<CharacterService>>()), repository);
    }

    [Test]
    public void IsBound_ReadsTheBearerSocketOfTheItem()
    {
        SkillCardBindRules.IsBound(BoundCard()).Should().BeTrue();
        SkillCardBindRules.IsBound(BoundCard(bearer: 0)).Should().BeFalse();
        SkillCardBindRules.IsBound(new ItemEntity { SocketItemIds = null }).Should().BeFalse();
        SkillCardBindRules.IsBound(new ItemEntity { SocketItemIds = System.Array.Empty<long>() }).Should().BeFalse();
    }

    [Test]
    public void IsSelfTarget_AcceptsOnlyTheCharacterOwnHandle()
    {
        SkillCardBindRules.IsSelfTarget(CharacterHandle, CharacterHandle).Should().BeTrue();
        SkillCardBindRules.IsSelfTarget(CharacterHandle + 1, CharacterHandle).Should().BeFalse();
        SkillCardBindRules.IsSelfTarget(0u, CharacterHandle).Should().BeFalse();
        SkillCardBindRules.IsSelfTarget(0u, 0u).Should().BeFalse();
    }

    [Test]
    public void CheckUnbindable_AcceptsABoundCardOfTheCharacter()
    {
        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle, BoundCard(),
            SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.Success);
        verdict.ResponseCode.Should().Be((ushort)ResultCode.Success);
        verdict.Succeeded.Should().BeTrue();
    }

    [Test]
    public void CheckUnbindable_AnswersNotExistForAHandleTheCharacterDoesNotOwn()
    {
        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 99, CharacterHandle, null,
            SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.NotFound);
        verdict.ResponseCode.Should().Be((ushort)ResultCode.NotExist);
        // NGemity echoes the item handle there (WorldSession.cpp:1707).
        verdict.Value.Should().Be(99);
    }

    [Test]
    public void CheckUnbindable_AnswersNotActableForAnotherTarget()
    {
        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle + 1, BoundCard(),
            SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.NotActable);
        verdict.ResponseCode.Should().Be((ushort)ResultCode.NotActable);
        // ... and the target handle there (WorldSession.cpp:1711).
        verdict.Value.Should().Be((int)CharacterHandle + 1);
    }

    [Test]
    public void CheckUnbindable_AnswersAccessDeniedForAnItemOfAnotherGroup()
    {
        var card = BoundCard(resourceId: 900002);

        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle, card,
            SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        verdict.ResponseCode.Should().Be((ushort)ResultCode.AccessDenied);
        verdict.Value.Should().Be(7);
    }

    [Test]
    public void CheckUnbindable_LeavesAnUnknownResourceUngated()
    {
        // A resource the catalog does not know cannot be judged: refusing it would invent a refusal
        // neither the reference nor the 253 emits.
        var card = BoundCard(resourceId: 999999);

        SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle, card, SkillCardCatalog)
            .Succeeded.Should().BeTrue();
    }

    [Test]
    public void CheckUnbindable_AnswersAccessDeniedForAWornItem()
    {
        var card = BoundCard();
        card.WearInfo = ItemWearType.Weapon;

        SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle, card, SkillCardCatalog)
            .Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
    }

    [Test]
    public void CheckUnbindable_AnswersAccessDeniedForACardThatIsNotBound()
    {
        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 7, CharacterHandle,
            BoundCard(bearer: 0), SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        verdict.ResponseCode.Should().Be((ushort)ResultCode.AccessDenied);
    }

    [Test]
    public void CheckUnbindable_AnswersNotExistBeforeLookingAtTheTarget()
    {
        // The reference resolves the handle first (:1706) and only then compares the target (:1710).
        var verdict = SkillCardBindRules.CheckUnbindable(CharacterHandle, 99, CharacterHandle + 1, null,
            SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.NotFound);
    }

    [Test]
    public async Task UnbindSkillCard_ClearsTheBearerSocketAndPersists()
    {
        var card = BoundCard();
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 7, CharacterHandle, SkillCardCatalog);

        verdict.Succeeded.Should().BeTrue();
        card.SocketItemIds.Should().HaveCount(SkillCardBindRules.SocketCount);
        card.SocketItemIds[SkillCardBindRules.BearerSocketIndex].Should().Be(0);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task UnbindSkillCard_KeepsTheOtherSocketsOfTheItem()
    {
        var card = BoundCard();
        card.SocketItemIds = new long[] { CharacterHandle, 11, 0, 0 };
        var (service, repository) = ServiceWithItems(card);

        (await service.UnbindSkillCardAsync("Character", 7, CharacterHandle, SkillCardCatalog))
            .Succeeded.Should().BeTrue();

        card.SocketItemIds.Should().Equal(0L, 11L, 0L, 0L);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task UnbindSkillCard_RefusesAForeignTargetWithoutWriting()
    {
        var card = BoundCard();
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 7, CharacterHandle + 1, SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.NotActable);
        verdict.Value.Should().Be((int)CharacterHandle + 1);
        card.SocketItemIds[SkillCardBindRules.BearerSocketIndex].Should().Be(CharacterHandle);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task UnbindSkillCard_RefusesAHandleTheCharacterDoesNotOwnWithoutWriting()
    {
        var card = BoundCard();
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 99, CharacterHandle, SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.NotFound);
        verdict.Value.Should().Be(99);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task UnbindSkillCard_RefusesACardThatIsNotBoundWithoutWriting()
    {
        var card = BoundCard(bearer: 0);
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 7, CharacterHandle, SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        card.SocketItemIds[SkillCardBindRules.BearerSocketIndex].Should().Be(0);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task UnbindSkillCard_RefusesAnotherGroupWithoutWriting()
    {
        var card = BoundCard(resourceId: 900002);
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 7, CharacterHandle, SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        card.SocketItemIds[SkillCardBindRules.BearerSocketIndex].Should().Be(CharacterHandle);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task UnbindSkillCard_RefusesAWornCardWithoutWriting()
    {
        var card = BoundCard();
        card.WearInfo = ItemWearType.Weapon;
        var (service, repository) = ServiceWithItems(card);

        var verdict = await service.UnbindSkillCardAsync("Character", 7, CharacterHandle, SkillCardCatalog);

        verdict.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        card.SocketItemIds[SkillCardBindRules.BearerSocketIndex].Should().Be(CharacterHandle);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }
}
