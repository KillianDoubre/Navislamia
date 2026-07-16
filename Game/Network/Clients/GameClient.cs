using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private int _returnToLobbyInProgress;
    private int _learnSkillInProgress;

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
        if (msg is Packet<TS_SC_RESULT> resultPacket)
        {
            var result = resultPacket.DataStruct;
            _logger.Debug(
                "{name} ({id}) Length: {length}, request={requestId}, result={result}, value={value} sent to {clientTag}",
                msg.StructName, msg.Id, msg.Length, result.RequestMsgID, result.Result, result.Value, ClientTag);
        }
        else
        {
            _logger.Debug("{name} ({id}) Length: {length} sent to {clientTag}", msg.StructName, msg.Id, msg.Length,
                ClientTag);
        }

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
            new TS_SC_GAME_TIME { T = ClientTick(), GameTime = 0 });
        Connection.Send(message.Data);
    }

    private uint ClientTick()
    {
        return unchecked((uint)Environment.TickCount + ConnectionInfo.ClientClockOffset);
    }

    public void SendTimeSync()
    {
        var message = new Packet<TS_TIMESYNC>((ushort)GamePackets.TM_TIMESYNC,
            new TS_TIMESYNC { Time = (uint)Environment.TickCount });
        Connection.Send(message.Data);
    }

    private void HandleTimeSync(byte[] packet)
    {
        const int sampleWindow = 4;

        var clientTime = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4));
        var gap = unchecked((int)((uint)Environment.TickCount - clientTime));
        ConnectionInfo.ClientClockOffset = unchecked((uint)-gap);

        var gaps = ConnectionInfo.TimeSyncGaps;
        gaps.Add(gap);
        if (gaps.Count > sampleWindow)
        {
            gaps.RemoveAt(0);
        }

        if (gaps.Count < sampleWindow)
        {
            SendTimeSync();
            return;
        }

        var total = 0L;
        foreach (var sample in gaps)
        {
            total += sample;
        }

        var averageGap = (int)(total / gaps.Count);
        _logger.Debug("Clock synchronized for {clientTag}: gap={averageGap}ms over {count} samples",
            ClientTag, averageGap, gaps.Count);

        var message = new Packet<TS_SC_SET_TIME>((ushort)GamePackets.TM_SC_SET_TIME,
            new TS_SC_SET_TIME { Gap = averageGap });
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

        ConnectionInfo.ClientClockOffset = unchecked(curTime - (uint)Environment.TickCount);
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

    private async void HandleSetProperty(byte[] buffer)
    {
        const int maxClientInfoLength = 4096;
        if (!GameStatPackets.TryReadSetProperty(buffer, out var name, out var value))
        {
            _logger.Warning("Malformed property update received from {clientTag}", ClientTag);
            return;
        }

        if (!string.Equals(name, "client_info", StringComparison.Ordinal) || value.Length > maxClientInfoLength)
        {
            _logger.Warning("Rejected property {name} ({length} bytes) from {clientTag}", name, value.Length,
                ClientTag);
            return;
        }

        try
        {
            if (!await _networkService.CharacterService.UpdateClientInfoAsync(ConnectionInfo.CharacterName, value))
            {
                _logger.Warning("Could not persist client settings for {character} from {clientTag}",
                    ConnectionInfo.CharacterName, ClientTag);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not persist client settings for {character} from {clientTag}",
                ConnectionInfo.CharacterName, ClientTag);
        }
    }

    public override async void OnDisconnect()
    {
        try
        {
            _networkService.CombatService.StopAttack(this);
            await SaveProgressSafelyAsync("while disconnecting");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not cleanly disconnect {clientTag}", ClientTag);
        }
        finally
        {
            base.OnDisconnect();
        }
    }

    private async Task ReturnToLobbyAsync()
    {
        var info = ConnectionInfo;
        if (Interlocked.CompareExchange(ref _returnToLobbyInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _logger.Debug("{clientTag} returning to character selection", ClientTag);
            _networkService.CombatService.StopAttack(this);
            await SaveProgressSafelyAsync("before returning to character selection");
            info.ClearCharacterSession();
            SendResult((ushort)GamePackets.TM_CS_RETURN_LOBBY, (ushort)ResultCode.Success);
            _logger.Debug("{clientTag} completed the character selection transition", ClientTag);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not return {clientTag} to character selection", ClientTag);
        }
        finally
        {
            Volatile.Write(ref _returnToLobbyInProgress, 0);
        }
    }

    private async Task SaveProgressSafelyAsync(string operation)
    {
        var info = ConnectionInfo;
        try
        {
            await _networkService.CharacterService.SaveProgressAsync(info.CharacterName, info.CharacterLevel,
                info.CharacterJobLevel, info.CharacterExp, info.CharacterJp, info.CharacterGold, info.CharacterChaos);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save progress {operation} for {clientTag}", operation, ClientTag);
        }
    }

    private async Task HandlePutonItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadPutonItem(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_PUTON_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.EquipmentService.EquipAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process equip for {clientTag}", ClientTag);
        }
    }

    private async Task HandleArrangeItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadArrangeItem(packet, out var isStorage))
        {
            SendResult((ushort)GamePackets.TM_CS_ARRANGE_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.InventoryService.ArrangeAsync(this, isStorage);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process arrange item for {clientTag}", ClientTag);
        }
    }

    private async Task HandleTakeItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadTakeItem(packet, out var itemHandle))
        {
            SendResult((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.GroundItemService.TakeAsync(this, itemHandle);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process take item for {clientTag}", ClientTag);
        }
    }

    private async Task HandleChangeItemPositionAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadChangeItemPosition(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_CHANGE_ITEM_POSITION, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.InventoryService.SwapPositionsAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process change item position for {clientTag}", ClientTag);
        }
    }

    private async Task HandlePutoffItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadPutoffItem(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_PUTOFF_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.EquipmentService.UnequipAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process unequip for {clientTag}", ClientTag);
        }
    }

    private async Task HandleLearnSkillAsync(byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_LEARN_SKILL;
        if (!GameActionPackets.TryReadLearnSkill(packet, out var request))
        {
            SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (Interlocked.CompareExchange(ref _learnSkillInProgress, 1, 0) != 0)
        {
            SendResult(requestId, (ushort)ResultCode.Pending, request.SkillId);
            return;
        }

        try
        {
            await _networkService.SkillService.LearnAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process skill learning for {clientTag}", ClientTag);
            SendResult(requestId, (ushort)ResultCode.Misc, request.SkillId);
        }
        finally
        {
            Volatile.Write(ref _learnSkillInProgress, 0);
        }
    }

    public void SendDisconnectDesription(DisconnectType type)
    {
        var message = new Packet<TS_SC_DISCONNECT_DESC>((ushort)GamePackets.TM_SC_DISCONNECT_DESC, new TS_SC_DISCONNECT_DESC(type));
        SendMessage(message);
    }

    public override void OnDataReceived(int bytesReceived)
    {
        var remainingData = bytesReceived;

        while (remainingData >= Marshal.SizeOf<Header>())
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

            if (header.ID == (ushort)GamePackets.TM_CS_JOB_LEVEL_UP)
            {
                _networkService.LevelingService.ApplyJobLevelUp(this, GameActionPackets.ReadTargetHandle(msgBuffer));
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_LEARN_SKILL)
            {
                _ = HandleLearnSkillAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTON_ITEM)
            {
                _ = HandlePutonItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTOFF_ITEM)
            {
                _ = HandlePutoffItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_TIMESYNC)
            {
                HandleTimeSync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_TAKE_ITEM)
            {
                _ = HandleTakeItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_ARRANGE_ITEM)
            {
                _ = HandleArrangeItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CHANGE_ITEM_POSITION)
            {
                _ = HandleChangeItemPositionAsync(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_SET_PROPERTY)
            {
                HandleSetProperty(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CONTACT)
            {
                _networkService.NpcDialogService.Contact(this, msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_DIALOG)
            {
                _networkService.NpcDialogService.Select(this, msgBuffer);
                continue;
            }

            if (header.ID is (ushort)GamePackets.TM_CS_UPDATE or
                (ushort)GamePackets.TM_CS_MONSTER_RECOGNIZE or
                (ushort)GamePackets.TM_CS_QUERY)
            {
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_RETURN_LOBBY)
            {
                _logger.Debug("TM_CS_REQUEST_RETURN_LOBBY ({id}) Length: {length} received from {clientTag}",
                    header.ID, header.Length, ClientTag);
                SendResult(header.ID, (ushort)ResultCode.Success);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_RETURN_LOBBY)
            {
                _logger.Debug("TM_CS_RETURN_LOBBY ({id}) Length: {length} received from {clientTag}",
                    header.ID, header.Length, ClientTag);
                _ = ReturnToLobbyAsync();
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
