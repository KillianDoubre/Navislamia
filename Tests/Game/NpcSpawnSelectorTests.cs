using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class NpcSpawnSelectorTests
{
    [Test]
    public void WithinRange_IncludesInRange_ExcludesOutOfRange()
    {
        var npcs = new[]
        {
            new NpcResourceEntity { Id = 1, X = 92100, Y = 116980 },
            new NpcResourceEntity { Id = 2, X = 200000, Y = 116950 },
            new NpcResourceEntity { Id = 3, X = 92044, Y = 116950 }
        };

        var result = NpcSpawnSelector.WithinRange(npcs, 92044f, 116950f, 1000f)
            .Select(n => n.Id).ToList();

        result.Should().BeEquivalentTo(new long[] { 1, 3 });
    }
}
