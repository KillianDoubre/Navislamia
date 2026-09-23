using System;
using System.Net.Sockets;

using Navislamia.Game.Network.Interfaces;
using Navislamia.Game.Network.Security;

namespace Navislamia.Game.Network;

/// <summary>
/// Abstraction of the Connection class that provides encode/decode capabilities for Game BaseClientService connections
/// </summary>
public class CipherConnection : Connection, IConnection
{
    private readonly Xrc4Cipher _sendCipher = new();
    private readonly Xrc4Cipher _receiveCipher = new();

    /// <summary>
    /// How many bytes from <see cref="Connection.ReadOffset"/> are already decoded in place.
    /// </summary>
    /// <remarks>
    /// XRC4 is a stream cipher and the stream is consumed strictly in order, so a byte can be decoded the
    /// first time anything looks at it and never again. <see cref="Peek"/> used to decode a copy of the
    /// header and roll the keystream back — allocating the copy and a 256-byte cipher state per packet —
    /// only for <see cref="Read"/> to decode the same bytes a second time.
    /// </remarks>
    private int _decodedLength;

    /// <summary>
    /// Creates a new instance of the cipher connection wrapper abstraction
    /// </summary>
    /// <param name="socket">Socket being wrapped</param>
    /// <param name="cipherKey">Key to be used in cipher operations</param>
    public CipherConnection(Socket socket, string cipherKey) : base(socket)
    {
        _sendCipher.SetKey(cipherKey);
        _receiveCipher.SetKey(cipherKey);
    }

    /// <summary>
    /// Peeks decoded data in the receive buffer.
    /// </summary>
    /// <param name="length">Amount of data to be peeked from the receive buffer</param>
    /// <returns>ReadOnlySpan pointing to the data inside the receive buffer</returns>
    public override ReadOnlySpan<byte> Peek(int length)
    {
        DecodeUpTo(length);

        return base.Peek(length);
    }

    /// <summary>
    /// Reads decoded data from the receive buffer and advances past it.
    /// </summary>
    /// <param name="input">Amount of data to be read</param>
    /// <returns>Byte array containing read data</returns>
    public override byte[] Read(int input)
    {
        DecodeUpTo(input);

        var readBuffer = base.Read(input);
        _decodedLength -= readBuffer.Length;

        return readBuffer;
    }

    /// <summary>
    /// Encodes on the send loop's own copy, in wire order.
    /// </summary>
    /// <remarks>
    /// XRC4's keystream advances per byte, so the client can only decode what was encoded in the order it
    /// is sent. Encoding used to happen in <c>Send</c>, in place on the caller's array, under a lock that
    /// held encode-and-queue together because the combat, movement and cast ticks and the client's own
    /// thread all send on one connection. The send loop is the single reader of the queue, so encoding
    /// there is ordered by construction and needs no lock — and a packet array is no longer altered, so
    /// the same array can be sent to several connections.
    /// </remarks>
    protected override void EncodeOutgoing(byte[] buffer, int length)
    {
        _sendCipher.Code(buffer.AsSpan(0, length));
    }

    private void DecodeUpTo(int length)
    {
        length = Math.Min(length, AvailableLength);
        if (length <= _decodedLength)
        {
            return;
        }

        _receiveCipher.Code(ReceiveBuffer.AsSpan(ReadOffset + _decodedLength, length - _decodedLength));
        _decodedLength = length;
    }
}
