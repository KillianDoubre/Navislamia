using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;
using Navislamia.Game.Network;

namespace Navislamia.LoadTest;

/// <summary>
/// One XRC4 connection seen from the client side. It reuses the server's own <see cref="CipherConnection"/>:
/// the cipher is symmetric with one keystream per direction, so the same class speaks either end, and the
/// framing is the one <c>PacketServer</c> uses. Frames land in <see cref="Inbox"/> in arrival order.
/// </summary>
public sealed class PacketLink : IDisposable
{
    private readonly CipherConnection _connection;
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly RunMetrics _metrics;

    public PacketLink(string ip, int port, string cipherKey, RunMetrics metrics)
    {
        _metrics = metrics;
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        _connection = new CipherConnection(socket, cipherKey);
        _connection.Connect(ip, port);
        _connection.OnDataSent = _ => { };
        _connection.OnDisconnected = () => _inbox.Writer.TryComplete();
        _connection.OnDataReceived = OnDataReceived;
        _connection.Start();
    }

    public ChannelReader<byte[]> Inbox => _inbox.Reader;

    public bool Connected => _connection.Connected;

    public void Send(byte[] frame)
    {
        _metrics.FramesSent.Increment();
        _connection.Send(frame);
    }

    /// <summary>Waits for the first frame of <paramref name="id"/> that <paramref name="match"/> accepts; others go to <paramref name="other"/>.</summary>
    public async Task<byte[]> WaitForAsync(ushort id, TimeSpan timeout, CancellationToken token,
        Func<byte[], bool>? match = null, Action<byte[]>? other = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var frame = await _inbox.Reader.ReadAsync(cts.Token);
                if (Frames.Id(frame) == id && (match is null || match(frame))) return frame;
                other?.Invoke(frame);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"no frame {id} within {timeout.TotalSeconds:0} s");
        }
        catch (ChannelClosedException)
        {
            throw new IOException($"disconnected while waiting for frame {id}");
        }
    }

    private void OnDataReceived(int available)
    {
        var remaining = available;
        while (remaining >= Frames.HeaderSize)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(_connection.Peek(Frames.HeaderSize));
            if (length < Frames.HeaderSize)
            {
                _connection.Disconnect();
                return;
            }

            if (length > remaining) return;
            var frame = _connection.Read(length);
            remaining -= frame.Length;
            _metrics.FramesReceived.Increment();
            _metrics.BytesReceived.Add(frame.Length);
            _inbox.Writer.TryWrite(frame);
        }
    }

    public void Dispose() => _connection.Disconnect();
}
