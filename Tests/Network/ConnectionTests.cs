using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Security;

namespace Tests.Network;

/// <summary>
/// The connection over real loopback sockets: framing of a stream split and coalesced by TCP, the XRC4
/// keystream in both directions, and the disconnect signal that replaced the 100 ms poll.
/// </summary>
[TestFixture]
public class ConnectionTests
{
    private const string Key = "navislamia-test-key";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private TcpListener _listener;
    private Socket _peer;
    private Socket _serverSide;

    [SetUp]
    public async Task Connect()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var accept = _listener.AcceptSocketAsync();
        await _peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
        _serverSide = await accept;
        _serverSide.NoDelay = true;
    }

    [TearDown]
    public void Disconnect()
    {
        _peer.Dispose();
        _serverSide.Dispose();
        _listener.Stop();
    }

    [Test]
    public async Task CipherConnection_DecodesFramesSplitAndCoalescedByTcp()
    {
        var frames = new[] { Frame(7, 1), Frame(40, 2), Frame(15, 3), Frame(7, 4) };
        var received = new ConcurrentQueue<byte[]>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var connection = new CipherConnection(_serverSide, Key);
        connection.OnDataReceived = available =>
        {
            ReadFrames(connection, available, received);
            if (received.Count == frames.Length)
            {
                done.TrySetResult();
            }
        };
        connection.OnDisconnected = () => { };
        connection.OnDataSent = _ => { };
        connection.Start();

        var wire = Encode(frames.SelectMany(frame => frame).ToArray());

        // Cut inside the second frame's header and inside its body, so a header is decoded while its
        // bytes are still split across two reads.
        foreach (var (start, end) in new[] { (0, 10), (10, 30), (30, wire.Length) })
        {
            await _peer.SendAsync(wire.AsMemory(start, end - start), SocketFlags.None);
            await Task.Delay(30);
        }

        await done.Task.WaitAsync(Timeout);
        received.Should().HaveCount(frames.Length);
        received.ToArray().Should().BeEquivalentTo(frames, options => options.WithStrictOrdering());
    }

    [Test]
    public async Task CipherConnection_EncodesInWireOrderWithoutTouchingTheCallersArray()
    {
        var first = Frame(12, 5);
        var second = Frame(9, 6);
        var pristine = first.ToArray();

        var connection = new CipherConnection(_serverSide, Key);
        connection.OnDataReceived = _ => { };
        connection.OnDisconnected = () => { };
        connection.OnDataSent = _ => { };
        connection.Start();

        // The same array twice: encoding used to happen in place, so the second send went out
        // encrypted twice.
        connection.Send(first);
        connection.Send(first);
        connection.Send(second);

        var wire = await ReceiveExactly(first.Length * 2 + second.Length);

        Decode(wire).Should().Equal(first.Concat(first).Concat(second));
        first.Should().Equal(pristine);
    }

    [Test]
    public async Task Connection_SignalsTheDisconnectWhenThePeerCloses()
    {
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signals = 0;

        var connection = new Connection(_serverSide);
        connection.OnDataReceived = _ => { };
        connection.OnDataSent = _ => { };
        connection.OnDisconnected = () =>
        {
            Interlocked.Increment(ref signals);
            disconnected.TrySetResult();
        };
        connection.Start();

        _peer.Shutdown(SocketShutdown.Both);
        _peer.Close();

        await disconnected.Task.WaitAsync(Timeout);
        await Task.Delay(100);
        signals.Should().Be(1);
        connection.Connected.Should().BeFalse();
    }

    [Test]
    public async Task Connection_KeepsReceivingAfterAHandlerThrows()
    {
        var calls = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<byte[]>();

        var connection = new Connection(_serverSide);
        connection.OnDataSent = _ => { };
        connection.OnDisconnected = () => { };
        connection.OnDataReceived = available =>
        {
            ReadFrames(connection, available, received);
            if (Interlocked.Increment(ref calls) == 1)
            {
                // Escaping the I/O callback used to terminate the process.
                throw new InvalidOperationException("handler bug");
            }

            second.TrySetResult();
        };
        connection.Start();

        await _peer.SendAsync(Frame(8, 7), SocketFlags.None);
        await Task.Delay(100);
        await _peer.SendAsync(Frame(8, 8), SocketFlags.None);

        await second.Task.WaitAsync(Timeout);
        received.Should().HaveCount(2);
        connection.Connected.Should().BeTrue();
    }

    /// <summary>A frame whose first four bytes are its length, filled with a recognisable byte.</summary>
    private static byte[] Frame(int length, byte fill)
    {
        var frame = Enumerable.Repeat(fill, length).ToArray();
        BitConverter.GetBytes((uint)length).CopyTo(frame, 0);
        return frame;
    }

    private static void ReadFrames(Connection connection, int available, ConcurrentQueue<byte[]> received)
    {
        while (available >= 7)
        {
            var length = (int)BitConverter.ToUInt32(connection.Peek(7));
            if (length > available)
            {
                return;
            }

            var frame = connection.Read(length);
            available -= frame.Length;
            received.Enqueue(frame);
        }
    }

    private static byte[] Encode(byte[] plain)
    {
        var cipher = new Xrc4Cipher();
        cipher.SetKey(Key);
        var encoded = new byte[plain.Length];
        cipher.Encode(plain, encoded, plain.Length);
        return encoded;
    }

    private static byte[] Decode(byte[] wire)
    {
        var cipher = new Xrc4Cipher();
        cipher.SetKey(Key);
        var plain = new byte[wire.Length];
        cipher.Decode(wire, plain, wire.Length);
        return plain;
    }

    private async Task<byte[]> ReceiveExactly(int length)
    {
        var buffer = new byte[length];
        var read = 0;
        using var cancellation = new CancellationTokenSource(Timeout);

        while (read < length)
        {
            var count = await _peer.ReceiveAsync(buffer.AsMemory(read), SocketFlags.None, cancellation.Token);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return buffer[..read];
    }
}
