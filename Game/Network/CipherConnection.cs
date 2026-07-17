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
    private readonly object _sendCipherLock = new();

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
    /// Peeks encoded data in the receive buffer for data.
    /// </summary>
    /// <param name="length">Amount of data to be peeked from the receive buffer</param>
    /// <returns>ReadOnlySpan pointing to the data inside the receive buffer</returns>
    public override ReadOnlySpan<byte> Peek(int length)
    {
        var peekBuffer = new byte[length];

        Buffer.BlockCopy(ReceiveBuffer, 0, peekBuffer, 0, length);

        _receiveCipher.Decode(peekBuffer, peekBuffer, length, true);

        return new ReadOnlySpan<byte>(peekBuffer, 0, length);
    }

    /// <summary>
    /// Reads encoded data from the receive buffer and moves remaining data to the front of the receive buffer
    /// </summary>
    /// <param name="input">Amount of data to be read</param>
    /// <returns>Byte array containing read data</returns>
    public override byte[] Read(int input)
    {
        var readBuffer = base.Read(input);

        _receiveCipher.Decode(readBuffer, readBuffer, input);

        return readBuffer;
    }

    /// <summary>
    /// Encodes a message and queues it.
    /// </summary>
    /// <remarks>
    /// XRC4 is a stream cipher, so the keystream advances per message and the client decodes in the
    /// order the server encoded. Encoding and queueing therefore have to be one atomic step: the combat
    /// tick, the movement tick, the cast tick and the client's own thread all send on the same
    /// connection, and two of them interleaving here would both consume the keystream out of order and
    /// queue in an order that no longer matches it, which the client cannot decode.
    /// </remarks>
    /// <param name="buffer">Message data to be sent</param>
    public override void Send(byte[] buffer)
    {
        lock (_sendCipherLock)
        {
            _sendCipher.Encode(buffer, buffer, buffer.Length);

            base.Send(buffer);
        }
    }
}