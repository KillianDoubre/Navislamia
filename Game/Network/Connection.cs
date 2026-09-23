using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Channels;

using Navislamia.Game.Network.Interfaces;
using Serilog;

namespace Navislamia.Game.Network;

/// <summary>
/// Socket wrapper for handling Auth/Upload/Game connections
/// </summary>
public class Connection : IConnection
{
    private readonly ILogger _logger = Log.ForContext<Connection>();

    private readonly Socket _socket;

    /// <summary>0 while the connection is alive, 1 once the disconnect has been signalled.</summary>
    private int _disconnectSignaled;

    internal volatile int BytesSent;
    internal volatile int BytesReceived;

    /// <summary>
    /// Unread data is <c>ReceiveBuffer[ReadOffset .. ReadOffset + _dataLength)</c>. Reads advance the
    /// offset; the remainder is moved to the front once per receive, in <see cref="Listen"/>.
    /// </summary>
    /// <remarks>
    /// Every <see cref="Read"/> used to move the whole remainder to the front of the buffer, so a burst of
    /// small packets coalesced in one TCP read was copied over and over: quadratic in the burst.
    /// </remarks>
    private int _dataLength;

    protected int ReadOffset { get; private set; }

    /// <summary>How many unread bytes follow <see cref="ReadOffset"/>.</summary>
    protected int AvailableLength => _dataLength;

    internal readonly byte[] ReceiveBuffer = new byte[32768];

    /// <summary>
    /// Outgoing messages. A channel rather than a polled queue: the send loop parks on
    /// <c>WaitToReadAsync</c> and wakes the moment something is queued, and it collapses a burst into
    /// one wake-up instead of one per message.
    /// </summary>
    private readonly Channel<byte[]> _sendChannel =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Caps how much of a burst is copied into one pooled buffer before it is flushed.</summary>
    private const int MaxSendBatchBytes = 64 * 1024;

    internal readonly CancellationTokenSource CancellationTokenSource = new();
    private CancellationToken _cancellationToken;

    /// <summary>
    /// Event triggered when data has been sent to the remote connection. (includes count of bytes sent)
    /// </summary>
    public Action<int> OnDataSent { get; set; }

    /// <summary>
    /// Event triggered when data has been received from the remote connection (includes count of bytes received)
    /// </summary>
    public Action<int> OnDataReceived { get; set; }

    /// <summary>
    /// Event triggered when the remote connection has disconnected
    /// </summary>
    public Action OnDisconnected { get; set; }


    /// <summary>
    /// Creates a new instance of the connection wrapper
    /// </summary>
    /// <param name="socket">Socket being wrapped</param>
    public Connection(Socket socket)
    {
        _socket = socket;
    }

    /// <summary>
    /// Connects to the remote host at provided ip and port
    /// </summary>
    /// <param name="ip">Remote host ip address</param>
    /// <param name="port">Remote host port</param>
    public void Connect(string ip, int port)
    {
        _socket.Connect(ip, port);
    }

    /// <summary>
    /// Gracefully disconnects the connection. The pending receive then completes empty or in error, and
    /// that completion is what signals the disconnect.
    /// </summary>
    public void Disconnect()
    {
        if (!Connected)
        {
            return;
        }

        try
        {
            _socket.Disconnect(false);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // Already gone: the receive path signals it.
        }
    }

    /// <summary>
    /// Local IP Address of the wrapped socket
    /// </summary>
    public string LocalIp
    {
        get
        {
            if (_socket?.LocalEndPoint is IPEndPoint localEp)
            {
                return localEp.Address.ToString();
            }

            return default;
        }
    }

    /// <summary>
    /// Local port of the wrapped socket
    /// </summary>
    public int LocalPort
    {
        get
        {
            if (_socket?.LocalEndPoint is IPEndPoint localEp)
            {
                return localEp.Port;
            }

            return -1;
        }
    }

