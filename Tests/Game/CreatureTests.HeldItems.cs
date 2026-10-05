using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The equipment a creature leaves behind when its card goes to someone else (socle-duree-invocations.md §6).</summary>
public partial class CreatureTests
{
    /// <summary>
    /// <c>DB_Login::readEquipItemList</c>: an item whose summon is not the character's (<c>GetSummon</c> fails) is
    /// skipped, so it comes back to the bag unworn; the items of the character's own summons stay on them.
    /// </summary>
    [Test]
    public async Task World_entry_unwears_the_items_of_a_summon_that_is_no_longer_the_characters()
    {
        var h = new Harness();
        var card = new ItemEntity { Id = 60, ItemResourceId = CardId, Amount = 1,
            Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
        A.CallTo(() => h.Characters.GetCreatureStateAsync("Ana", A<IReadOnlyCollection<int>>._))
            .Returns(new CreatureState(new[] { new CreatureCardRecord(card, new SummonEntity
                { Id = 9, SummonResourceId = SummonId, CardItemId = 60, Name = "RossParr", Lv = 1 }) },
                new long[] { 60, 0, 0, 0, 0, 0 }, null));
        A.CallTo(() => h.Characters.GetSummonEquipmentAsync("Ana")).Returns(new List<ItemEntity>
        {
            new() { Id = 70, ItemResourceId = 690001, WearInfo = 0, EquippedBySummonId = 9 },
            new() { Id = 71, ItemResourceId = 690001, WearInfo = (ItemWearType)1, EquippedBySummonId = 77 }
        });
        h.Info.CreatureCards.Clear();

        await h.Service.OnWorldEntryAsync(h.Client);

        A.CallTo(() => h.Characters.UnwearItemsAsync("Ana",
                A<IReadOnlyCollection<long>>.That.Matches(ids => ids.SequenceEqual(new long[] { 71 }))))
            .MustHaveHappenedOnceExactly();
        h.Info.CreatureCards[60].Equipment.Should().ContainSingle(worn => worn.ItemId == 70);
    }
}
