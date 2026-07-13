using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class SpatialIndexTests
{
    [Test]
    public void WithinRange_ReturnsOnlyObjectsInsideCircularView()
    {
        var objects = new[]
        {
            new WorldObject(1, 100, 100),
            new WorldObject(2, 539, 0),
            new WorldObject(3, 400, 400),
            new WorldObject(4, 2000, 2000)
        };
        var index = CreateIndex(objects);

        var result = index.WithinRange(0, 0, WorldVisibility.ViewRange);

        result.Select(item => item.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Test]
    public void WithinRange_HandlesNegativeCoordinatesAndCellBoundaries()
    {
        var objects = new[]
        {
            new WorldObject(1, -541, -541),
            new WorldObject(2, -100, -100),
            new WorldObject(3, 100, 100)
        };
        var index = CreateIndex(objects);

        var result = index.WithinRange(-540, -540, WorldVisibility.ViewRange);

        result.Select(item => item.Id).Should().BeEquivalentTo(new[] { 1 });
    }

    [Test]
    public void WithinRange_IncludesRadiusBoundaryAndExcludesDiagonalOutsideCircle()
    {
        var objects = new[]
        {
            new WorldObject(1, WorldVisibility.ViewRange, 0),
            new WorldObject(2, WorldVisibility.ViewRange, WorldVisibility.ViewRange)
        };
        var index = CreateIndex(objects);

        var result = index.WithinRange(0, 0, WorldVisibility.ViewRange);

        result.Select(item => item.Id).Should().Equal(1);
    }

    [Test]
    public void WithinRange_SelectsObjectsLocalToEachPosition()
    {
        var objects = new[]
        {
            new WorldObject(1, 83996, 115946),
            new WorldObject(2, 89001, 121464)
        };
        var index = CreateIndex(objects);

        var firstArea = index.WithinRange(83950, 115980, WorldVisibility.ViewRange);
        var secondArea = index.WithinRange(89000, 121465, WorldVisibility.ViewRange);

        firstArea.Select(item => item.Id).Should().Equal(1);
        secondArea.Select(item => item.Id).Should().Equal(2);
    }

    [Test]
    public void Constructor_IndexesCoordinatesOnlyOnce()
    {
        var objects = new[] { new WorldObject(1, 10, 20), new WorldObject(2, 30, 40) };
        var coordinateReads = 0;
        var index = new SpatialIndex<WorldObject>(objects,
            item =>
            {
                coordinateReads++;
                return item.X;
            },
            item =>
            {
                coordinateReads++;
                return item.Y;
            },
            WorldVisibility.ViewRange);

        index.WithinRange(0, 0, 100);
        index.WithinRange(20, 20, 100);

        coordinateReads.Should().Be(4);
        index.Count.Should().Be(2);
    }

    private static SpatialIndex<WorldObject> CreateIndex(IEnumerable<WorldObject> objects)
    {
        return new SpatialIndex<WorldObject>(objects, item => item.X, item => item.Y,
            WorldVisibility.ViewRange);
    }

    private sealed record WorldObject(int Id, float X, float Y);
}