    /// <summary>
    /// Remote IP Address of the wrapped socket
    /// </summary>
    public string RemoteIp
    {
        get
        {
            try
            {
                if (_socket?.RemoteEndPoint is IPEndPoint remoteEp)
                {
                    return remoteEp.Address.ToString();
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                // Read from error paths, where the socket may already be gone.
            }

            return default;
        }
    }

    /// <summary>
    /// Remote port of the wrapped socket
    /// </summary>
    public int RemotePort
    {
        get
        {
            if (_socket?.RemoteEndPoint is IPEndPoint remoteEp)
            {
                return remoteEp.Port;
            }

            return -1;
        }
    }

    /// <summary>
    /// Whether the connection is still usable. It no longer polls the socket: the receive loop learns of
    /// a disconnect first (an empty or failed receive) and records it.
    /// </summary>
    public bool Connected => _socket.Connected && Volatile.Read(ref _disconnectSignaled) == 0;

    /// <summary>
    /// Starts internal processes like sending messages and begins listening
    /// </summary>
    /// <remarks>
    /// A disconnect used to be detected by a loop per connection that woke every 100 ms and called
    /// <c>Socket.Poll(1000 µs)</c>, which blocks a thread-pool thread for the whole millisecond whenever
    /// nothing is pending, i.e. almost always: 1% of a thread per player, for information the receive
    /// already has. A peer that closes makes the pending receive complete with 0 bytes, and a reset makes
    /// it throw; both now signal the disconnect directly.
    /// </remarks>
    public virtual void Start()
    {
        _cancellationToken = CancellationTokenSource.Token;

        Task.Run(SendLoop, _cancellationToken);

        Listen();
    }

    /// <summary>
    /// Peeks the receive buffer for data.
    /// </summary>
    /// <param name="length">Amount of data to be peeked from the receive buffer</param>
    /// <returns>ReadOnlySpan pointing to the data inside the receive buffer</returns>
    public virtual ReadOnlySpan<byte> Peek(int length)
    {
        return new ReadOnlySpan<byte>(ReceiveBuffer, ReadOffset, length);
    }

    /// <summary>
    /// Reads data from the receive buffer and advances past it.
    /// </summary>
    /// <param name="input">Amount of data to be read</param>
    /// <returns>Byte array containing read data</returns>
    public virtual byte[] Read(int input)
    {
        // Set the read input to be the smaller of available data in the buffer or the input provided
        var length = Math.Clamp(input, 0, _dataLength);
        var readBuffer = new byte[length];

        Buffer.BlockCopy(ReceiveBuffer, ReadOffset, readBuffer, 0, length);

        // Wipe what was consumed. The receive buffer lives as long as the connection, and a cipher
        // connection decodes in place, so without this a secret frame (a security password, a one-time
        // key) would stay readable in clear until later traffic happened to overwrite it. The frame is
        // now in the caller's hands alone, and a handler can zero that copy too.
        Array.Clear(ReceiveBuffer, ReadOffset, length);

        _dataLength -= length;
        ReadOffset = _dataLength == 0 ? 0 : ReadOffset + length;

        return readBuffer;
    }

    /// <summary>
    /// Queues a message for sending. Derived connections must route through here rather than touch the
    /// channel, or their messages are queued without ever waking the send loop. The buffer is never
    /// modified: any transformation happens on the send loop's own copy (<see cref="EncodeOutgoing"/>).
    /// </summary>
    /// <param name="buffer">Message data to be sent</param>
    public virtual void Send(byte[] buffer)
    {
        _sendChannel.Writer.TryWrite(buffer);
    }

    /// <summary>
    /// Transforms outgoing bytes in place, in exactly the order they go on the wire. Runs on the send loop
    /// only, so a stream cipher needs no lock: the single reader dequeues, encodes and writes in one order.
    /// </summary>
    /// <param name="buffer">The send loop's pooled copy; the first <paramref name="length"/> bytes are sent.</param>
    protected virtual void EncodeOutgoing(byte[] buffer, int length)
    {
    }

    /// <summary>
    /// Drains queued messages onto the socket, coalescing whatever is already queued into a single
    /// write.
    /// </summary>
    /// <remarks>
    /// This used to poll: it drained the queue, then slept 100 ms unconditionally, so anything queued
    /// just after a drain waited up to 100 ms before leaving the server. Every object entering the
    /// player's view paid that, which is what made things pop in late while walking. It also spun at
    /// 100% CPU on a disconnect with a non-empty queue, because the disconnect branch skipped the
    /// dequeue and the queue therefore never emptied.
    /// <para>
    /// Coalescing is safe and is what the wire already looks like: TCP is a byte stream and the client
    /// splits messages by the header length, which is why a lone header-only packet only ever arrived
    /// coalesced with other traffic.
    /// </para>
    /// </remarks>
    protected async Task SendLoop()
    {
        var pending = new List<byte[]>();

        try
        {
            while (await _sendChannel.Reader.WaitToReadAsync(_cancellationToken))
            {
                if (Volatile.Read(ref _disconnectSignaled) != 0)
                {
                    return;
                }

                pending.Clear();
                var total = 0;

                while (total < MaxSendBatchBytes && _sendChannel.Reader.TryRead(out var buffer))
                {
                    pending.Add(buffer);
                    total += buffer.Length;
                }

                if (total > 0)
                {
                    await SendBatchAsync(pending, total);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The socket is gone.
            SignalDisconnect();
        }
    }

    private async Task SendBatchAsync(List<byte[]> pending, int total)
    {
        // Always a copy, even for one message: the encoding happens on it, never on the caller's array,
        // so one packet can safely be handed to several connections.
        var batch = ArrayPool<byte>.Shared.Rent(total);

        try
        {
            var offset = 0;
            foreach (var buffer in pending)
            {
                Buffer.BlockCopy(buffer, 0, batch, offset, buffer.Length);
                offset += buffer.Length;
            }

            EncodeOutgoing(batch, total);

            var sent = 0;
            while (sent < total)
            {
                var written = await _socket.SendAsync(batch.AsMemory(sent, total - sent), SocketFlags.None);
                if (written <= 0)
                {
                    SignalDisconnect();
                    return;
                }

                sent += written;
            }

            OnDataSent?.Invoke(sent);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(batch);
        }
    }

    /// <summary>
    /// Listen for new data sent by the remote connection as long as the connection hasn't been disconnected
    /// </summary>
    protected virtual void Listen()
    {
        if (Volatile.Read(ref _disconnectSignaled) != 0)
        {
            return;
        }

        // Once per receive, not once per packet: move what is left unread to the front.
        if (ReadOffset > 0)
        {
            Buffer.BlockCopy(ReceiveBuffer, ReadOffset, ReceiveBuffer, 0, _dataLength);
            ReadOffset = 0;
        }

        if (_dataLength >= ReceiveBuffer.Length)
        {
            // A frame larger than the buffer can never complete: the peer is not speaking this protocol.
            _logger.Warning("Receive buffer full without a complete frame from {remote}; disconnecting", RemoteIp);
            Disconnect();
            SignalDisconnect();
            return;
        }

        // receive the data into the receive buffer @ the current data input (to preserve any partial packets that may remain in the buffer)
        _socket.BeginReceive(ReceiveBuffer, _dataLength, ReceiveBuffer.Length - _dataLength, SocketFlags.None, OnReceive, _socket);
    }

    /// <summary>
    /// Receives data from the remote connection and add trigger hooked actions for the data to be processed, then trigger listen again
    /// </summary>
    /// <param name="ar"></param>
    private void OnReceive(IAsyncResult ar)
    {
        if (Volatile.Read(ref _disconnectSignaled) != 0)
        {
            return;
        }

        int receiveBytes;
        try
        {
            receiveBytes = _socket.EndReceive(ar);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // A reset or a socket closed by Disconnect.
            SignalDisconnect();
            return;
        }

        if (receiveBytes <= 0)
        {
            // An orderly close by the peer.
            SignalDisconnect();
            return;
        }

        BytesReceived += receiveBytes;
        _dataLength += receiveBytes;

        try
        {
            OnDataReceived(_dataLength);
        }
        catch (Exception exception)
        {
            // This runs on an I/O completion thread: an exception escaping it terminates the process, so
            // one malformed packet or one handler bug used to take the whole server down. The frame that
            // failed has been consumed; the session goes on unless the handler disconnected it.
            _logger.Error(exception, "Unhandled exception while processing data from {remote}", RemoteIp);
        }

        try
        {
            Listen();
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            SignalDisconnect();
        }
    }

    /// <summary>
    /// Records the disconnect once, stops the send loop and raises <see cref="OnDisconnected"/>.
    /// </summary>
    private void SignalDisconnect()
    {
        if (Interlocked.Exchange(ref _disconnectSignaled, 1) != 0)
        {
            return;
        }

        CancellationTokenSource.Cancel();

        try
        {
            OnDisconnected?.Invoke();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Disconnect handler failed for {remote}", RemoteIp);
        }
    }
}
