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
using Navislamia.Game.Services;
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
        return unchecked(ServerClock.Now + ConnectionInfo.ClientClockOffset);
    }

    public void SendTimeSync()
    {
        var message = new Packet<TS_TIMESYNC>((ushort)GamePackets.TM_TIMESYNC,
            new TS_TIMESYNC { Time = ServerClock.Now });
        Connection.Send(message.Data);
    }

    /// <summary>
    /// TM_SC_WEATHER_INFO (902) sent to this client alone: <paramref name="regionId"/> is the
    /// <c>WorldLocation.id</c> of the location, never a visibility region index.
    /// </summary>
    public void SendWeatherInfo(uint regionId, ushort weatherId)
    {
        Connection.Send(GameWeatherPackets.BuildWeatherInfo(regionId, weatherId));
    }

    private void HandleTimeSync(byte[] packet)
    {
        const int sampleWindow = 4;

        var clientTime = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4));
        var gap = unchecked((int)(ServerClock.Now - clientTime));
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

        ConnectionInfo.ClientClockOffset = unchecked(curTime - ServerClock.Now);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        SyncVisibleObjects();
        RefreshEventArea();
    }

    private void HandleRegionUpdate(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        ConnectionInfo.Z = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(12, 4));
        SyncVisibleObjects();
        RefreshEventArea();
    }

    private void HandleChangeLocation(byte[] buffer)
    {
        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(0, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        SyncVisibleObjects();
        RefreshEventArea();
    }

    /// <summary>
    /// The client's 15/16 packets are never the only trigger: the server knows the position and the
    /// loaded polygons, so every position change re-checks the session's event area. This is also
    /// what ends the area state on a map change or a warp, instead of a blind reset that would make
    /// the next position update re-enter the area the character never left.
    /// </summary>
    private void RefreshEventArea() => _networkService.EventAreaService.Refresh(this);

    /// <summary>
    /// TM_CS_GET_REGION_INFO (550): the client converted its own position into region indices and asks for
    /// the region it occupies, so the answer is computed from the two floats it just sent — not from
    /// ConnectionInfo.X/Y, which may lag one move behind. The divisor is the one announced to the client at
    /// login (WorldVisibility.RegionSize, written into TS_SC_LOGIN_RESULT.RegionSize), and the division is
    /// truncated toward zero, exactly as the client does it. Only the asking client is answered.
    /// </summary>
    private void HandleGetRegionInfo(byte[] buffer)
    {
        if (!GameActionPackets.TryReadGetRegionInfo(buffer, out var request))
        {
            _logger.Warning("Malformed region info request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Warning("Region info request received from {clientTag} before the character entered the world",
                ClientTag);
            return;
        }

        var rx = GameMovePackets.GetRegionIndex(request.X);
        var ry = GameMovePackets.GetRegionIndex(request.Y);

        Connection.Send(GameMovePackets.BuildRegionAck(rx, ry));
        _logger.Debug(
            "TM_CS_GET_REGION_INFO ({id}) Length: {length} received from {clientTag}: x={x} y={y} -> rx={rx} ry={ry}",
            (ushort)GamePackets.TM_CS_GET_REGION_INFO, buffer.Length, ClientTag, request.X, request.Y, rx, ry);
    }

    /// <summary>
    /// TM_CS_TAKEOUT_COMMERCIAL_ITEM (10005): the player pulled an item out of the commercial storage
    /// window. The frame is read and logged and nothing is sent back — this lot implements no container
    /// policy at all, because none is established: neither rzu nor NGemity models the container, so no
    /// cost, no cap and no result code may be invented (spec file, reserves 7b and 7e). Any answer that
    /// becomes necessary later goes through the ordinary inventory packets (TM_SC_INVENTORY,
    /// TM_SC_UPDATE_ITEM_COUNT), never through a 10005, which the server must never emit.
    /// </summary>
    private void HandleTakeoutCommercialItem(byte[] buffer)
    {
        if (!GameActionPackets.TryReadTakeoutCommercialItem(buffer, out var request))
        {
            _logger.Warning("Malformed commercial item takeout received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug(
            "TM_CS_TAKEOUT_COMMERCIAL_ITEM ({id}) Length: {length} received from {clientTag}: uid={uid} count={count}",
            (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM, buffer.Length, ClientTag, request.Uid, request.Count);
    }

    /// <summary>
    /// TM_CS_GET_WEATHER_INFO (903): the client asks for the weather of a location id. The id it sends is
    /// opaque — no 7.3 client site builds this packet — so Navislamia reads it as the only identity both
    /// sides can share: <c>WorldLocation.id</c>, the same value a 902 carries. A known id is answered with
    /// a 902 to the asking client alone; an unknown one is answered with nothing at all, because no
    /// reference defines a result or an error for this family. Only the exact 11-byte request is read.
    /// </summary>
    private void HandleGetWeatherInfo(byte[] buffer)
    {
        if (!GameWeatherPackets.TryReadGetWeatherInfo(buffer, out var regionId))
        {
            _logger.Warning("Malformed weather info request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Warning("Weather info request received from {clientTag} before the character entered the world",
                ClientTag);
            return;
        }

        if (regionId > int.MaxValue ||
            !_networkService.WorldLocationService.TryGet((int)regionId, out var location))
        {
            _logger.Debug(
                "TM_CS_GET_WEATHER_INFO ({id}) Length: {length} received from {clientTag}: unknown location {regionId}, no answer",
                (ushort)GamePackets.TM_CS_GET_WEATHER_INFO, buffer.Length, ClientTag, regionId);
            return;
        }

        SendWeatherInfo(regionId, location.CurrentWeather);
        _logger.Debug(
            "TM_CS_GET_WEATHER_INFO ({id}) Length: {length} received from {clientTag}: location {regionId} -> weather_id={weatherId}",
            (ushort)GamePackets.TM_CS_GET_WEATHER_INFO, buffer.Length, ClientTag, regionId,
            location.CurrentWeather);
    }

    private void SyncVisibleObjects()
    {
        _networkService.NpcSpawnService.Sync(this);
        _networkService.MonsterSpawnService.Sync(this);
        _networkService.FieldPropService.Sync(this);
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

    private void HandleResurrection(byte[] buffer)
    {
        if (!GameActionPackets.TryReadResurrection(buffer, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_RESURRECTION, (ushort)ResultCode.InvalidArgument);
            return;
        }

        _networkService.ResurrectionService.Resurrect(this, request);
    }

    private void HandleEmotion(byte[] buffer)
    {
        if (!GameActionPackets.TryReadEmotion(buffer, out var emotion))
        {
            _logger.Warning("Malformed emotion packet received from {clientTag}", ClientTag);
            return;
        }

        // The emotion value is opaque: neither rzu nor NGemity validates a range and the client 7.3
        // resolves the animation and the local message itself, so it is echoed verbatim. No TS_SC_RESULT
        // is sent — nothing identifies an acknowledgement for 1202 and the 1201 alone plays the animation.
        // Only the actor is served: no player-to-player visibility exists yet, so a broadcast would carry
        // a handle the other clients do not know.
        Connection.Send(GameCharacterPackets.BuildEmotion(ConnectionInfo.CharacterHandle, emotion));
        _logger.Debug("TM_CS_EMOTION ({id}) Length: {length} received from {clientTag}: emotion={emotion}",
            (ushort)GamePackets.TM_CS_EMOTION, buffer.Length, ClientTag, emotion);
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
            _networkService.CombatService.DropAggro(this);
            _networkService.SkillCastService.Unregister(this);
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
                info.CharacterJobLevel, info.CharacterExp, info.CharacterJp, info.CharacterGold,
                info.CharacterChaos, info.X, info.Y, info.PkMode);
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

    private async Task HandleEraseItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadEraseItem(packet, out var requests))
        {
            SendResult((ushort)GamePackets.TM_CS_ERASE_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.InventoryService.EraseAsync(this, requests);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process erase item for {clientTag}", ClientTag);
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

    private async Task HandleDropItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadDropItem(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_DROP_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.GroundItemService.DropFromInventoryAsync(this, request.ItemHandle,
                request.Count);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process drop item for {clientTag}", ClientTag);
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

    private async Task HandleUseItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadUseItem(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_USE_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.ItemUseService.UseAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process item use for {clientTag}", ClientTag);
        }
    }

    private void HandleSkill(byte[] packet)
    {
        if (!GameActionPackets.TryReadSkill(packet, out var request))
        {
            return;
        }

        try
        {
            _networkService.SkillCastService.Cast(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process skill {skillId} for {clientTag}", request.SkillId,
                ClientTag);
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

    /// <summary>
    /// TM_CS_INSTANCE_GAME_ENTER (4250): the 7.3 client sends it as an answer to an incoming instance-game
    /// message, copying the <c>instance_game_type</c> it was handed (values 0, 1 and 2 are the only ones
    /// observed). Nothing is answered here: entering an instance is a server side move of the character (a
    /// TM_SC_WARP / region change), not an acknowledgement of its own. The message that triggers it is not
    /// identified yet, so the server cannot provoke a 4250 for now — NON ÉTABLI (b) of
    /// docs/packet-specs/socle-instances-jeu.md.
    /// </summary>
    private void HandleInstanceGameEnter(byte[] buffer)
    {
        if (!GameInstanceGamePackets.TryReadEnter(buffer, out var request))
        {
            _logger.Warning("Malformed instance game enter request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug(
            "TM_CS_INSTANCE_GAME_ENTER ({id}) Length: {length} received from {clientTag}: instanceGameType={type}",
            (ushort)GamePackets.TM_CS_INSTANCE_GAME_ENTER, buffer.Length, ClientTag, request.InstanceGameType);
    }

    /// <summary>
    /// TM_CS_INSTANCE_GAME_EXIT (4251) carries no payload and expects no answer: the character is brought back
    /// to the lobby by the server. The frame is only checked for its exact 7-byte form.
    /// </summary>
    private void HandleInstanceGameExit(byte[] buffer)
    {
        if (!GameInstanceGamePackets.HasNoPayload(buffer))
        {
            _logger.Warning("Malformed instance game exit request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug("TM_CS_INSTANCE_GAME_EXIT ({id}) Length: {length} received from {clientTag}",
            (ushort)GamePackets.TM_CS_INSTANCE_GAME_EXIT, buffer.Length, ClientTag);
    }

    /// <summary>
    /// TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252) is answered by TM_SC_INSTANCE_GAME_SCORE_REQUEST (4253) and by
    /// nothing else: the 4253 is never sent unsolicited. Only <c>holicpoint</c> has a source in 7.3
    /// (CharacterEntity.HuntaholicPoint, the same value the login sequence publishes as the client property
    /// <c>huntaholicpoint</c>). <c>bearroad_ranking</c>, <c>deathmatch_kill_count</c> and
    /// <c>deathmatch_death_count</c> have no source anywhere in 7.3, so they are written as zero — an explicit
    /// placeholder, not a scoring policy. See NON ÉTABLI (h) of docs/packet-specs/socle-instances-jeu.md.
    /// </summary>
    private void HandleInstanceGameScoreRequest(byte[] buffer)
    {
        if (!GameInstanceGamePackets.HasNoPayload(buffer))
        {
            _logger.Warning("Malformed instance game score request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Warning(
                "Instance game score request received from {clientTag} before the character entered the world",
                ClientTag);
            return;
        }

        var character = _networkService.CharacterService.GetCharacterByName(ConnectionInfo.CharacterName);
        if (character is null)
        {
            _logger.Warning("Instance game score request received from {clientTag} for an unknown character {name}",
                ClientTag, ConnectionInfo.CharacterName);
            return;
        }

        var holicPoint = GameInstanceGamePackets.ToWireHolicPoint(character.HuntaholicPoint);

        Connection.Send(GameInstanceGamePackets.BuildScoreResponse(holicPoint, 0u, 0u, 0u));
        _logger.Debug(
            "TM_SC_INSTANCE_GAME_SCORE_REQUEST ({id}) Length: {length} sent to {clientTag}: holicpoint={holicpoint}",
            (ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST, GameInstanceGamePackets.ScoreResponseLength,
            ClientTag, holicPoint);
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

            if (header.ID == (ushort)GamePackets.TM_CS_GET_REGION_INFO)
            {
                HandleGetRegionInfo(msgBuffer);
                continue;
            }

            // TM_SC_REGION_ACK is a server to client packet: the 7.3 client never sends it. An incoming one
            // is a protocol anomaly, not a request, so it is logged and dropped instead of reaching the
            // "Unknown Packet Type" throw below.
            if (header.ID == (ushort)GamePackets.TM_SC_REGION_ACK)
            {
                _logger.Warning("Server to client packet TM_SC_REGION_ACK ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            if (header.ID is (ushort)GamePackets.TM_SC_NPC_TRADE_INFO or (ushort)GamePackets.TM_SC_MARKET)
            {
                // TM_SC_NPC_TRADE_INFO (240) and TM_SC_MARKET (250) are server to client packets: the 7.3
                // client has no way to send them, so an incoming one is a protocol anomaly rather than a
                // request. Logged and dropped, like TM_SC_REGION_ACK above, so the two ids never reach the
                // "Unknown Packet Type" throw below.
                _logger.Warning("Server to client packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            if (header.ID is (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_INFO
                or (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_LIST)
            {
                // TM_SC_COMMERCIAL_STORAGE_INFO (10003) and TM_SC_COMMERCIAL_STORAGE_LIST (10004) are server
                // to client packets: the 7.3 client builds no frame for either id (SFrame.exe owns no
                // constructor site for 0x2713/0x2714), so an incoming one is a protocol anomaly, not a
                // request. Logged and dropped like TM_SC_REGION_ACK above, instead of reaching the
                // "Unknown Packet Type" throw below.
                _logger.Warning("Server to client packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            // The three auction responses are server to client packets too; the 7.3 client only builds
            // 1300/1302/1304/1306/1308/1309/1310 (docs/packet-specs/socle-encheres.md §4.2). Same
            // treatment as TM_SC_REGION_ACK: log and drop, never the throw below.
            if (header.ID is (ushort)GamePackets.TM_SC_AUCTION_SEARCH
                or (ushort)GamePackets.TM_SC_AUCTION_SELLING_LIST
                or (ushort)GamePackets.TM_SC_AUCTION_BIDDED_LIST)
            {
                _logger.Warning("Server to client packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM)
            {
                HandleTakeoutCommercialItem(msgBuffer);
                continue;
            }

            // TM_SC_MIX_RESULT (257) and TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW (261) are server to client
            // packets as well (rzu declares both SessionPacketOrigin::Server): the 7.3 client never sends
            // them, so an incoming one is a protocol anomaly, logged and dropped instead of reaching the
            // "Unknown Packet Type" throw below.
            if (header.ID is (ushort)GamePackets.TM_SC_MIX_RESULT or
                (ushort)GamePackets.TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW)
            {
                _logger.Warning("Server to client packet ({id}) received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            // TM_CS_INSTANCE_GAME_ENTER (4250): the client answers an incoming instance game message with it, so
            // the frame is recorded and nothing is sent back — the character is moved by the server instead.
            if (header.ID == (ushort)GamePackets.TM_CS_INSTANCE_GAME_ENTER)
            {
                HandleInstanceGameEnter(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_INSTANCE_GAME_EXIT)
            {
                HandleInstanceGameExit(msgBuffer);
                continue;
            }

            // TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252) is the only trigger of the 4253 answer.
            if (header.ID == (ushort)GamePackets.TM_CS_INSTANCE_GAME_SCORE_REQUEST)
            {
                HandleInstanceGameScoreRequest(msgBuffer);
                continue;
            }

            // TM_SC_INSTANCE_GAME_SCORE_REQUEST (4253) is a server to client packet: an incoming one is a
            // protocol anomaly, not a request. Logged and dropped so that no id added by this change can reach
            // the "Unknown Packet Type" throw below.
            if (header.ID == (ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST)
            {
                _logger.Warning(
                    "Server to client packet TM_SC_INSTANCE_GAME_SCORE_REQUEST ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_CHANGE_LOCATION)
            {
                HandleChangeLocation(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_ENTER_EVENT_AREA)
            {
                _networkService.EventAreaService.HandlePacket(this, msgBuffer, isEnter: true);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_LEAVE_EVENT_AREA)
            {
                _networkService.EventAreaService.HandlePacket(this, msgBuffer, isEnter: false);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_GET_WEATHER_INFO)
            {
                HandleGetWeatherInfo(msgBuffer);
                continue;
            }

            // TM_SC_WEATHER_INFO is a server to client packet: the 7.3 client never sends it. An incoming one
            // is a protocol anomaly, not a request, so it is logged and dropped instead of reaching the
            // "Unknown Packet Type" throw below — exactly like TM_SC_REGION_ACK above.
            if (header.ID == (ushort)GamePackets.TM_SC_WEATHER_INFO)
            {
                _logger.Warning("Server to client packet TM_SC_WEATHER_INFO ({id}) received from {clientTag}",
                    header.ID, ClientTag);
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

            if (header.ID == (ushort)GamePackets.TM_CS_SKILL)
            {
                HandleSkill(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_ERASE_ITEM)
            {
                _ = HandleEraseItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_TAKE_ITEM)
            {
                _ = HandleTakeItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_DROP_ITEM)
            {
                _ = HandleDropItemAsync(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_USE_ITEM)
            {
                _ = HandleUseItemAsync(msgBuffer);
                continue;
            }

            // The crafting and item-enchantment family (256, 260, 262, 263, 264) goes through the
            // structural socle, which reads and bounds the frame, resolves the handles it names and
            // refuses: the crafting engine and its game policy are not written yet. One arm covers the
            // five ids so that no member of GamePackets reaches the "Unknown Packet Type" throw below.
            // See docs/packet-specs/socle-artisanat-objets.md §9.2.
            if (header.ID is (ushort)GamePackets.TM_CS_MIX or
                (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT or
                (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE or
                (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY or
                (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT)
            {
                _ = _networkService.CraftingSocleService.HandleAsync(this, header.ID, msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_RESURRECTION)
            {
                HandleResurrection(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_EMOTION)
            {
                HandleEmotion(msgBuffer);
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

            // Client anti-cheat datagram (54). The operational disposition is still open — verify,
            // record, ignore or refuse — so this arm only makes the datagram observable: it never
            // answers, validates or disconnects. It is deliberately kept out of the
            // TM_CS_UPDATE/TM_CS_MONSTER_RECOGNIZE/TM_CS_QUERY group above, which is a disposition
            // already settled ("valid, no reply expected") that does not apply here.
            // See docs/packet-specs/socle-anti-triche.md.
            if (header.ID == (ushort)GamePackets.TM_CS_ANTI_HACK)
            {
                GameAntiHackPackets.TryReadAntiHack(msgBuffer, out var declaredAntiHackLength);

                _logger.Debug(
                    "TM_CS_ANTI_HACK ({id}) Length: {length} nLength: {nLength} received from {clientTag}",
                    header.ID, header.Length, declaredAntiHackLength, ClientTag);
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
