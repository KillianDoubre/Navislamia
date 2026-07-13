using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Navislamia.Game.Network.Clients.Actions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Interfaces;
using Serilog;

namespace Navislamia.Game.Network.Clients;

public class GameClient : Client
{
    private readonly ILogger _logger = Log.ForContext<GameClient>();
    private readonly NetworkService _networkService;

    public GameClient(Socket socket, NetworkService networkService) : base(networkService, ClientType.Game)
    {
        _networkService = networkService;
        Connection = new CipherConnection(socket, networkService.NetworkOptions.CipherKey);
    }

    public void CreateClientConnection()
    {
        Connection.OnDataSent = OnDataSent;
        Connection.OnDataReceived = OnDataReceived;
        Connection.OnDisconnected = OnDisconnect;
        Connection.Start();
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

        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        SyncVisibleObjects();
    }

    private void HandleRegionUpdate(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        ConnectionInfo.Z = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(12, 4));
        SyncVisibleObjects();
    }

    private void HandleChangeLocation(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(0, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        SyncVisibleObjects();
    }

    private void SyncVisibleObjects()
    {
        _networkService.NpcSpawnService.Sync(this);
        _networkService.MonsterSpawnService.Sync(this);
    }

    private void HandleTargeting(byte[] buffer)
    {
        var target = GameActionPackets.ReadTargetHandle(buffer);
        ConnectionInfo.TargetHandle = target;

        if (target == 0)
        {
            _networkService.CombatService.StopAttack(this);
        }
    }

    private void HandleCancelAction(byte[] buffer)
    {
        var handle = GameActionPackets.ReadCancelActionHandle(buffer);
        _logger.Verbose("{clientTag} cancelled action for handle {handle}", ClientTag, handle);
        _networkService.CombatService.StopAttack(this);
    }

    private void HandleAttackRequest(byte[] buffer)
    {
        var target = GameAttackPackets.ReadAttackTarget(buffer);
        _networkService.CombatService.StartAttack(this, target);
    }

    private void HandleChatRequest(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        var count = input[22];
        var type = input[23];
        var message = Encoding.ASCII.GetString(input.Slice(24, count));

        var isLocal = type is (byte)ChatType.Normal or (byte)ChatType.Yell;
        var reply = isLocal
            ? GameChatPackets.BuildChatLocal(ConnectionInfo.CharacterHandle, type, message)
            : GameChatPackets.BuildChat(ConnectionInfo.CharacterName, type, message);

        Connection.Send(reply);
    }

    public override void OnDisconnect()
    {
        _networkService.CombatService.StopAttack(this);
        base.OnDisconnect();
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

            if (header.ID == (ushort)GamePackets.TM_CS_ATTACK_REQUEST)
            {
                HandleAttackRequest(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_TARGETING)
            {
                HandleTargeting(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CANCEL_ACTION)
            {
                HandleCancelAction(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CHAT_REQUEST)
            {
                HandleChatRequest(msgBuffer);
                continue;
            }

            if (header.ID is (ushort)GamePackets.TM_CS_UPDATE or
                (ushort)GamePackets.TM_CS_MONSTER_RECOGNIZE)
            {
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_RETURN_LOBBY)
            {
                SendResult(header.ID, (ushort)ResultCode.Success);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_RETURN_LOBBY)
            {
                ConnectionInfo.CharacterHandle = 0;
                ConnectionInfo.ClearVisibleObjects();
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_LOGOUT)
            {
                SendResult(header.ID, (ushort)ResultCode.Success);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_LOGOUT)
            {
                _logger.Debug("{clientTag} logging out", ClientTag);
                continue;
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
