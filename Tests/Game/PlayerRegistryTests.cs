using System;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The handle to session registry of the player visibility socle
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.2 point 2): filled at world entry, emptied at
/// world exit, and it never lets a leaving session unbind the session that took the handle over.
/// </summary>
[TestFixture]
public class PlayerRegistryTests
{
    private const uint Handle = 0x40000001u;

    [Test]
    public void Register_ThenResolve_FindsTheSession()
    {
        var registry = new PlayerRegistry();
        var client = NewClient();

        registry.Register(Handle, client).Should().BeTrue();

        registry.Count.Should().Be(1);
        registry.TryResolve(Handle, out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(client);
    }

    [Test]
    public void Register_WithoutACharacterHandle_IsRejected()
    {
        var registry = new PlayerRegistry();

        var register = () => registry.Register(0, NewClient());

        register.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Register_TheSameHandleAgain_GivesItToTheNewerSession()
    {
        var registry = new PlayerRegistry();
        var older = NewClient();
        var newer = NewClient();

        registry.Register(Handle, older);
        registry.Register(Handle, newer).Should().BeFalse("the handle was already bound");

        registry.Count.Should().Be(1);
        registry.TryResolve(Handle, out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(newer);
    }

    [Test]
    public void Register_TheSameSessionAgain_ReportsThatItWasAlreadyBound()
    {
        var registry = new PlayerRegistry();
        var client = NewClient();

        registry.Register(Handle, client).Should().BeTrue();
        registry.Register(Handle, client).Should().BeFalse();

        registry.Count.Should().Be(1);
    }

    [Test]
    public void Unregister_RemovesTheBinding()
    {
        var registry = new PlayerRegistry();
        var client = NewClient();
        registry.Register(Handle, client);

        registry.Unregister(Handle, client).Should().BeTrue();

        registry.Count.Should().Be(0);
        registry.TryResolve(Handle, out var resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Test]
    public void Unregister_FromASessionThatLostTheHandle_KeepsTheBinding()
    {
        var registry = new PlayerRegistry();
        var older = NewClient();
        var newer = NewClient();
        registry.Register(Handle, older);
        registry.Register(Handle, newer);

        registry.Unregister(Handle, older).Should().BeFalse("the handle belongs to the newer session");
        registry.TryResolve(Handle, out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(newer);
    }

    [Test]
    public void Unregister_OfAnUnknownHandle_IsFalse()
    {
        var registry = new PlayerRegistry();

        registry.Unregister(Handle).Should().BeFalse();
    }

    [Test]
    public void TryResolve_OfAnUnknownHandle_IsFalse()
    {
        var registry = new PlayerRegistry();

        registry.TryResolve(Handle, out var resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    private static GameClient NewClient()
    {
        return StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
    }
}
