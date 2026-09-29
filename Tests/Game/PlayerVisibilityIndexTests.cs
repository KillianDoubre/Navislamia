using System;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The mutable presence grid of the player visibility socle
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.2 point 1): adds, moves, removals and the window
/// query with its two filters — layer and euclidean distance.
/// </summary>
[TestFixture]
public class PlayerVisibilityIndexTests
{
    private const uint HandleA = 0x40000001u;
    private const uint HandleB = 0x40000002u;
    private const uint HandleC = 0x40000003u;

    [Test]
    public void Add_IndexesThePresenceAtItsPlace()
    {
        var index = new PlayerVisibilityIndex();
        var presence = Presence(HandleA, 100f, 200f);

        index.Add(presence).Should().BeTrue();

        index.Count.Should().Be(1);
        index.TryGet(HandleA, out var found).Should().BeTrue();
        found.Should().BeSameAs(presence);
    }

    [Test]
    public void Add_WithTheSameHandleTwice_ReplacesTheOlderPresence()
    {
        var index = new PlayerVisibilityIndex();
        var older = Presence(HandleA, 100f, 200f);
        var newer = Presence(HandleA, 5000f, 5000f);

        index.Add(older).Should().BeTrue();
        index.Add(newer).Should().BeFalse("the handle was already indexed");

        index.Count.Should().Be(1, "a character has one presence, never two");
        index.TryGet(HandleA, out var found).Should().BeTrue();
        found.Should().BeSameAs(newer);
        index.WithinRange(100f, 200f, 0, WorldVisibility.ViewRange).Should()
            .BeEmpty("the replaced presence left its cell behind");
    }

    [Test]
    public void PeersInViewOf_KeepsOnlyTheSameLayerAndTheWindow()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f));
        index.Add(Presence(HandleB, 300f, 400f));
        index.Add(Presence(HandleC, 600f, 0f));

        var inView = index.PeersInViewOf(HandleA);

        inView.Select(presence => presence.Handle).Should().Equal(HandleB);
    }

    [Test]
    public void PeersInViewOf_ExcludesItself()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f));

        index.PeersInViewOf(HandleA).Should().BeEmpty();
    }

    [Test]
    public void PeersInViewOf_OfAnUnknownHandle_IsEmpty()
    {
        var index = new PlayerVisibilityIndex();

        index.PeersInViewOf(HandleA).Should().BeEmpty();
    }

    [Test]
    public void PeersInViewOf_OnAnotherLayer_IgnoresTheNeighboursOfTheOtherLayer()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f, layer: 1));
        index.Add(Presence(HandleB, 100f, 100f, layer: 1));
        index.Add(Presence(HandleC, 100f, 100f, layer: 3));

        index.PeersInViewOf(HandleA).Select(presence => presence.Handle).Should().Equal(HandleB);
    }

    [Test]
    public void Move_ReindexesThePresence()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f));
        var walker = Presence(HandleB, 100f, 100f);
        index.Add(walker);

        index.Move(HandleB, 40_000f, 40_000f);

        walker.X.Should().Be(40_000f);
        walker.Y.Should().Be(40_000f);
        index.Count.Should().Be(2, "a move reindexes, it does not duplicate");
        index.PeersInViewOf(HandleA).Should().BeEmpty("the walker left the window");
        index.PeersInViewOf(HandleB).Should().BeEmpty();
        index.TryGet(HandleB, out var found).Should().BeTrue();
        found.Should().BeSameAs(walker);
    }

    [Test]
    public void Move_CanChangeTheLayer()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f, layer: 1));

        index.Move(HandleA, 0f, 0f, 5);

        index.TryGet(HandleA, out var found).Should().BeTrue();
        found.Layer.Should().Be(5);
        index.PeersInViewOf(HandleA).Should().BeEmpty("a query happens on the new layer");
    }

    [Test]
    public void Move_OfAnUnknownHandle_DoesNothing()
    {
        var index = new PlayerVisibilityIndex();

        var move = () => index.Move(HandleA, 10f, 10f);

        move.Should().NotThrow();
        index.Count.Should().Be(0);
    }

    [Test]
    public void Remove_ForgetsThePresence()
    {
        var index = new PlayerVisibilityIndex();
        var presence = Presence(HandleA, 10f, 10f);
        index.Add(presence);

        var removed = index.Remove(HandleA);

        removed.Should().BeSameAs(presence);
        index.Count.Should().Be(0);
        index.TryGet(HandleA, out _).Should().BeFalse();
        index.WithinRange(10f, 10f, 0, WorldVisibility.ViewRange).Should().BeEmpty();
    }

    [Test]
    public void Remove_OfAnUnknownHandle_ReturnsNull()
    {
        var index = new PlayerVisibilityIndex();

        index.Remove(HandleA).Should().BeNull();
    }

    [Test]
    public void WithinRange_FiltersOutTheCornersOfTheNeighbourhood()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 539f, 539f));
        index.Add(Presence(HandleB, 300f, 300f));

        // Both live in the cell of the origin, but only one is inside the 540 unit circle
        // (sqrt(539^2 + 539^2) = 762).
        var inRange = index.WithinRange(0f, 0f, 0, WorldVisibility.ViewRange);

        inRange.Select(presence => presence.Handle).Should().Equal(HandleB);
    }

    [Test]
    public void WithinRange_CanExcludeOneHandle()
    {
        var index = new PlayerVisibilityIndex();
        index.Add(Presence(HandleA, 0f, 0f));
        index.Add(Presence(HandleB, 100f, 0f));

        var inRange = index.WithinRange(0f, 0f, 0, WorldVisibility.ViewRange, excludeHandle: HandleA);

        inRange.Select(presence => presence.Handle).Should().Equal(HandleB);
    }

    [Test]
    public void Constructor_RejectsANonPositiveCellSize()
    {
        var zero = () => new PlayerVisibilityIndex(0f);
        var negative = () => new PlayerVisibilityIndex(-1f);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void WithinRange_RejectsANegativeRange()
    {
        var index = new PlayerVisibilityIndex();

        var query = () => index.WithinRange(0f, 0f, 0, -1f);

        query.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static PlayerPresence Presence(uint handle, float x, float y, byte layer = 0)
    {
        return new PlayerPresence(handle, Appearance(), layer, x, y, 0f);
    }

    private static PlayerAppearance Appearance() => new() { Race = 3, Sex = 1, FaceId = 11 };
}
