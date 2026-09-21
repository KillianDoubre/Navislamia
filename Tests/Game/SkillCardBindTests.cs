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
/// The bind of a skill card, TM_CS_BIND_SKILLCARD (284), as docs/packet-specs/284-bind-skillcard.md
/// reads it out of NGemity's WorldSession::onBindSkillCard and Item::SetBindTarget.
/// </summary>
[TestFixture]
public class SkillCardBindTests
{
    private const long SkillCardResource = 640001;
    private const uint CardHandle = 0x80000123u;
    // ConnectionInfo.CharacterHandle is (uint)character.Id (GameActions.cs:96), so the handle the packet
    // carries is the character id the gated judgement compares against.
    private const uint CharacterHandle = 0x40000001u;
    private const long CharacterId = CharacterHandle;

    private static ItemGroupCatalog Catalog(params ItemGroupFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetGroupFields()).Returns(fields);
        return new ItemGroupCatalog(repository);
    }

    private static CharacterService BuildCharacterService(CharacterEntity character,
        out ICharacterRepository repository)
    {
        var fakeRepository = A.Fake<ICharacterRepository>();
        A.CallTo(() => fakeRepository.GetCharacterByNameWithItems(character.CharacterName)).Returns(character);
        repository = fakeRepository;
        return new CharacterService(A.Fake<IStarterItemsRepository>(), fakeRepository,
            A.Fake<ILogger<CharacterService>>());
    }

    private static CharacterEntity Character(ItemEntity card)
    {
        var character = new CharacterEntity { Id = CharacterId, CharacterName = "Binder" };
        character.Items = new List<ItemEntity> { card };
        return character;
    }

    private static ItemEntity Card(long[] sockets = null, ItemWearType wearInfo = ItemWearType.None)
    {
        return new ItemEntity
        {
            Id = CardHandle,
            CharacterId = CharacterId,
            ItemResourceId = SkillCardResource,
            Amount = 1,
            WearInfo = wearInfo,
            SocketItemIds = sockets
        };
    }

    [Test]
    public void IsSelfTarget_AcceptsOnlyTheCharactersOwnHandle()
    {
        SkillCardBindRules.IsSelfTarget(CharacterHandle, CharacterHandle).Should().BeTrue();
        SkillCardBindRules.IsSelfTarget(CharacterHandle + 1, CharacterHandle).Should().BeFalse();
        SkillCardBindRules.IsSelfTarget(0u, CharacterHandle).Should().BeFalse();
    }

    [Test]
    public void IsSelfTarget_RefusesAZeroHandleOnBothSides()
    {
        // A card whose bearer reference cannot be written must not answer Success: socket 0 holds the
        // bearer id, and 0 is exactly what "not bound" reads as.
        SkillCardBindRules.IsSelfTarget(0u, 0u).Should().BeFalse();
    }

    [Test]
    public void IsBound_ReadsTheBearerSocketOnly()
    {
        SkillCardBindRules.IsBound(null).Should().BeFalse();
        SkillCardBindRules.IsBound(new long[4]).Should().BeFalse();
        SkillCardBindRules.IsBound(new long[] { CharacterId, 0, 0, 0 }).Should().BeTrue();
        // Socket 1 is the summoned creature's slot (fiche §5.2 i): it never reads as a bound card.
        SkillCardBindRules.IsBound(new long[] { 0, 7, 8, 9 }).Should().BeFalse();
    }

    [Test]
    public void CheckBindable_AcceptsAnUnboundSkillCardOfTheInventory()
    {
        SkillCardBindRules.CheckBindable(ItemGroup.Skillcard, ItemWearType.None, null)
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckBindable_RefusesEveryOtherGroup()
    {
        SkillCardBindRules.CheckBindable(ItemGroup.Etc, ItemWearType.None, null)
            .Should().Be(ResultCode.AccessDenied);
        SkillCardBindRules.CheckBindable(ItemGroup.Weapon, ItemWearType.None, null)
            .Should().Be(ResultCode.AccessDenied);
        SkillCardBindRules.CheckBindable(ItemGroup.Itemcard, ItemWearType.None, null)
            .Should().Be(ResultCode.AccessDenied);
    }

    [Test]
    public void CheckBindable_LeavesAnUnknownResourceUngated()
    {
        // IItemGroupCatalog's contract: a resource the catalog cannot judge is not refused, it is left to
        // the rules that can be judged. Same stance as GroundItemService and ItemUseService.
        SkillCardBindRules.CheckBindable(null, ItemWearType.None, null).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckBindable_RefusesAWornCard()
    {
        SkillCardBindRules.CheckBindable(ItemGroup.Skillcard, ItemWearType.Righthand, null)
            .Should().Be(ResultCode.AccessDenied);
    }

    [Test]
    public void CheckBindable_RefusesAnAlreadyBoundCard()
    {
        SkillCardBindRules.CheckBindable(ItemGroup.Skillcard, ItemWearType.None, new long[] { CharacterId, 0, 0, 0 })
            .Should().Be(ResultCode.AccessDenied);
    }

    [Test]
    public void AnswerCodes_MatchTheOnesTheClientExpects()
    {
        ((ushort)ResultCode.NotExist).Should().Be(1);
        ((ushort)ResultCode.NotActable).Should().Be(5);
        ((ushort)ResultCode.AccessDenied).Should().Be(6);
        ((int)ItemGroup.Skillcard).Should().Be(10);
    }

    [Test]
    public async Task BindSkillCard_WritesTheCharacterIdInSocketZero()
    {
        var character = Character(Card());
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.Success);
        result.Item.SocketItemIds.Should().Equal(CharacterId, 0L, 0L, 0L);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task BindSkillCard_KeepsTheSocketsTheCardAlreadyCarried()
    {
        var character = Character(Card(new long[] { 0, 7, 8, 9 }));
        var service = BuildCharacterService(character, out _);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Item.SocketItemIds.Should().Equal(CharacterId, 7L, 8L, 9L);
    }

    [Test]
    public async Task BindSkillCard_AnswersNotFoundForAHandleTheCharacterDoesNotOwn()
    {
        // The wrong target rides along: NGemity resolves the handle first, so NOT_EXIST wins.
        var character = Character(Card());
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle + 1, CharacterHandle + 1,
            catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.NotFound);
        result.Item.Should().BeNull();
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task BindSkillCard_RefusesATargetThatIsNotTheCharacter()
    {
        var character = Character(Card());
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle + 1,
            catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.NotActable);
        result.Item.SocketItemIds.Should().BeNull();
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task BindSkillCard_RefusesAnItemThatIsNotASkillCard()
    {
        var character = Character(Card());
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Etc));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task BindSkillCard_RefusesAnAlreadyBoundCard()
    {
        var character = Character(Card(new long[] { CharacterId, 0, 0, 0 }));
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task BindSkillCard_RefusesACardTheCharacterWears()
    {
        var character = Character(Card(wearInfo: ItemWearType.Righthand));
        var service = BuildCharacterService(character, out var repository);
        var catalog = Catalog(new ItemGroupFields((int)SkillCardResource, ItemGroup.Skillcard));

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.AccessDenied);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task BindSkillCard_LeavesACardOfAnUnknownResourceBindable()
    {
        var character = Character(Card());
        var service = BuildCharacterService(character, out _);
        var catalog = Catalog();

        var result = await service.BindSkillCardAsync(character.CharacterName, CardHandle, CharacterHandle, catalog);

        result.Outcome.Should().Be(SkillCardBindOutcome.Success);
    }
}
