using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class CharacterGateTests
{
    [Test]
    public async Task RunAsync_NeverOverlapsTwoOperationsOnTheSameCharacter()
    {
        var gate = new CharacterGate();
        var inside = 0;
        var maximum = 0;

        async Task Operation()
        {
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maximum, now);
            await Task.Delay(5);
            Interlocked.Decrement(ref inside);
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => gate.RunAsync("Character", Operation)));

        maximum.Should().Be(1, "a read-modify-write on one character must stay atomic");
    }

    [Test]
    public async Task RunAsync_ReturnsTheOperationsResult()
    {
        var gate = new CharacterGate();

        (await gate.RunAsync("Character", () => Task.FromResult(42))).Should().Be(42);
    }

    [Test]
    public async Task RunAsync_ReleasesTheGateWhenTheOperationThrows()
    {
        var gate = new CharacterGate();

        var failing = () => gate.RunAsync("Character", () => Task.FromException(new System.InvalidOperationException()));
        await failing.Should().ThrowAsync<System.InvalidOperationException>();

        var next = gate.RunAsync("Character", () => Task.FromResult(true));
        (await next.WaitAsync(System.TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
