using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Interfaces;
using Serilog;

namespace Navislamia.Game.Network.Clients;

public class GameClient : Client
{
    private readonly ILogger _logger = Log.ForContext<GameClient>();
    public GameClient(Socket socket, NetworkService networkService) : base(networkService, ClientType.Game)
    {
        Connection = new CipherConnection(socket, networkService.NetworkOptions.CipherKey);
    }

    public void CreateClientConnection()
    {
        Connection.OnDataSent = OnDataSent;
        Connection.OnDataReceived = OnDataReceived;
        Connection.OnDisconnected = OnDisconnect;
        Connection.Start();;
    }

    public override void SendMessage(IPacket msg)
    {
        _logger.Debug("{name} ({id}) Length: {length} sent to {clientTag}", msg.StructName, msg.Id, msg.Length, ClientTag);

        base.SendMessage(msg);
    }

    public void SendResult(ushort id, ushort result, int value = 0)
    {
        var message = new Packet<TS_SC_RESULT>((ushort)GamePackets.TM_SC_RESULT, new TS_SC_RESULT(id, result, value));
        SendMessage(message);
    }

    public void SendGameTime()
    {
        var message = new Packet<TS_SC_GAME_TIME>((ushort)GamePackets.TM_SC_GAME_TIME,
            new TS_SC_GAME_TIME { T = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), GameTime = 0 });
        Connection.Send(message.Data);
    }

    private void HandleMoveRequest(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(0, 4));
        var curTime = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(12, 4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(17, 2));
        var waypoints = input.Slice(19, count * 8);

        const byte speed = 100;
        var total = 7 + 12 + count * 8;
        var packet = new byte[total];
        var s = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), curTime);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(11, 4), handle);
        s[15] = 0;
        s[16] = speed;
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(17, 2), count);
        waypoints.CopyTo(s.Slice(19));

        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;

        Connection.Send(packet);
    }

    private void HandleRegionUpdate(byte[] buffer)
    {
        // TS_CS_REGION_UPDATE: update_time(u32), x/y/z(float), bIsStopMessage(bool)
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        ConnectionInfo.Z = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(12, 4));
    }

    private void HandleChangeLocation(byte[] buffer)
    {
        // TS_CS_CHANGE_LOCATION: x(float), y(float)
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(0, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
    }

    public void SendDisconnectDesription(DisconnectType type)
    {
        var message = new Packet<TS_SC_DISCONNECT_DESC>((ushort)GamePackets.TM_SC_DISCONNECT_DESC, new TS_SC_DISCONNECT_DESC(type));
        SendMessage(message);
    }

    public override void OnDataReceived(int bytesReceived)
    {
        var remainingData = bytesReceived;

        while (remainingData > Marshal.SizeOf<Header>())
        {
            var header = new Header(Connection.Peek(Marshal.SizeOf<Header>()));
            var isValidMsg = header.Checksum == header.CalculateChecksum();

            if (header.Length > remainingData)
            {
                // Normal TCP fragmentation: the packet spans reads. Keep the bytes buffered and
                // finish framing it on the next receive.
                _logger.Verbose(
                    "Waiting for rest of packet from {clientTag} (ID: {id} Length: {length} Available: {remaining})",
                    ClientTag, header.ID, header.Length, remainingData);

                return;
            }

            if (!isValidMsg)
            {
                _logger.Error("Invalid Message received from {clientTag} !!!", ClientTag);
                Connection.Disconnect();
                throw new Exception($"Invalid Message recieved from {ClientTag}");
            }

            var msgBuffer = Connection.Read((int)header.Length);

            remainingData -= msgBuffer.Length;

            if (!Enum.IsDefined(typeof(GamePackets), header.ID))
            {
                _logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", header.ID, header.Length, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_NONE)
            {
                // Client keepalive/latency ping (id 9999): not a gameplay opcode (absent from rzu
                // and the client opcode table), carries a tick timestamp, needs no response.
                _logger.Verbose("Keepalive (TM_NONE) Length: {length} from {clientTag}", header.Length, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_GAME_TIME)
            {
                SendGameTime();
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_MOVE_REQUEST)
            {
                HandleMoveRequest(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REGION_UPDATE)
            {
                HandleRegionUpdate(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CHANGE_LOCATION)
            {
                HandleChangeLocation(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_UPDATE)
            {
                continue; // periodic client keepalive, nothing to answer
            }

            IPacket msg = header.ID switch
            {
                (ushort)GamePackets.TM_CS_VERSION => new Packet<TM_CS_VERSION>(msgBuffer),
                (ushort)GamePackets.TM_CS_LOGIN => new Packet<TS_CS_LOGIN>(msgBuffer),
                (ushort)GamePackets.TM_CS_CHARACTER_LIST => new Packet<TS_CS_CHARACTER_LIST>(msgBuffer),
                (ushort)GamePackets.TM_CS_CREATE_CHARACTER => new Packet<TS_CS_CREATE_CHARACTER>(msgBuffer),
                (ushort)GamePackets.TM_CS_DELETE_CHARACTER => new Packet<TS_CS_DELETE_CHARACTER>(msgBuffer),
                (ushort)GamePackets.TM_CS_CHECK_CHARACTER_NAME => new Packet<TS_CS_CHECK_CHARACTER_NAME>(msgBuffer),
                (ushort)GamePackets.TM_CS_ACCOUNT_WITH_AUTH => new Packet<TM_CS_ACCOUNT_WITH_AUTH>(msgBuffer),
                (ushort)GamePackets.TM_CS_REPORT => new Packet<TS_CS_REPORT>(msgBuffer),

                _ => throw new Exception("Unknown Packet Type")
            };

            _logger.Debug("{name} ({id}) Length: {length} received from {clientTag}", msg.StructName, msg.Id, msg.Length, ClientTag);

            Actions.Execute(this, msg);
        }
    }
}
