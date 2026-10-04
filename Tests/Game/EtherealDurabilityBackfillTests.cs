using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class EtherealDurabilityBackfillTests
{
    /// <summary>Resource 1 has a maximum of 5 000; resource 2 has none.</summary>
    private sealed class Catalog : IItemMatchCatalog
    {
        public bool TryGetFields(long code, out ItemMatchFields fields)
        {
            fields = new ItemMatchFields((int)code, default, ItemType.OnehandSword, 1, ItemWearType.Weapon)
                { Mix = ItemMixFields.Empty with { Grade = 1, MaxEtherealDurability = code == 1 ? 5_000 : 0 } };
            return code is 1 or 2;
        }
    }

    [Test]
    public async Task Items_never_initialised_get_their_maximum_and_the_others_are_left_alone()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Items.AddRange(
                new ItemEntity { Id = 1, ItemResourceId = 1, Amount = 1, EtherealDurability = 0 },
                new ItemEntity { Id = 2, ItemResourceId = 1, Amount = 1, EtherealDurability = 1_234 },
                new ItemEntity { Id = 3, ItemResourceId = 2, Amount = 1, EtherealDurability = 0 },
                new ItemEntity { Id = 4, ItemResourceId = 99, Amount = 1, EtherealDurability = 0 });
            await db.SaveChangesAsync();
        }

        await using (var db = new TelecasterContext(options))
            (await EtherealDurabilityBackfill.RunAsync(db, new Catalog())).Should().Be(1);

        await using (var db = new TelecasterContext(options))
            (await db.Items.OrderBy(i => i.Id).Select(i => i.EtherealDurability).ToListAsync())
                .Should().Equal(5_000, 1_234, 0, 0);
    }
}
