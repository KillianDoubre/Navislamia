using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.GmCommands;
using Serilog;
using Serilog.Events;

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
        // Serilog boxes every argument into an object[] before it checks the level once a template has
        // more than three properties, so these two lines allocated on every packet sent, logged or not.
        if (!_logger.IsEnabled(LogEventLevel.Debug))
        {
            base.SendMessage(msg);
            return;
        }

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

    /// <summary>A frame about this player for the player and every player who sees them.</summary>
    public void SendToSelfAndObservers(byte[] frame)
    {
        _networkService.PlayerVisibilityService.SendToObservers(this, frame, includeSelf: true);
    }

    /// <summary>
    /// Sends a vital <c>TS_SC_PROPERTY</c> (hp, mp, max_hp, max_mp) to the player, and the new vitals to the
    /// players who see them as <c>TS_SC_HPMP</c> (509) — what the reference broadcasts for a unit's HP/MP
    /// (NGemity <c>Messages::BroadcastHPMPMessage</c>); the property frame is the local player's own.
    /// </summary>
    public void SendVitalProperty(byte[] propertyFrame)
    {
        Connection.Send(propertyFrame);

        var info = ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        var maxHp = info.CharacterMaxHp;
        var maxMp = info.CharacterMp;
        if (_networkService.StatService is { } stats)
        {
            var total = stats.Compute(info).Total;
            if (total is not null)
            {
                maxHp = (int)total.MaxHp;
                maxMp = (int)total.MaxMp;
            }
        }

        _networkService.PlayerVisibilityService.SendToObservers(this, GameStatPackets.BuildHpMp(
            info.CharacterHandle, 0, info.CharacterHp, Math.Max(maxHp, info.CharacterHp), 0, info.CharacterMp,
            Math.Max(maxMp, info.CharacterMp)));
        _networkService.PartyService?.OnVitalsChanged(this);
        _networkService.GuildService?.OnVitalsChanged(this);
    }

    public void SendGameTime()
    {
        var message = new Packet<TS_SC_GAME_TIME>((ushort)GamePackets.TM_SC_GAME_TIME,
            new TS_SC_GAME_TIME { T = ClientTick(), GameTime = WorldClock.GameTimeNow });
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

    /// <summary>
    /// Publishes this client's own actor status mask as a <c>TM_SC_STATUS_CHANGE</c> (500): the complete
    /// instant snapshot composed by <see cref="ActorStatus.ForPlayer(ConnectionInfo)"/> — PK bit
    /// (<see cref="CreatureStatus.PlayerPkOn"/>, bit 11), sitting, battle mode, walking and booth. Every
    /// site that changes one of those states goes through here, so none of them can publish a mask that
    /// clears another flag. The 7.3 client reads bit 11 to choose between <c>TM_CS_TURN_ON_PK_MODE</c> (800)
    /// and its twin 801, so a stale mask makes it oscillate between the two. The frame also goes to every
    /// player who sees this one, as the booth status does: a peer keeps the mask of its <c>TM_SC_ENTER</c>
    /// otherwise. See docs/packet-specs/socle-mode-pk.md §5.2 and 800-turn-on-pk-mode.md §5.2.
    /// </summary>
    public void SendActorStatus()
    {
        var info = ConnectionInfo;
        var frame = GameCharacterPackets.BuildStatusChange(info.CharacterHandle, ActorStatus.ForPlayer(info));
        if (_networkService?.PlayerVisibilityService is { } visibility)
        {
            visibility.SendToObservers(this, frame, includeSelf: true);
            return;
        }

        Connection.Send(frame);
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

    /// <summary>Header (7) + handle, x, y, cur_time (16) + speed (1) + count (2): the waypoints start at 26.</summary>
    private const int MoveRequestFixedLength = 26;

    private void HandleMoveRequest(byte[] buffer)
    {
        // The waypoint count is the client's to claim: without this check a short frame threw inside the
        // receive callback, which terminated the whole server.
        if (buffer.Length < MoveRequestFixedLength
            || buffer.Length < MoveRequestFixedLength
            + BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(24, 2)) * 8)
        {
            _logger.Warning("Malformed move request received from {clientTag} (Length: {length})", ClientTag,
                buffer.Length);
            return;
        }

        var input = buffer.AsSpan(7);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(0, 4));
        var curTime = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(12, 4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(17, 2));
        var waypoints = input.Slice(19, count * 8);

        // The handle is the client's to claim too (docs/packet-specs/socle-visibilite-joueurs.md §7.7):
        // relaying a foreign handle would walk another client's actor at every observer. A session that
        // holds no character yet (handle 0) has no actor to protect and keeps its echo.
        // onMoveRequest: a summon of the character walks too, at its own speed or its master's (speed_sync).
        if (ConnectionInfo.CharacterHandle != 0 && handle != ConnectionInfo.CharacterHandle
            && _networkService.CreatureService is { } creatures && creatures.OwnsSummon(this, handle))
        {
            creatures.MoveSummon(this, handle, BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4)), curTime, input[16], waypoints);
            return;
        }

        if (ConnectionInfo.CharacterHandle != 0 && handle != ConnectionInfo.CharacterHandle)
        {
            _logger.Warning("{clientTag} claimed handle {handle} instead of {ownHandle} in a move request",
                ClientTag, handle, ConnectionInfo.CharacterHandle);
            return;
        }

        // onMoveRequest echoes GetRealMoveSpeed(): the stat move speed, slowed by the load
        // (StructPlayer::GetMoveSpeed), divided by 7. The echo, the peers' copy and the position estimate all
        // use it (docs/packet-specs/socle-vitesse-echo.md, socle-poids.md).
        if (Navislamia.Game.Services.Creatures.SummonFall.IsActive(ConnectionInfo, ServerClock.Now)) return;
        var speed = _networkService.CarriedWeightService?.RealMoveSpeed(ConnectionInfo)
                    ?? ConnectionInfo.EchoedMoveSpeed;
        // A rider moves at its mount's speed when that is faster (StructSummon::GetRidingMoveSpeed).
        if (_networkService.CreatureService?.RidingSpeed(ConnectionInfo) is { } riding && riding > speed)
        {
            speed = riding;
        }

        ConnectionInfo.MoveSpeed = speed;
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

        // The last waypoint is where the character is going; with the start tick, the server estimates where
        // it is between two reports (what its pet trails).
        ConnectionInfo.MoveStartTick = ServerClock.Now;
        if (count > 0)
        {
            var last = waypoints.Slice((count - 1) * 8, 8);
            ConnectionInfo.DestinationX = BinaryPrimitives.ReadSingleLittleEndian(last.Slice(0, 4));
            ConnectionInfo.DestinationY = BinaryPrimitives.ReadSingleLittleEndian(last.Slice(4, 4));
        }
        else
        {
            ConnectionInfo.DestinationX = ConnectionInfo.X;
            ConnectionInfo.DestinationY = ConnectionInfo.Y;
        }

        SyncVisibleObjects();
        RefreshEventArea();

        // The players: the entry moves in the presence index, the two sides of every pair are put back
        // in step, and the walk is relayed to whoever sees the walker
        // (docs/packet-specs/socle-visibilite-joueurs.md §5.3, trigger 2).
        _networkService.PlayerVisibilityService.OnMove(this, waypoints.ToArray());
    }

    private void HandleRegionUpdate(byte[] buffer)
    {
        // x, y and z are read at 11, 15 and 19.
        if (buffer.Length < 23)
        {
            _logger.Warning("Malformed region update received from {clientTag} (Length: {length})", ClientTag,
                buffer.Length);
            return;
        }

        var input = buffer.AsSpan(7);
        ConnectionInfo.X = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(4, 4));
        ConnectionInfo.Y = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(8, 4));
        ConnectionInfo.Z = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(12, 4));

        // A real position: the estimate of a walking character restarts from it.
        ConnectionInfo.MoveStartTick = ServerClock.Now;
        SyncVisibleObjects();
        RefreshEventArea();

        // A frontier is a move too, but the client sent no path: the neighbours change, nobody walks
        // (docs/packet-specs/socle-visibilite-joueurs.md §5.3, trigger 3).
        _networkService.PlayerVisibilityService.Sync(this);
    }

    private void HandleChangeLocation(byte[] buffer)
    {
        // x and y are read at 7 and 11.
        if (buffer.Length < 15)
        {
            _logger.Warning("Malformed location change received from {clientTag} (Length: {length})", ClientTag,
                buffer.Length);
            return;
        }

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
    /// TM_CS_REQUEST (60): a raw command channel, never a player action. The frame is variable — the
    /// selector <c>t</c> at offset 7, then the command running to the end of the datagram with its NUL
    /// terminator, <c>Length = 9 + L</c> (see GameRequestPackets). Nothing is ported here because there
    /// is nothing to port: rzu declares the packet and never consumes it, Chihiro logs 60 as an unknown
    /// packet and keeps the connection, and the 7.3 client neither names nor emits it. The only producer
    /// found anywhere is a supervision tool shipping a cipher-blobbed SQL statement, so this arm does the
    /// strict minimum a reading without any decryption allows: bound the frame, log its sizes, execute
    /// nothing, answer nothing, sanction nothing.
    /// See docs/packet-specs/60-request.md §5, §8, §9.
    /// </summary>
    private void HandleRequest(byte[] buffer)
    {
        if (!GameRequestPackets.TryReadRequest(buffer, out var selector, out var command))
        {
            _logger.Warning("Malformed TM_CS_REQUEST received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        // Sizes and the selector only, at the Debug level the undeclared id already used. The command
        // itself is never written to the log, not even its first bytes: it is opaque (zlib + simple
        // cipher, hex encoded by the one producer we know) and as large as the receive buffer.
        _logger.Debug(
            "TM_CS_REQUEST ({id}) Length: {length} received from {clientTag}: t={selector} commandLength={commandLength}",
            (ushort)GamePackets.TM_CS_REQUEST, buffer.Length, ClientTag, selector, command.Length);
    }

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
    /// window. The container is the table <c>PaidItems</c>, and this gesture has a real effect now: the row
    /// is resolved under the ownership conditions, the goods go to the bag through the ordinary inventory
    /// packets and the line is consumed — no answer carries the takeout itself, since the family has no
    /// result code (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.6). The frame is read here
    /// so that a malformed one is logged with its client, and the work runs in the background like the
    /// storage request (300/212) does.
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

        _ = TakeoutCommercialItemAsync(request);
    }

    private async Task TakeoutCommercialItemAsync(GameActionPackets.TakeoutCommercialItemRequest request)
    {
        try
        {
            await _networkService.CommercialStorageService.HandleTakeoutAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process commercial item takeout for {clientTag}", ClientTag);
        }
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

    /// <summary>
    /// TM_CS_AUCTION_SELLING_LIST (1302): the player opened, or paged, the sales tab of the auction
    /// house. The eleven-byte request is read and its page logged; the answer is a
    /// TM_SC_AUCTION_SELLING_LIST (1303) built by the socle — 3899 bytes, its forty slots always
    /// written — because nothing in this repository ever writes <c>TelecasterContext.Auctions</c>: no
    /// code adds, updates or removes an <c>AuctionEntity</c>, so the page is necessarily empty and that
    /// is demonstrable (spec §5.5). The request's <c>page_num</c> is echoed back and
    /// <c>total_page_count</c> stays 0: its rule, the ordering, which finished announcements stay
    /// listed, the meaning of <c>status</c> and <c>IsHiddenVillageOnly</c> are open decisions, so no
    /// query and no filter is invented here (spec §5.5, §5.6, §7).
    /// A frame the client could not have built gets the family's result, never an auction frame: no
    /// error variant of 1303 exists in the references or in the client (spec §5.2).
    /// See docs/packet-specs/1302-auction-selling-list.md.
    /// </summary>
    private void HandleAuctionSellingList(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionSellingList(buffer, out var pageNum))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_SELLING_LIST ({id}) Length: {length} received from {clientTag}: page_num={pageNum}",
                (ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST, buffer.Length, ClientTag, pageNum);
        }

        if (_networkService.AuctionService is { } auctions)
            _ = auctions.SellingListAsync(this, pageNum);
        else
            Connection.Send(GameAuctionPackets.BuildAuctionSellingList(pageNum, 0));
    }

    /// <summary>
    /// TM_CS_CHECK_ILLEGAL_USER (57): the client's own security watch reports a suspected illegal program
    /// — never a player action, and the client writes the length in hard at 11, so the frame has no other
    /// form. There is no server to client answer of this family in rzu, NGemity, op_codes.md or the 7.3
    /// client's own incoming dispatcher, and no reference sanctions the sender: the frame is read, logged
    /// at Debug (the level the undeclared id already used) and dropped, without inventing a response or a
    /// sanction. See docs/packet-specs/57-check-illegal-user.md §5.4, §5.5.
    /// </summary>
    private void HandleCheckIllegalUser(byte[] buffer)
    {
        if (!GameActionPackets.TryReadCheckIllegalUser(buffer, out var logCode))
        {
            _logger.Warning("Malformed illegal user report received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug(
            "TM_CS_CHECK_ILLEGAL_USER ({id}) Length: {length} received from {clientTag}: log_code={logCode}",
            (ushort)GamePackets.TM_CS_CHECK_ILLEGAL_USER, buffer.Length, ClientTag, logCode);
    }

    /// <summary>
    /// TM_CS_AUCTION_BIDDED_LIST (1304): the player opened, or paged, the "my bids" tab of the auction
    /// house. The eleven-byte request — the very same frame as TM_CS_AUCTION_SELLING_LIST (1302), with
    /// another Id — is read and its page logged; the answer is a TM_SC_AUCTION_BIDDED_LIST (1305) built
    /// by the socle, 3899 bytes with its forty slots always written, because nothing in this repository
    /// ever writes <c>TelecasterContext.Auctions</c>: no code adds, updates or removes an
    /// <c>AuctionEntity</c>, so the page is necessarily empty and that is demonstrable (spec §5.5).
    /// The request's <c>page_num</c> is echoed back and <c>total_page_count</c> stays 0: its rule, which
    /// announcements stay listed once the character has been outbid, the meaning of <c>status</c>, the
    /// order of the entries and <c>IsHiddenVillageOnly</c> are open decisions, so no query and no filter
    /// is invented here (spec §5.5, §5.6, §7).
    /// The two candidate predicates of "an announcement I bid on" are named where the query would go and
    /// neither is chosen: <c>AuctionEntity.BiddersIds.Contains(handle)</c> ("I bid at least once", a
    /// nullable <c>bigint[]</c> with no writer) and <c>AuctionEntity.HighestBidderId == handle</c> ("I am
    /// the current highest bidder", a not-null indexed column) do not select the same set (spec §5.5,
    /// §8 q1/q2).
    /// A frame the client could not have built gets the family's result, never an auction frame: no
    /// error variant of 1305 exists in the references or in the client (spec §5.2).
    /// See docs/packet-specs/1304-auction-bidded-list.md.
    /// </summary>
    private void HandleAuctionBiddedList(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionBiddedList(buffer, out var pageNum))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_BIDDED_LIST ({id}) Length: {length} received from {clientTag}: page_num={pageNum}",
                (ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST, buffer.Length, ClientTag, pageNum);
        }

        // No query on TelecasterContext.Auctions: an announcement the character bid on would be selected
        // either by BiddersIds (the character appears in it, outbid included) or by HighestBidderId (the
        // character is still the highest bidder). The two sets differ and no source settles which one the
        // window displays (spec §5.5, §8 q1), so the query is not written. The empty page is also the only
        // page this repository can serve: nothing writes Auctions.
        if (_networkService.AuctionService is { } auctions)
            _ = auctions.BiddedListAsync(this, pageNum);
        else
            Connection.Send(GameAuctionPackets.BuildAuctionBiddedList(pageNum, 0));
    }

    /// <summary>
    /// TM_CS_XTRAP_CHECK (59): the XTrap integrity check, 135 bytes of header plus a fixed 128 byte
    /// payload. No reference server implements it and the 7.3 client never emits it — no constructor
    /// writing id 59 exists in SFrame.exe — so there is no logic to port. The frame is read for its
    /// declared size only and dropped: no answer (nothing in rzu, NGemity, op_codes.md or the client's
    /// incoming dispatcher names one, and the client parses its counterpart 58 into an empty branch),
    /// no sanction, and the opaque buffer is never interpreted or logged. Nothing about the content of
    /// pCheckBuffer is established, so nothing can be judged from it.
    /// See docs/packet-specs/59-xtrap-check.md §6, §9.
    /// </summary>
    private void HandleXtrapCheck(byte[] buffer)
    {
        if (!GameXtrapPackets.TryReadXtrapCheck(buffer, out var checkBuffer))
        {
            _logger.Warning("Malformed XTrap check frame received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        // Only the sizes are logged, at the Debug level the undeclared id already used. The 128 payload
        // bytes are opaque and are never written to the log.
        _logger.Debug(
            "TM_CS_XTRAP_CHECK ({id}) Length: {length} pCheckBuffer: {bufferLength} bytes received from {clientTag}",
            (ushort)GamePackets.TM_CS_XTRAP_CHECK, buffer.Length, checkBuffer.Length, ClientTag);
    }

    /// <summary>
    /// TM_CS_AUCTION_SEARCH (1300): the player launched a search in the auction house. The 51-byte
    /// request is read and its five fields logged; the answer is a TM_SC_AUCTION_SEARCH (1301) empty
    /// page, because nothing in this repository ever writes <c>TelecasterContext.Auctions</c> — no code
    /// adds, updates or removes an <c>AuctionEntity</c> — so the page is necessarily empty and that is
    /// demonstrable (spec §5.4). The request's <c>page_num</c> is echoed back and <c>total_page_count</c>
    /// stays 0: its rule, the ordering and page size, the category filter and the meaning of
    /// <c>is_equipable</c> are open decisions, so no policy is invented here (spec §5.5, §5.6, §7).
    /// A frame the client could not have built gets the family's result, never an auction frame: no
    /// error variant of 1301 exists in the references or in the client (spec §5.1).
    /// See docs/packet-specs/1300-auction-search.md.
    /// </summary>
    private void HandleAuctionSearch(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionSearch(buffer, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_SEARCH, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_SEARCH ({id}) Length: {length} received from {clientTag}: category_id={categoryId}, sub_category_id={subCategoryId}, keyword={keyword}, page_num={pageNum}, is_equipable={isEquipable}",
                (ushort)GamePackets.TM_CS_AUCTION_SEARCH, buffer.Length, ClientTag, request.CategoryId,
                request.SubCategoryId, request.Keyword, request.PageNum, request.IsEquipable);
        }

        if (_networkService.AuctionService is { } auctions)
            _ = auctions.SearchAsync(this, request);
        else
            Connection.Send(GameAuctionPackets.BuildAuctionSearch(request.PageNum, 0));
    }

    /// <summary>
    /// TM_CS_TURN_ON_PK_MODE (800): the player switched PK mode on from the client (the toggle key, or the
    /// <c>/PKON</c> line the client turns into the same constructor), a header-only frame of 7 bytes with
    /// an empty body. The treatment is exactly the one the GM command <c>/pk</c> already applies to the same
    /// session state — mutate <see cref="ConnectionInfo.PkMode"/> and republish the actor status mask
    /// through <see cref="SendActorStatus"/> — because the mask is the only channel that state travels on.
    /// Nothing is answered: no <c>TM_SC_*</c> PK packet exists in rzu, NGemity or the 7.3 client's incoming
    /// dispatcher, and the texts the client shows (<c>smsq_pkmode_on</c>, the countdown) are its own, played
    /// locally before it even sends. No restriction is invented either: the client refuses on its own in the
    /// areas it judges closed, from a field of its state this fiche could not name (§2.1, §7 point 1), and
    /// the server keeps no copy of that rule. Persistence is already in place: the session save writes
    /// <c>ConnectionInfo.PkMode</c> back to <c>Characters.PkMode</c> on disconnect.
    /// See docs/packet-specs/800-turn-on-pk-mode.md §5.1-§5.3.
    /// </summary>
    private void HandleTurnOnPkMode(byte[] buffer)
    {
        if (!GameActionPackets.TryReadTurnOnPkMode(buffer))
        {
            _logger.Warning("Malformed TM_CS_TURN_ON_PK_MODE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_TURN_ON_PK_MODE, buffer.Length, ClientTag);
            return;
        }

        // The official switch (onTurnOnPkMode): a PK field only, a 10-second countdown, a result either way.
        if (_networkService.PkModeService is { } pk)
        {
            SendResult((ushort)GamePackets.TM_CS_TURN_ON_PK_MODE, (ushort)pk.RequestOn(this));
            return;
        }

        var wasPkOn = ConnectionInfo.PkMode;
        ConnectionInfo.PkMode = true;
        if (!wasPkOn) _networkService.CombatService.OnPkEnabled(this);
        SendActorStatus();

        _logger.Debug("TM_CS_TURN_ON_PK_MODE ({id}) Length: {length} received from {clientTag}: PK mode on",
            (ushort)GamePackets.TM_CS_TURN_ON_PK_MODE, buffer.Length, ClientTag);
    }

    /// <summary>
    /// TM_CS_TURN_OFF_PK_MODE (801): the player switched PK mode off from the client (the toggle key, or
    /// the <c>/PKOFF</c> line the client turns into the same constructor), a header-only frame of 7 bytes
    /// with an empty body. The treatment is exactly the one the GM command <c>/pk</c> already applies to the
    /// same session state — mutate <see cref="ConnectionInfo.PkMode"/> and republish the actor status mask
    /// through <see cref="SendActorStatus"/> — because the mask is the only channel that state travels on.
    /// Nothing is answered: no <c>TM_SC_*</c> PK packet exists in rzu, NGemity or the 7.3 client's incoming
    /// dispatcher (grepped, none), and the texts the client shows (<c>smsq_pkmode_off</c>) are its own,
    /// played locally before it even sends. No restriction is invented either: the client's "off" path
    /// never consults the area lock its "on" path reads (SFrame.exe+0x684fc7), so no state in which turning
    /// the mode off would be refused is established, and the server keeps no copy of that rule. The frame is
    /// not a toggle: receiving it twice republishes the same mask, because <see cref="ConnectionInfo.PkMode"/>
    /// is the only source of truth. Persistence is already in place: the session save writes
    /// <c>ConnectionInfo.PkMode</c> back to <c>Characters.PkMode</c> on disconnect.
    /// See docs/packet-specs/801-turn-off-pk-mode.md §5.1-§5.5.
    /// </summary>
    private void HandleTurnOffPkMode(byte[] buffer)
    {
        if (!GameActionPackets.TryReadTurnOffPkMode(buffer))
        {
            _logger.Warning("Malformed TM_CS_TURN_OFF_PK_MODE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE, buffer.Length, ClientTag);
            return;
        }

        // The official switch (onTurnOffPkMode): a 30-second countdown, a result either way.
        if (_networkService.PkModeService is { } pk)
        {
            SendResult((ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE, (ushort)pk.RequestOff(this));
            return;
        }

        ConnectionInfo.PkMode = false;
        SendActorStatus();

        _logger.Debug("TM_CS_TURN_OFF_PK_MODE ({id}) Length: {length} received from {clientTag}: PK mode off",
            (ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE, buffer.Length, ClientTag);
    }

    /// <summary>
    /// TM_CS_GET_SUMMON_SETUP_INFO (324), 8 bytes: the client asks for the creature formation again, most
    /// often with show_dialog set when the player opens the window. The answer is a TM_EQUIP_SUMMON (303)
    /// carrying the same six handles as the frame sent at world entry, with the open_dialog byte replaying
    /// what was received. Nothing is written and no TS_SC_RESULT is sent: 324 has no result packet, the
    /// 303 is its only acknowledgement.
    /// </summary>
    private void HandleGetSummonSetupInfo(byte[] buffer)
    {
        if (!GameActionPackets.TryReadGetSummonSetupInfo(buffer, out var request))
        {
            _logger.Warning("Malformed summon setup info request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Warning(
                "Summon setup info request received from {clientTag} before the character entered the world",
                ClientTag);
            return;
        }

        Connection.Send(GameCharacterPackets.BuildEquipSummon(ConnectionInfo.SummonSlots, request.ShowDialog));
        _logger.Debug(
            "TM_CS_GET_SUMMON_SETUP_INFO ({id}) Length: {length} received from {clientTag}: show_dialog={showDialog}",
            (ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO, buffer.Length, ClientTag, request.ShowDialog);
    }

    /// <summary>
    /// TM_CS_AUCTION_CANCEL (1310): the player withdraws one of his own announcements, the act the
    /// register window emits when the <c>deregister</c> confirmation box comes back. The eleven-byte frame
    /// (spec §3.1) is read and logged, and nothing is executed: no auction service or repository exists
    /// here, <c>TM_CS_AUCTION_REGISTER</c> (1309) is not declared so nothing has ever been put up for
    /// auction, and the fate of the returned item and of the registration tax is a game rule Killian has
    /// not settled (spec §5.4, §5.5, §7 question 4). The client expects no dedicated answer either — no
    /// <c>TS_SC_AUCTION_*</c> frame exists past 1305 and the emitting window consumes no result for
    /// 0x51E: it asks for its own selling list (1302) right after sending this frame (spec §3.4, §5.3). A
    /// frame that is not exactly eleven bytes is refused with the family's <c>InvalidArgument</c>, which is
    /// the only thing the server can say about it today.
    /// </summary>
    private void HandleAuctionCancel(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionCancel(buffer, out var auctionUid))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_CANCEL, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_CANCEL ({id}) Length: {length} received from {clientTag}: auction_uid={auctionUid}",
                (ushort)GamePackets.TM_CS_AUCTION_CANCEL, buffer.Length, ClientTag, auctionUid);
        }

        if (_networkService.AuctionService is { } auctions) _ = auctions.CancelAsync(this, unchecked((int)auctionUid));
    }

    /// <summary>
    /// TM_EQUIP_SUMMON (303) from the client, 32 bytes: the player validated a creature formation. NGemity
    /// (<c>WorldSession::onEquipSummon</c>) keeps a card only when it is a summon card owned by the player
    /// and carrying <c>ITEM_FLAG_SUMMON</c> (bit 31, a tamed card), within the slot count the Creature
    /// Control skill (1801) allows, then <b>always</b> answers with the resulting formation. Nothing sets
    /// that flag here — there is no taming and <c>/item</c> writes no flag — so every card is refused and the
    /// resulting formation is the stored one: it is re-sent unchanged, with the request's
    /// <c>open_dialog</c>, which is the reference's own answer for that case and lets the window resync
    /// instead of waiting. Nothing is written. Before this arm the declared id reached the
    /// "Unknown Packet Type" throw. See docs/packet-specs/324-get-summon-setup-info.md §14.
    /// </summary>
    private void HandleEquipSummon(byte[] buffer)
    {
        if (!GameActionPackets.TryReadEquipSummon(buffer, out var request))
        {
            _logger.Warning("Malformed creature formation received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Warning("Creature formation received from {clientTag} before the character entered the world",
                ClientTag);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_EQUIP_SUMMON ({id}) received from {clientTag}: open_dialog={openDialog} cards={cards}; no card is tamed, the stored formation is sent back",
                (ushort)GamePackets.TM_EQUIP_SUMMON, ClientTag, request.OpenDialog,
                string.Join(",", request.CardHandles));
        }

        // StructPlayer::EquipSummon (docs/packet-specs/socle-apprivoisement-invocation.md §15).
        if (_networkService.CreatureService is { } creatures)
        {
            _ = EquipCreaturesAsync(creatures, request);
            return;
        }

        Connection.Send(GameCharacterPackets.BuildEquipSummon(ConnectionInfo.SummonSlots, request.OpenDialog));
    }

    private async Task EquipCreaturesAsync(Navislamia.Game.Services.Creatures.ICreatureService creatures,
        GameActionPackets.EquipSummonRequest request)
    {
        try
        {
            await creatures.EquipAsync(this, request.CardHandles, request.OpenDialog);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Creature formation of {clientTag} failed", ClientTag);
        }
    }

    /// <summary>
    /// TM_CS_SUMMON_CARD_SKILL_LIST (452): the client asks for the skill list of the summon tied to a
    /// creature card (click on the card window's <c>button_flip</c>). The frame is 11 bytes — a 7 byte
    /// header plus a single uint32 <c>item_handle</c> at offset 7. It is read and bounded, and the
    /// handle is logged so that a client capture tells what the field carries; nothing is answered.
    /// No reference implements 452: NGemity declares it and has no handler, rzu only ships the client
    /// side, and neither the client's incoming dispatcher nor op_codes.md names a server answer. The
    /// hypothetical one (TM_SC_SKILL_LIST, 403) would need the card -> summon resolution the spec leaves
    /// open, which is not established, so no table is invented and the received handle is not echoed
    /// back as a target. See docs/packet-specs/452-summon-card-skill-list.md §5.4, §5.5, §7a §7c.
    /// </summary>
    private void HandleSummonCardSkillList(byte[] buffer)
    {
        if (!GameActionPackets.TryReadSummonCardSkillList(buffer, out var itemHandle))
        {
            _logger.Warning("Malformed summon card skill list frame received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_SUMMON_CARD_SKILL_LIST ({id}) Length: {length} item_handle={itemHandle} received from {clientTag}",
                (ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST, buffer.Length, itemHandle, ClientTag);
        }

        // The skills of the card's summon, on the summon's handle (docs/packet-specs/socle-invocations-progression.md).
        _networkService.CreatureService?.SendCardSkillList(this, itemHandle);
    }

    private void SyncVisibleObjects()
    {
        _networkService.NpcSpawnService.Sync(this);
        _networkService.MonsterSpawnService.Sync(this);
        _networkService.FieldPropService.Sync(this);
        _networkService.GroundItemService.Sync(this);
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
        if (buffer.Length != 11) return;
        var handle = GameActionPackets.ReadCancelActionHandle(buffer);
        _logger.Verbose("{clientTag} cancelled action for handle {handle}", ClientTag, handle);
        if (handle != ConnectionInfo.CharacterHandle)
        {
            if (_networkService.CreatureService?.OwnsSummon(this, handle) != true) return;
            _networkService.CreatureService.SummonAttack(this, handle, 0);
            _networkService.SkillCastService?.CancelSummonCast(this, handle);
            return;
        }
        _networkService.CombatService.StopAttack(this);

        // onCancelAction also cancels the cast in progress (StructCreature::CancelSkill).
        _networkService.SkillCastService?.CancelCast(this);
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
        _networkService.PlayerVisibilityService.SendToObservers(this,
            GameCharacterPackets.BuildEmotion(ConnectionInfo.CharacterHandle, emotion), includeSelf: true);
        _logger.Debug("TM_CS_EMOTION ({id}) Length: {length} received from {clientTag}: emotion={emotion}",
            (ushort)GamePackets.TM_CS_EMOTION, buffer.Length, ClientTag, emotion);
    }

    /// <summary>
    /// TM_CS_RANKING_TOP_RECORD (5000): the client asks for the top records of one ranking and expects a
    /// single TM_SC_RANKING_TOP_RECORD (5001) — no TS_SC_RESULT, and no state is armed on its side.
    /// Donation type 0 reads the persisted monthly score ledger; scores use c_fixed10 units.
    /// The official reward roster (1) and unsupported Hunta types do not return invented score lists.
    /// </summary>
    private void HandleRankingTopRecord(byte[] buffer)
    {
        if (!GameActionPackets.TryReadRankingTopRecord(buffer, out var request))
        {
            // A length other than 8 cannot come from the 7.3 client; the specification decides no answer
            // for it, and a TS_SC_RESULT tagged 5000 has no established display (§5.3, §7i).
            _logger.Warning("Malformed ranking top record request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _ = SendDonationRankingAsync(request.RankingType);

        _logger.Debug(
            "TM_CS_RANKING_TOP_RECORD ({id}) Length: {length} received from {clientTag}: ranking_type={rankingType}",
            (ushort)GamePackets.TM_CS_RANKING_TOP_RECORD, buffer.Length, ClientTag, request.RankingType);
    }

    private async Task SendDonationRankingAsync(sbyte type)
    {
        var handle = ConnectionInfo.CharacterHandle; var name = ConnectionInfo.CharacterName;
        try
        {
            var packet = _networkService.DonationRankingService is { } ranking
                ? await ranking.GetAsync(handle, type) : null;
            if (packet is not null && ConnectionInfo.CharacterHandle == handle && ConnectionInfo.CharacterName == name)
                Connection.Send(packet);
        }
        catch (Exception ex) { _logger.Error(ex, "Could not read donation ranking for {Client}", ClientTag); }
    }

    /// <summary>
    /// TM_CS_REQUEST_FARM_INFO (6000), the creature farm window asking for its content — the client sends it on
    /// open and on every refresh (SFrame.exe 0x6109f0). The answer is a TM_SC_FARM_INFO (6001) with
    /// <c>summons = 0</c>: NavisLamia stores no farm at all, so no entry could be filled with anything but
    /// invented values, and the 7.3 client handles the empty farm cleanly.
    ///
    /// A frame whose length is not 7 is malformed, not a request: the client writes the length in hard and no
    /// reference establishes an answer for it, so it is only logged. See
    /// docs/packet-specs/socle-ferme-creatures.md §5.2, §5.3.
    /// </summary>
    private void HandleRequestFarmInfo(byte[] buffer)
    {
        if (!GameFarmPackets.HasNoPayload(buffer))
        {
            _logger.Warning("Malformed TM_CS_REQUEST_FARM_INFO ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_REQUEST_FARM_INFO, buffer.Length, ClientTag);
            return;
        }

        Connection.Send(GameFarmPackets.BuildEmptyFarmInfo());

        _logger.Debug("TM_CS_REQUEST_FARM_INFO ({id}) Length: {length} received from {clientTag}",
            (ushort)GamePackets.TM_CS_REQUEST_FARM_INFO, buffer.Length, ClientTag);
    }

    /// <summary>
    /// TM_CS_FOSTER_CREATURE (6002), the farm's "assign" button: the card to leave in the farm and the ticket
    /// and cracker stacks it consumes. The frame is read and bounded, and nothing is answered: the result frame
    /// 6003 carries a <c>result</c> byte whose values no reference establishes, and this server can neither
    /// validate nor consume the stacks (which item is a ticket is not established either). See
    /// docs/packet-specs/socle-ferme-creatures.md §5.2, §7.5, §7.6.
    /// </summary>
    private void HandleFosterCreature(byte[] buffer)
    {
        if (!GameFarmPackets.TryReadFosterCreature(buffer, out var request))
        {
            _logger.Warning("Malformed TM_CS_FOSTER_CREATURE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_FOSTER_CREATURE, buffer.Length, ClientTag);
            return;
        }

        // Six properties: guarded, or the argument array is built before the level check.
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_FOSTER_CREATURE ({id}) Length: {length} received from {clientTag}: card_handle={cardHandle} " +
                "tickets={tickets} crackers={crackers}",
                (ushort)GamePackets.TM_CS_FOSTER_CREATURE, buffer.Length, ClientTag, request.CreatureCardHandle,
                request.Tickets.Length, request.Crackers.Length);
        }
    }

    /// <summary>
    /// TM_CS_RETRIEVE_CREATURE (6004), the farm's "regain" button: the card the player takes back. Read and
    /// logged, never answered — 6005's <c>result</c> values are not established and the server holds no farm
    /// state to act on. See docs/packet-specs/socle-ferme-creatures.md §5.2, §5.3.
    /// </summary>
    private void HandleRetrieveCreature(byte[] buffer)
    {
        if (!GameFarmPackets.TryReadRetrieveCreature(buffer, out var creatureCardHandle))
        {
            _logger.Warning("Malformed TM_CS_RETRIEVE_CREATURE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, buffer.Length, ClientTag);
            return;
        }

        // Four properties: guarded, or the argument array is built before the level check.
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_RETRIEVE_CREATURE ({id}) Length: {length} received from {clientTag}: card_handle={cardHandle}",
                (ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, buffer.Length, ClientTag, creatureCardHandle);
        }
    }

    /// <summary>
    /// TM_CS_NURSE_CREATURE (6006), the farm's "ministration" buttons (one per slot in the client): the same
    /// 11-byte frame as 6004. Read and logged, never answered, for the same reasons as 6004.
    /// See docs/packet-specs/socle-ferme-creatures.md §5.2, §5.3.
    /// </summary>
    private void HandleNurseCreature(byte[] buffer)
    {
        if (!GameFarmPackets.TryReadNurseCreature(buffer, out var creatureCardHandle))
        {
            _logger.Warning("Malformed TM_CS_NURSE_CREATURE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_NURSE_CREATURE, buffer.Length, ClientTag);
            return;
        }

        // Four properties: guarded, or the argument array is built before the level check.
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_NURSE_CREATURE ({id}) Length: {length} received from {clientTag}: card_handle={cardHandle}",
                (ushort)GamePackets.TM_CS_NURSE_CREATURE, buffer.Length, ClientTag, creatureCardHandle);
        }
    }

    /// <summary>
    /// TM_CS_REQUEST_FARM_MARKET (6008), the farm's ticket-buy button. Read and bounded, never answered: rzu and
    /// NGemity declare no answer to 6008 and the 7.3 client routes no incoming frame for it, so what this
    /// request expects is not established. See docs/packet-specs/socle-ferme-creatures.md §5.2, §7.7.
    /// </summary>
    private void HandleRequestFarmMarket(byte[] buffer)
    {
        if (!GameFarmPackets.HasNoPayload(buffer))
        {
            _logger.Warning("Malformed TM_CS_REQUEST_FARM_MARKET ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET, buffer.Length, ClientTag);
            return;
        }

        _logger.Debug("TM_CS_REQUEST_FARM_MARKET ({id}) Length: {length} received from {clientTag}",
            (ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET, buffer.Length, ClientTag);
    }

    private void HandleAttackRequest(byte[] buffer)
    {
        if (buffer.Length != 15 || ConnectionInfo.CharacterHp <= 0) return;
        var target = GameAttackPackets.ReadAttackTarget(buffer);

        // onAttackRequest: a handle other than the character's names one of its summons.
        var attacker = buffer.Length >= 11 ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(7, 4)) : 0;
        if (attacker != 0 && attacker != ConnectionInfo.CharacterHandle
            && _networkService.CreatureService is { } creatures && creatures.OwnsSummon(this, attacker))
        {
            creatures.SummonAttack(this, attacker, target);
            return;
        }

        if (attacker != ConnectionInfo.CharacterHandle)
        { Connection.Send(GameStateResultPackets.CantAttack(attacker, target, ResultCode.NotOwn)); return; }
        if (target == 0) _networkService.CombatService.StopAttack(this);
        else _networkService.CombatService.StartAttack(this, target);
    }

    /// <summary>
    /// TM_CS_COMPETE_REQUEST (4500), the duel invitation: <c>compete_type</c> at offset 7 and the target's name as
    /// a fixed 31-byte C string at offset 8. This socle implements no duel at all — there is no player to player
    /// registry and no duel state — so every well formed request is refused with the code the 7.3 client displays
    /// for a 4500 (<see cref="GameCompetePackets.RequestRefusalCode"/>, box 1633).
    ///
    /// A malformed frame (length other than 39, or a name without its NUL inside the 31 bytes) is only logged:
    /// no refusal is sent for a frame the client did not build. The server must never emit a 4500 — the 7.3
    /// client does not route that id (it falls into its "message non traité" log).
    /// See docs/packet-specs/socle-competition-joueurs.md, lot C1.
    /// </summary>
    private void HandleCompeteRequest(byte[] buffer)
    {
        if (!GameCompetePackets.TryReadRequest(buffer, out var request))
        {
            _logger.Warning("Malformed compete request received from {clientTag} (Length: {length})", ClientTag,
                buffer.Length);
            return;
        }

        // compete_type is not validated: the 7.3 client copies it without testing it and every observed frame
        // carries 0. Its domain is not established, so the value is only logged (NON ÉTABLI (a) of the sheet).
        _logger.Debug(
            "TM_CS_COMPETE_REQUEST ({id}) Length: {length} received from {clientTag}: competeType={competeType}, requestee={requestee}",
            (ushort)GamePackets.TM_CS_COMPETE_REQUEST, buffer.Length, ClientTag, request.CompeteType,
            request.Requestee);

        if (_networkService.CompeteService is { } compete)
        {
            compete.Request(this, request);
            return;
        }

        SendResult((ushort)GamePackets.TM_CS_COMPETE_REQUEST, (ushort)GameCompetePackets.RequestRefusalCode);
    }

    /// <summary>
    /// TM_CS_COMPETE_ANSWER (4502), the answer to an invitation: <c>compete_type</c> at offset 7 and
    /// <c>answer_type</c> at offset 8. No competition can be in progress here, so a well formed answer is always
    /// refused with <see cref="GameCompetePackets.AnswerRefusalCode"/> (box 1633), including one whose
    /// <c>answer_type</c> is outside the three values the client emits — that case is logged as a warning.
    /// A malformed frame (length other than 9) is only logged.
    /// See docs/packet-specs/socle-competition-joueurs.md, lot C1.
    /// </summary>
    private void HandleCompeteAnswer(byte[] buffer)
    {
        if (!GameCompetePackets.TryReadAnswer(buffer, out var answer))
        {
            _logger.Warning("Malformed compete answer received from {clientTag} (Length: {length})", ClientTag,
                buffer.Length);
            return;
        }

        if (!GameCompetePackets.IsObservedAnswerType(answer.AnswerType))
        {
            _logger.Warning(
                "TM_CS_COMPETE_ANSWER ({id}) from {clientTag} carries an answer type outside the observed values 0, 1 and 2: {answerType}",
                (ushort)GamePackets.TM_CS_COMPETE_ANSWER, ClientTag, answer.AnswerType);
        }

        _logger.Debug(
            "TM_CS_COMPETE_ANSWER ({id}) Length: {length} received from {clientTag}: competeType={competeType}, answerType={answerType}",
            (ushort)GamePackets.TM_CS_COMPETE_ANSWER, buffer.Length, ClientTag, answer.CompeteType,
            answer.AnswerType);

        if (_networkService.CompeteService is { } compete)
        {
            compete.Answer(this, answer);
            return;
        }

        SendResult((ushort)GamePackets.TM_CS_COMPETE_ANSWER, (ushort)GameCompetePackets.AnswerRefusalCode);
    }

    /// <summary><c>szTarget</c> of <c>TS_CS_CHAT_REQUEST</c>: 21 bytes, then <c>request_id</c>.</summary>
    private const int WhisperTargetSize = 21;

    private void HandleChatRequest(byte[] buffer)
    {
        if (buffer.Length < 32)
        {
            return;
        }

        var input = buffer.AsSpan(7);
        var count = input[22];
        var type = input[23];
        if (count == 0 || input.Length < 24 + count)
        {
            return;
        }

        var message = Encoding.ASCII.GetString(input.Slice(24, count)).TrimEnd('\0');
        if (type != (byte)ChatType.Whisper && message.StartsWith('/')
            && _networkService.GuildService?.TryHandleCommand(this, message) == true) return;

        // The party window has no opcode: it sends /pcreate, /pinvite, /pjoin... as chat lines and parses
        // the @PARTY answers (docs/packet-specs/socle-groupe.md). They are player commands, not GM ones.
        if (type != (byte)ChatType.Whisper && message.StartsWith('/')
            && _networkService.PartyService.TryHandleCommand(this, message))
        {
            return;
        }

        // A line starting with '/' is a GM command, never relayed as chat (NGemity's rule,
        // WorldSession::onChatRequest). The handler awaits the database for /item, so it is fired and not
        // awaited here: the receive loop must not wait on it. See docs/gm-commands.md.
        if (GmCommandParser.IsCommand(type, message))
        {
            _ = _networkService.GmCommandService.HandleAsync(this, message,
                _networkService.AuthorizedGameClients.Values);
            return;
        }

        var info = ConnectionInfo;
        if (info.CharacterHandle == 0 || string.IsNullOrEmpty(message))
        {
            return;
        }

        if (info.ChatBlockRemaining(ServerClock.Now) > 0)
        {
            SendResult((ushort)GamePackets.TM_CS_CHAT_REQUEST, (ushort)ResultCode.BlockChat);
            return;
        }

        if (type is (byte)ChatType.Normal or (byte)ChatType.Yell)
        {
            var reply = GameChatPackets.BuildChatLocal(info.CharacterHandle, type, message);
            Connection.Send(reply);
            foreach (var peer in _networkService.PlayerVisibilityService.Index.PeersInViewOf(info.CharacterHandle))
            {
                if (_networkService.PlayerVisibilityService.Registry.TryResolve(peer.Handle, out var recipient))
                {
                    recipient.Connection.Send(reply);
                }
            }
            return;
        }

        var chat = GameChatPackets.BuildChat(info.CharacterName, type, message);
        if (type == (byte)ChatType.Whisper)
        {
            // szTarget is 21 bytes and request_id follows it: the name ends at its first NUL, never at the
            // end of a wider slice, or a non-zero request_id would be read as a last character.
            var targetField = input.Slice(0, WhisperTargetSize);
            var nul = targetField.IndexOf((byte)0);
            var target = Encoding.ASCII.GetString(nul < 0 ? targetField : targetField.Slice(0, nul));

            // NGemity WorldSession::onChatRequest: the line goes to the target alone and the sender gets a
            // result on the chat request, Success or NotExist — the message is not echoed back to them.
            foreach (var recipient in _networkService.PlayerVisibilityService.Registry.Clients)
            {
                if (string.Equals(recipient.ConnectionInfo.CharacterName, target,
                    StringComparison.OrdinalIgnoreCase))
                {
                    recipient.Connection.Send(chat);
                    SendResult((ushort)GamePackets.TM_CS_CHAT_REQUEST, (ushort)ResultCode.Success);
                    return;
                }
            }

            SendResult((ushort)GamePackets.TM_CS_CHAT_REQUEST, (ushort)ResultCode.NotExist);
            return;
        }

        foreach (var recipient in _networkService.PlayerVisibilityService.Registry.Clients)
        {
            var other = recipient.ConnectionInfo;
            var deliver = type switch
            {
                (byte)ChatType.Global => true,
                (byte)ChatType.Party => info.PartyId is not null && info.PartyId == other.PartyId,
                (byte)ChatType.Guild => info.GuildId is > 0 && info.GuildId == other.GuildId,
                (byte)ChatType.AttackTeam => _networkService.GuildService?.SameAttackTeam(this, recipient) == true,
                _ => ReferenceEquals(recipient, this)
            };
            if (deliver) recipient.Connection.Send(chat);
        }
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

    private async Task HandleHideEquipInfoAsync(byte[] buffer)
    {
        if (!GameActionPackets.TryReadHideEquipInfo(buffer, out var hideEquipFlag))
        {
            _logger.Warning("Malformed hide-equip update received from {clientTag}", ClientTag);
            return;
        }

        var flag = unchecked((int)hideEquipFlag);

        try
        {
            // Neither reference server answers 221, and no TS_SC_RESULT is identified for it: the one
            // established acknowledgement is the 222 echo, which the client already handles at bootstrap.
            Connection.Send(GameCharacterPackets.BuildHideEquipInfo(ConnectionInfo.CharacterHandle, flag));

            if (!await _networkService.CharacterService.UpdateHideEquipFlagAsync(ConnectionInfo.CharacterName, flag))
            {
                _logger.Warning("Could not persist the hide-equip flag for {character} from {clientTag}",
                    ConnectionInfo.CharacterName, ClientTag);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not handle the hide-equip flag for {character} from {clientTag}",
                ConnectionInfo.CharacterName, ClientTag);
        }
    }

    private async Task HandleTradeAsync(byte[] buffer)
    {
        if (!PlayerTradePackets.TryRead(buffer, out var request))
        {
            _logger.Warning("TS_TRADE ({id}) of {length} bytes from {clientTag} refused: {size} expected",
                (ushort)GamePackets.TM_TRADE, buffer.Length, ClientTag, PlayerTradePackets.Size);
            return;
        }

        try
        {
            await _networkService.PlayerTradeService.HandleAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Trade step {mode} of {clientTag} failed", request.Mode, ClientTag);
        }
    }

    public override async void OnDisconnect()
    {
        try
        {
            _networkService.CombatService.StopAttack(this);
            _networkService.CombatService.DropAggro(this);
            _networkService.SkillCastService.Unregister(this);
            _networkService.CompeteService?.Leave(this, Navislamia.Game.Services.Compete.CompeteEndType.Logout);
            _networkService.CreatureService?.OnWorldExit(this);
            _networkService.HuntaholicService?.OnWorldExit(this);

            // The exit goes first: every observer must be told before the asynchronous save and the
            // socket cleanup (docs/packet-specs/socle-visibilite-joueurs.md §5.3, trigger 5). An open
            // booth closes before, so its watchers receive their 709.
            _networkService.BoothTradeService.CloseBooth(this);
            _networkService.PlayerTradeService?.CancelFor(this);
            _networkService.PlayerVisibilityService.LeaveWorld(this);
            _networkService.PartyService?.OnWorldExit(this);
            if (_networkService.GuildService is { } guilds) await guilds.OnWorldExitAsync(this);
            _networkService.DungeonRooms?.OnWorldExit(this);

            if (_networkService.QuestService is not null) await _networkService.QuestService.LeaveWorldAsync(this);
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
            _networkService.CompeteService?.Leave(this, Navislamia.Game.Services.Compete.CompeteEndType.Logout);
            _networkService.CreatureService?.OnWorldExit(this);
            _networkService.HuntaholicService?.OnWorldExit(this);
            _networkService.SkillCastService.Unregister(this);
            _networkService.BoothTradeService.CloseBooth(this);
            _networkService.PlayerTradeService?.CancelFor(this);
            _networkService.PlayerVisibilityService.LeaveWorld(this);
            _networkService.PartyService?.OnWorldExit(this);
            if (_networkService.GuildService is { } guilds) await guilds.OnWorldExitAsync(this);
            _networkService.DungeonRooms?.OnWorldExit(this);
            if (_networkService.QuestService is not null) await _networkService.QuestService.LeaveWorldAsync(this);
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
        Task renaming; lock (info.NameChangeLock) renaming = info.NameChangeCompletion;
        try { await renaming; }
        catch (Exception exception) { _logger.Error(exception, "Name change failed before saving {clientTag}", ClientTag); }
        if (_networkService.EventAreaService is { } areas) await areas.LeaveWorldAsync(this);
        if (_networkService.EtherealWear is { } wear) await wear.FlushAsync(this);
        try
        {
            await _networkService.SkillCastService.SaveBuffsAsync(this);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save buffs {operation} for {clientTag}", operation, ClientTag);
        }
        try
        {
            await _networkService.CharacterService.SaveProgressAsync(info.CharacterName, info.CharacterLevel,
                info.CharacterJobLevel, info.CharacterExp, info.CharacterJp, info.CharacterGold,
                info.CharacterChaos, info.X, info.Y, info.PkMode, info.GetPvpProgress(), info.CharacterStamina,
                info.GetHuntaholicProgress());
            await _networkService.CharacterService.SaveChatBlockTimeAsync(info.CharacterName, info.ChatBlockRemaining(ServerClock.Now));
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
            // onPutonItem: a target other than the character is one of its summons (socle-equipement-invocation.md).
            // Routed here: EquipmentService cannot depend on the creatures (combat → death drop → equipment cycle).
            if (request.TargetHandle != 0 && request.TargetHandle != ConnectionInfo.CharacterHandle
                && _networkService.CreatureService is { } creatures)
            {
                await creatures.EquipItemAsync(this, request);
            }
            else
            {
                await _networkService.EquipmentService.EquipAsync(this, request);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process equip for {clientTag}", ClientTag);
        }
    }

    private async Task HandlePutonItemSetAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadPutonItemSet(packet, out var handles))
        {
            SendResult((ushort)GamePackets.TM_CS_PUTON_ITEM_SET, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.EquipmentService.EquipSetAsync(this, handles);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process equipment set for {clientTag}", ClientTag);
        }
    }

    private async Task HandlePutonCardAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadPutonCard(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_PUTON_CARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.CardSocketService.SocketAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process puton card for {clientTag}", ClientTag);
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

    /// <summary>
    /// <c>TM_CS_SELL_ITEM</c> (252), one frame per sold item (the client loops over its trade lines). The
    /// frame is read and bounded here; the gesture — the price, the erase, the gold and the two answers —
    /// belongs to <c>IMarketSellService</c>.
    /// </summary>
    private async Task HandleSellItemAsync(byte[] packet)
    {
        if (!GameTradePackets.TryReadSellItem(packet, out var itemHandle, out var sellCount))
        {
            SendResult((ushort)GamePackets.TM_CS_SELL_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.MarketSellService.SellAsync(this, itemHandle, sellCount);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process sell item for {clientTag}", ClientTag);
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

    /// <summary>
    /// TM_CS_DONATE_REWARD (259) carries only the reward selection the player confirmed in the donation
    /// window: signed slot indices and their quantities. No reference implements a server treatment for
    /// it (rzu and NGemity declare the structure only, §5.1), and neither the meaning of the two record
    /// fields nor the server side reward table is established (§7.1, §7.2, §7.7): the frame is read,
    /// validated, journalised and acknowledged, with no game effect and no invented reward mapping. The
    /// 7.3 client has a result block for request id 259 and expects that acknowledgement (§5.2).
    /// </summary>
    private void HandleDonateReward(byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_DONATE_REWARD;

        if (!GameActionPackets.TryReadDonateReward(packet, out var rewards))
        {
            _logger.Debug("Refused a non conforming TM_CS_DONATE_REWARD ({id}) Length: {length} from {clientTag}",
                requestId, packet.Length, ClientTag);
            SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        _logger.Debug("TM_CS_DONATE_REWARD ({id}) Length: {length} with {count} reward(s) received from {clientTag}",
            requestId, packet.Length, rewards.Length, ClientTag);

        foreach (var reward in rewards)
        {
            _logger.Debug("Donation reward slot {rewardType} carries {count} for {clientTag}",
                reward.RewardType, reward.Count, ClientTag);
        }

        // onDonateReward: the moral points buy the reward items (docs/packet-specs/259-donate-reward.md §11).
        if (_networkService.ItemDonateService is { } donations)
        {
            _ = AnswerDonateRewardAsync(donations, rewards);
            return;
        }

        SendResult(requestId, (ushort)ResultCode.Success);
    }

    private async Task AnswerDonateRewardAsync(IItemDonateService donations,
        GameActionPackets.DonateRewardEntry[] rewards)
    {
        var result = await donations.RewardAsync(this, rewards);
        SendResult((ushort)GamePackets.TM_CS_DONATE_REWARD, (ushort)result);
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

    /// <summary>
    /// TM_CS_AUCTION_BID (1306): the player places a bid on an announcement of the auction house, the only
    /// act of the family whose answer is not a <c>TS_SC_AUCTION_*</c> frame — the 7.3 client reads a
    /// TM_SC_RESULT (id 0) whose <c>request_msg_id</c> is 1306. The nineteen-byte frame (spec §3.1) is read
    /// and logged; the bid itself is never executed, because its rules, its announcements and its writer
    /// belong to lots that do not exist here (spec §12), and no <c>ResultCode</c> is sent for a well-formed
    /// frame: with nothing executed, neither <c>Success</c> nor a refusal code would be true, and the code
    /// the client expects is not established (spec §7.1).
    /// </summary>
    private void HandleAuctionBid(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionBid(buffer, out var auctionUid, out var price))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_BID, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_BID ({id}) Length: {length} received from {clientTag}: auction_uid={auctionUid}, price={price}",
                (ushort)GamePackets.TM_CS_AUCTION_BID, buffer.Length, ClientTag, auctionUid, price);
        }

        if (_networkService.AuctionService is { } auctions) _ = auctions.BidAsync(this, auctionUid, price);
    }

    private async Task HandleDropQuestAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadDropQuest(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_DROP_QUEST, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.QuestService.DropQuestAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process drop quest for {clientTag}", ClientTag);
        }
    }

    /// <summary>
    /// <c>TM_CS_QUEST_INFO</c> (604): the player selected a quest in the list window and pressed
    /// <c>quest_info_button</c>. The 11-byte frame is read bounded and logged and <b>nothing is
    /// answered</b>: the 7.3 client has no handler for the only candidate answer (602), NGemity declares
    /// 604 and never reads it, and 604 changes no state — a reply would be an invented policy. The code
    /// is deliberately not judged against <c>CharacterQuests</c>: no response consumes that verdict.
    /// Synchronous on purpose, with no database access on this path.
    /// See docs/packet-specs/604-quest-info.md §5.3, §5.5.
    /// </summary>
    private void HandleQuestInfo(byte[] packet)
    {
        if (!GameActionPackets.TryReadQuestInfo(packet, out var request))
        {
            _logger.Warning("Malformed quest info request received from {clientTag} (Length: {length})",
                ClientTag, packet.Length);
            return;
        }

        _logger.Debug("TM_CS_QUEST_INFO ({id}) Length: {length} received from {clientTag}: code={code}",
            (ushort)GamePackets.TM_CS_QUEST_INFO, packet.Length, ClientTag, request.Code);
    }

    /// <summary>
    /// <c>TM_CS_END_QUEST</c> (605): the confirmation button of the quest window. A frame of another length
    /// than 12 is refused with <c>InvalidArgument</c> like a malformed 603; everything else is judged by the
    /// quest service. See docs/packet-specs/605-end-quest.md.
    /// </summary>
    private async Task HandleEndQuestAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadEndQuest(packet, out var request))
        {
            _logger.Warning("Malformed end quest request received from {clientTag} (Length: {length})",
                ClientTag, packet.Length);
            SendResult((ushort)GamePackets.TM_CS_END_QUEST, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.QuestService.EndQuestAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process end quest for {clientTag}", ClientTag);
        }
    }

    /// <summary>
    /// TM_CS_AUCTION_INSTANT_PURCHASE (1308): the player buys an announcement at its fixed price, the act
    /// the two auction windows emit right next to the bid — SUIAuctionSearchWnd (slot 6, 0x4FEB60) and
    /// SUIAuctionTenderWnd (slot 6, 0x501220). The eleven-byte frame (spec §3.1) is read and logged, and
    /// nothing is executed: the announcement it names, its writer and the rules of the exchange (the
    /// leading bidder's fate, the item's destination, the registration tax) belong to lots that do not
    /// exist here, and <c>TM_CS_AUCTION_REGISTER</c> (1309) is not even declared (spec §5.5, §5.6, §12).
    /// No <c>ResultCode</c> is sent for a well-formed frame either: with nothing executed, <c>Success</c>
    /// would claim a purchase that never happened and the refusal code is not established (spec §7.1),
    /// while the client reads a generic TM_SC_RESULT (id 0) naming 1308 — the one frame its result
    /// handler 0x66DB80 reserves a case for (spec §5.4).
    /// </summary>
    private void HandleAuctionInstantPurchase(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionInstantPurchase(buffer, out var auctionUid))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_INSTANT_PURCHASE ({id}) Length: {length} received from {clientTag}: auction_uid={auctionUid}",
                (ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE, buffer.Length, ClientTag, auctionUid);
        }

        if (_networkService.AuctionService is { } auctions)
            _ = auctions.InstantPurchaseAsync(this, unchecked((int)auctionUid));
    }

    /// <summary>
    /// <c>TM_CS_START_BOOTH</c> (700). The frame is read and judged before anything is stored, and a
    /// refusal is answered with <c>TS_SC_RESULT</c> carrying the request id, because the family has no
    /// acknowledgement packet at all: <c>703</c>, <c>708</c>, <c>709</c> and <c>710</c> are the only
    /// answers the 7.3 client can receive, and an accepted <c>700</c> is answered with nothing
    /// (docs/packet-specs/socle-booths.md §5.3 points 4 and 5).
    /// </summary>
    private void HandleStartBooth(byte[] packet)
    {
        if (!BoothRules.TryAcceptStartBooth(packet, ConnectionInfo.CharacterLevel, out var request,
                out var result))
        {
            _logger.Debug("TM_CS_START_BOOTH refused for {clientTag}: {result}", ClientTag, result);
            SendResult((ushort)GamePackets.TM_CS_START_BOOTH, (ushort)result);
            return;
        }

        ConnectionInfo.OpenBooth(request);
        _logger.Debug("Booth opened by {clientTag}: type={type}, items={count}, nameLength={nameLength}",
            ClientTag, request.Type, request.Items.Length, request.Name.Length);

        // The booth becomes visible: the owner's status mask gains its booth bit for the owner and for
        // every client that sees it (docs/packet-specs/705-buy-from-booth.md §5.1).
        _networkService.BoothTradeService.PublishBoothStatus(this);
    }

    /// <summary>
    /// <c>TM_CS_STOP_BOOTH</c> (701). Closing with no booth open stays idempotent and answers
    /// <c>Success</c>: it is a tranché choice, no source fixes it (docs/packet-specs/socle-booths.md
    /// §7.4). The items the client declared are forgotten and nothing is persisted — a booth does not
    /// survive the session.
    /// </summary>
    private void HandleStopBooth(byte[] packet)
    {
        if (!BoothPackets.TryReadStopBooth(packet))
        {
            SendResult((ushort)GamePackets.TM_CS_STOP_BOOTH, (ushort)ResultCode.InvalidArgument);
            return;
        }

        var wasOpen = ConnectionInfo.IsBoothOpen;
        _networkService.BoothTradeService.CloseBooth(this);
        _logger.Debug("TM_CS_STOP_BOOTH from {clientTag}: booth was {state}", ClientTag,
            wasOpen ? "open" : "already closed");
        SendResult((ushort)GamePackets.TM_CS_STOP_BOOTH, (ushort)ResultCode.Success);
    }

    /// <summary>
    /// <c>TM_CS_CHECK_BOOTH_STARTABLE</c> (711), the client's "can a booth be started here?" frame.
    /// It is 7 bytes — the header and nothing else — and the 7.3 client <b>does</b> send it (frame
    /// builder <c>VA 0x48CFD0</c>, one call site <c>0x49A176</c>), even though its incoming
    /// dispatcher stops at 710 and no reference holds an answer. The frame is therefore read and
    /// bounded and <b>nothing is answered</b>: refusing a booth start stays
    /// <c>HandleStartBooth</c> → <c>BoothRules</c> → <c>TS_SC_RESULT(700, code)</c>, and no zone,
    /// level, distance or booth-state rule is established for this packet. A frame whose declared
    /// length is not 7 is logged and dropped rather than refused.
    /// See docs/packet-specs/711-check-booth-startable.md §5.2 and §5.3.
    /// </summary>
    private void HandleCheckBoothStartable(byte[] packet)
    {
        if (!BoothPackets.TryReadCheckBoothStartable(packet))
        {
            _logger.Warning(
                "Malformed TM_CS_CHECK_BOOTH_STARTABLE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE, packet.Length, ClientTag);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug("TM_CS_CHECK_BOOTH_STARTABLE ({id}) Length: {length} received from {clientTag}",
                (ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE, packet.Length, ClientTag);
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

    private async Task HandleStorageAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadStorage(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_STORAGE, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.StorageService.HandleAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process storage request for {clientTag}", ClientTag);
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
            if (request.TargetHandle != 0 && request.TargetHandle != ConnectionInfo.CharacterHandle
                && _networkService.CreatureService is { } creatures)
            {
                await creatures.UnequipItemAsync(this, request);
            }
            else
            {
                await _networkService.EquipmentService.UnequipAsync(this, request);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process unequip for {clientTag}", ClientTag);
        }
    }

    /// <summary>
    /// <c>TM_CS_PUTOFF_CARD</c> (215). The single payload byte is an ordinal 0..5 computed by the client
    /// inside its own six-entry object table, or 0xFF when the target handle is not in it (spec §2.4).
    /// What those six entries hold is not established (spec §7.a) and no primitive on <c>master</c> can
    /// clear a socket and hand the stone back atomically (spec §5.4), so the value is logged and nothing
    /// else happens: reading it as an <c>ItemWearType</c> or an inventory index is unsupported. 0xFF, and
    /// any byte outside the established 0..5 domain, is refused with an explicit answer.
    /// </summary>
    private void HandlePutoffCard(byte[] packet)
    {
        if (!GameActionPackets.TryReadPutoffCard(packet, out var position))
        {
            SendResult((ushort)GamePackets.TM_CS_PUTOFF_CARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (position == GameActionPackets.PutoffCardNotInTable)
        {
            _logger.Warning(
                "TM_CS_PUTOFF_CARD ({id}) from {clientTag}: 0xFF, the target handle is not in the client's object table — refused",
                (ushort)GamePackets.TM_CS_PUTOFF_CARD, ClientTag);

            SendResult((ushort)GamePackets.TM_CS_PUTOFF_CARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (position < 0 || position > GameActionPackets.PutoffCardMaxPosition)
        {
            _logger.Warning(
                "TM_CS_PUTOFF_CARD ({id}) from {clientTag}: position {position} is outside the established 0..{max} domain — refused",
                (ushort)GamePackets.TM_CS_PUTOFF_CARD, ClientTag, position,
                GameActionPackets.PutoffCardMaxPosition);

            SendResult((ushort)GamePackets.TM_CS_PUTOFF_CARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        _logger.Information(
            "TM_CS_PUTOFF_CARD ({id}) from {clientTag}: position {position} — no socket is cleared, the ordinal's meaning is not established (spec §7.a, §7.b)",
            (ushort)GamePackets.TM_CS_PUTOFF_CARD, ClientTag, position);
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

    /// <summary>
    /// TM_CS_AUCTION_REGISTER (1309): the player puts an item up for auction, the act the register button
    /// of SUIAuctionRegisterWnd emits (internal key 1160 = 0x488, sender 0x48DF30). The thirty-two byte
    /// frame (sheet §3.1) is read and logged, and nothing is executed: no service, repository or loading
    /// path of this repository can write an announcement — <c>DbSet&lt;AuctionEntity&gt; Auctions</c> is
    /// never read nor written, there is no AuctionRepository, and nothing computes EndTime from a
    /// duration_type, levies RegistrationTax or moves the item into the auction storage (sheet §5.5).
    /// The rules that would make the write legitimate are the open decisions of sheet §8 (durations, tax,
    /// price bounds, announcement caps, refusal codes, who refreshes the listing), so this lot stops at
    /// decoding and refusing.
    /// <para>
    /// A frame the reader rejects — any length but the exact thirty-two bytes — is answered with
    /// <c>TM_SC_RESULT</c> carrying the request id, the one frame the client knows for this act: 1309 has
    /// no dedicated answer in any reference and its result handler logs the case (sheet §5.3, §5.4). A
    /// well formed frame gets <b>no</b> answer at all: answering <c>Success</c> would claim a registration
    /// that never happened, and the business refusal vocabulary is not established (sheet §5.4, §8 q5).
    /// </para>
    /// </summary>
    private void HandleAuctionRegister(byte[] buffer)
    {
        if (!GameAuctionPackets.TryReadAuctionRegister(buffer, out var itemHandle, out var itemCount,
                out var startPrice, out var instantPurchasePrice, out var durationType))
        {
            SendResult((ushort)GamePackets.TM_CS_AUCTION_REGISTER, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_AUCTION_REGISTER ({id}) Length: {length} received from {clientTag}: " +
                "item_handle={itemHandle}, item_count={itemCount}, start_price={startPrice}, " +
                "instant_purchase_price={instantPurchasePrice}, duration_type={durationType}",
                (ushort)GamePackets.TM_CS_AUCTION_REGISTER, buffer.Length, ClientTag, itemHandle, itemCount,
                startPrice, instantPurchasePrice, durationType);
        }

        // The measured domain of duration_type is {1, 2, 3} — three option buttons, index + 1, 1 when the
        // player leaves the group untouched (sheet §3.5) — so anything else is a frame the 7.3 client
        // cannot build. It is only reported, never refused here: whether 0 means "unspecified" and may be
        // accepted, and what each value is worth in time, are decisions of Killian (sheet §7a, §8 q1), and
        // deciding them in code would invent a rule. Same restraint for item_count <= 0 and for negative
        // prices, both left unjudged by the reader (sheet §7b, §7c).
        if (durationType is not (1 or 2 or 3) && _logger.IsEnabled(LogEventLevel.Warning))
        {
            _logger.Warning(
                "TM_CS_AUCTION_REGISTER from {clientTag}: duration_type={durationType} is outside the " +
                "measured domain 1..3 of the 7.3 client", ClientTag, durationType);
        }

        // onAuctionRegister: anything but 2 (mid) and 3 (long) runs for the short duration.
        if (_networkService.AuctionService is { } auctions)
            _ = auctions.RegisterAsync(this, itemHandle, itemCount, startPrice, instantPurchasePrice, durationType);
    }

    /// <summary>TM_CS_ITEM_KEEPING_LIST (1350) / TM_CS_ITEM_KEEPING_TAKE (1352): the auction keeping box (11 bytes each).</summary>
    private void HandleItemKeeping(byte[] buffer, bool take)
    {
        var id = take ? GamePackets.TM_CS_ITEM_KEEPING_TAKE : GamePackets.TM_CS_ITEM_KEEPING_LIST;
        if (!GameAuctionPackets.TryReadItemKeepingRequest(buffer, out var value))
        {
            SendResult((ushort)id, (ushort)ResultCode.InvalidArgument);
            return;
        }

        if (_networkService.AuctionService is not { } auctions) return;
        _ = take ? auctions.TakeAsync(this, value) : auctions.KeepingListAsync(this, value);
    }

    private async Task HandleBuyItemAsync(byte[] packet)
    {
        if (!GameTradePackets.TryReadBuyItem(packet, out var itemCode, out var buyCount))
        {
            SendResult((ushort)GamePackets.TM_CS_BUY_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.MarketTradeService.BuyAsync(this, itemCode, buyCount);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process item purchase for {clientTag}", ClientTag);
        }
    }

    private async Task HandleDonateItemAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadDonateItem(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_DONATE_ITEM, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.ItemDonateService.DonateAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process item donation for {clientTag}", ClientTag);
        }
    }

    private async Task HandleBindSkillCardAsync(byte[] packet)
    {
        if (!GameActionPackets.TryReadBindSkillCard(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_BIND_SKILLCARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.SkillCardService.BindAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process skill card bind for {clientTag}", ClientTag);
        }
    }

    private async Task HandleUnbindSkillCardAsync(byte[] packet)
    {
        // The 7.3 client builds this request with Length = 15 and writes nothing after offset 14, so a
        // shorter frame is refused rather than partially read (spec §5.2 b).
        if (!GameActionPackets.TryReadUnbindSkillCard(packet, out var request))
        {
            SendResult((ushort)GamePackets.TM_CS_UNBIND_SKILLCARD, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            await _networkService.SkillCardService.UnbindAsync(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process skill card unbind for {clientTag}", ClientTag);
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

    private void HandleRemoveState(byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_REQUEST_REMOVE_STATE;
        if (!GameActionPackets.TryReadRemoveState(packet, out var request))
        {
            // The frame is fixed-size: a 408 of any other length is not a removal request.
            SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        try
        {
            _networkService.SkillCastService.RemoveState(this, request);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not process state removal {stateCode} for {clientTag}",
                request.StateCode, ClientTag);
            SendResult(requestId, (ushort)ResultCode.Misc, request.StateCode);
        }
    }

    /// <summary>
    /// TM_CS_HUNTAHOLIC_BEGIN_HUNTING (4011): the "start the hunt" gesture a player triggers from the
    /// HuntaHolic instance window (the 7.3 client sends it from a single site, control
    /// <c>button_entrance_01</c> of <c>SUIHuntaHolicInstanceWnd</c>, handler <c>0x5641b0</c>, call
    /// <c>0x564238</c>). The frame is 7 bytes — a bare header, no field at all — so there is nothing to read
    /// out of it: only the exact length is checked.
    /// <para>
    /// Nothing is answered and nothing changes state. No reference answers a 4011: the client's own receive
    /// dispatcher routes the id to its "unhandled message" branch, so a server to client 4011 would be logged
    /// by the client as unknown rather than acted upon, and no other packet of the family carries this answer.
    /// The play flow the server will eventually emit (4012 then 4009 then 4007/4006, then 4010) has its formats
    /// established by docs/packet-specs/socle-instances-jeu.md §3.4.6-3.4.8 but its order, its delays and its
    /// recipient are not established by any source, so none of it is invented here and no hunt state is kept.
    /// </para>
    /// <para>
    /// Reserve, not a check to add: the server has no HuntaHolic lobby or instance state yet, so it cannot tell
    /// whether the sender is inside an instance at all. No refusal is fabricated for that case; when the lobby
    /// state lands (lot S2 and the rest of S5) the context check belongs to that lot.
    /// </para>
    /// See docs/packet-specs/4011-huntaholic-begin-hunting.md §2.2, §5.2, §5.3, §5.5, §7b, §7c.
    /// </summary>
    private void HandleHuntaholicBeginHunting(byte[] buffer)
    {
        if (!GameHuntaholicPackets.IsBeginHunting(buffer))
        {
            _logger.Warning("Malformed HuntaHolic begin hunting request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug("TM_CS_HUNTAHOLIC_BEGIN_HUNTING ({id}) Length: {length} received from {clientTag}",
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING, buffer.Length, ClientTag);
        _networkService.HuntaholicService?.BeginHunting(this);
    }

    /// <summary>TM_CS_HUNTAHOLIC_LEAVE_LOBBY (4008), header only: out of the lobby (socle-huntaholic.md §3).</summary>
    private void HandleHuntaholicLeaveLobby(byte[] buffer)
    {
        if (!GameInstanceGamePackets.HasNoPayload(buffer))
        {
            _logger.Warning("Malformed HuntaHolic leave lobby request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _networkService.HuntaholicService?.LeaveLobby(this);
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
            // A 402 naming one of this client's summons is the summon's learning (StructSummon::IsLearnableSkill).
            if (_networkService.CreatureService is not { } creatures || !await creatures.TryLearnSkillAsync(this, request))
            {
                await _networkService.SkillService.LearnAsync(this, request);
            }

            if (_networkService.QuestService is not null) await _networkService.QuestService.RefreshAsync(this);
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
        _networkService.HuntaholicService?.InstanceGameEnter(this, request.InstanceGameType);
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
        _networkService.HuntaholicService?.InstanceGameExit(this);
    }

    /// <summary>
    /// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the "leave the instance" gesture of the HuntaHolic family,
    /// emitted by the 7.3 client from its instance window and from its scoreboard window. The frame is 7 bytes
    /// — a bare header, no field at all — so there is nothing to read out of it: only the exact length is
    /// checked.
    /// <para>
    /// Nothing is answered and nothing changes state. No reference answers a 4005: the client's own receive
    /// dispatcher routes the id to its "unhandled message" branch, so a server to client 4005 would be logged
    /// by the client as unknown rather than acted upon, and no other packet of the family carries this answer.
    /// The server holds no HuntaHolic lobby or instance state yet, so there is literally nothing to leave —
    /// the same no-op disposition as <see cref="HandleInstanceGameExit"/>. The packet that will eventually
    /// bring the character back (a warp? a location change?) is not established and is Killian's call when the
    /// lobby state lands, so none is invented here.
    /// </para>
    /// See docs/packet-specs/4005-huntaholic-leave-instance.md §5.2, §5.3, §7a.
    /// </summary>
    private void HandleHuntaholicLeaveInstance(byte[] buffer)
    {
        if (!GameHuntaholicPackets.IsLeaveInstance(buffer))
        {
            _logger.Warning("Malformed HuntaHolic leave instance request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug("TM_CS_HUNTAHOLIC_LEAVE_INSTANCE ({id}) Length: {length} received from {clientTag}",
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE, buffer.Length, ClientTag);
        _networkService.HuntaholicService?.LeaveInstance(this);
    }

    /// <summary>
    /// TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252) is answered by TM_SC_INSTANCE_GAME_SCORE_REQUEST (4253) and by
    /// nothing else: the 4253 is never sent unsolicited. Only <c>holicpoint</c> has a source in 7.3
    /// (CharacterEntity.HuntaholicPoint, the same value the login sequence publishes as the client property
    /// <c>huntaholicpoint</c>). <c>bearroad_ranking</c>, <c>deathmatch_kill_count</c> and
    /// <c>deathmatch_death_count</c> have no source anywhere in 7.3, so they are written as zero — an explicit
    /// placeholder, not a scoring policy. See NON ÉTABLI (h) of docs/packet-specs/socle-instances-jeu.md.
    /// </summary>
    private async Task HandleInstanceGameScoreRequestAsync(byte[] buffer)
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

        // onInstanceGameScoreRequest: the session's points (a purchase or a hunt may have moved them since the last
        // save) and the rank among the online players, the RankingManager this server does not have.
        var holicPoint = GameInstanceGamePackets.ToWireHolicPoint(ConnectionInfo.HuntaholicPoint);
        var ranking = _networkService.HuntaholicService?.Ranking(this) ?? 0u;
        await Task.CompletedTask;

        Connection.Send(GameInstanceGamePackets.BuildScoreResponse(holicPoint, ranking, 0u, 0u));
        _logger.Debug(
            "TM_SC_INSTANCE_GAME_SCORE_REQUEST ({id}) Length: {length} sent to {clientTag}: holicpoint={holicpoint}",
            (ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST, GameInstanceGamePackets.ScoreResponseLength,
            ClientTag, holicPoint);
    }

    /// <summary>
    /// TM_CS_HUNTAHOLIC_INSTANCE_LIST (4000): the HuntaHolic lobby window asks for a page of its room list —
    /// page 1 when it opens, then the page the previous answer clamped to. The frame is 11 bytes — a 7-byte
    /// header plus a single signed <c>int32</c> <c>page</c> at offset 7. It is read and bounded, and the page
    /// is logged raw so that a client capture tells what the field carries.
    /// <para>
    /// Nothing is answered. The client's lobby is fed by <c>TM_SC_HUNTAHOLIC_INSTANCE_LIST</c> (4001), and the
    /// three decisions that frame needs — the rooms to list, the pagination and the <c>huntaholic_id</c> — have
    /// no source in the repository: the server holds neither a <c>HuntaholicResource</c> nor a
    /// <c>HuntaholicInstanceResource</c> entity, and the client does not send the lobby id. No reference shows
    /// any server answer either: NGemity declares 4000 and never handles it, rzu only ships the headers.
    /// Nor does the client react to anything else: it is absent from its own <c>MSG_RESULT</c> cascade, so
    /// reading and logging an unanswered 4000 is invisible to the player.
    /// See docs/packet-specs/4000-huntaholic-instance-list.md §5.3, §5.5, §7a §7b §7c.
    /// </para>
    /// </summary>
    private void HandleHuntaholicInstanceList(byte[] buffer)
    {
        if (!GameHuntaholicPackets.TryReadHuntaholicInstanceList(buffer, out var page))
        {
            _logger.Warning("Malformed HuntaHolic instance list request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_HUNTAHOLIC_INSTANCE_LIST ({id}) Length: {length} page={page} received from {clientTag}",
                (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST, buffer.Length, page, ClientTag);
        }

        _networkService.HuntaholicService?.InstanceList(this, page);
    }

    /// <summary>
    /// TM_CS_SECURITY_NO (9005): the client answers TM_SC_REQUEST_SECURITY_NO (9004) with the security
    /// password the player typed, carrying back the mode it received and the code in a fixed 19-byte
    /// container. No reference implements the answer of the game server — rzu verifies the code on the
    /// authentication server (40001 -> 40000) and Navislamia has neither that transport nor any storage for
    /// the code — so the frame is read and bounded and nothing is verified, stored, answered or sanctioned
    /// (docs/packet-specs/9005-security-no.md §5.3, §5.4).
    /// <para>
    /// The code is a reusable secret that guards character deletion and the warehouse alike. Only the mode
    /// and the code's length are logged; the code is never turned into a string (an immutable copy nothing
    /// could wipe), and this frame — the one plaintext copy the receive loop hands over — is zeroed as soon
    /// as it has been read. <c>Connection.Read</c> wipes the receive buffer's copy.
    /// </para>
    /// </summary>
    private void HandleSecurityNo(byte[] buffer)
    {
        try
        {
            if (!GameSecurityPackets.TryReadSecurityNo(buffer, out var mode, out var securityNo))
            {
                _logger.Warning("Malformed security password received from {clientTag} (Length: {length})",
                    ClientTag, buffer.Length);
                return;
            }

            _logger.Debug(
                "TM_CS_SECURITY_NO ({id}) Length: {length} received from {clientTag}: mode={mode} securityNoLength={securityNoLength}",
                (ushort)GamePackets.TM_CS_SECURITY_NO, buffer.Length, ClientTag, mode, securityNo.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>
    /// TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004): the player picked a room in the HuntaHolic lobby list and the
    /// client asks to enter it. The frame is 28 bytes — a 7-byte header, the signed <c>int32</c>
    /// <c>instance_no</c> copied out of the 38-byte entry the list answer (4001) carries, and a fixed 17-byte
    /// password buffer which is all zeroes for a public room.
    /// <para>
    /// The frame is read, bounded and logged, and nothing is answered. The only refusal instrument the 7.3
    /// client understands is a result message whose <c>request_msg_id</c> is 4004 and whose <c>result</c> is
    /// <c>0x20</c>, and the notice it then prints is the room <i>creation</i> one ("You cannot create the room
    /// because you have used all of your entries for the day."): with no lobby state the server cannot tell
    /// whether a quota is exhausted, so emitting it would display a false reason. Entering a room has no other
    /// observable client effect — it shows up through 4001/4002 (lot S2), which are not integrated yet.
    /// </para>
    /// <para>
    /// The password is never carried out of the reader and never logged; only its presence is, which is what
    /// tells a public room from an attempt on a locked one.
    /// </para>
    /// See docs/packet-specs/4004-huntaholic-join-instance.md §5.2, §5.3.
    /// </summary>
    private void HandleHuntaholicJoinInstance(byte[] buffer)
    {
        if (!GameHuntaholicPackets.TryReadJoinInstance(buffer, out var request))
        {
            _logger.Warning(
                "Malformed HuntaHolic join instance request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        // Five properties: guarded, or the argument array is built before the level check.
        if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug(
                "TM_CS_HUNTAHOLIC_JOIN_INSTANCE ({id}) Length: {length} received from {clientTag}: " +
                "instanceNo={instanceNo} hasPassword={hasPassword}",
                (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE, buffer.Length, ClientTag, request.InstanceNo,
                request.HasPassword);
        }

        _networkService.HuntaholicService?.JoinInstance(this, request.InstanceNo,
            GameHuntaholicServerPackets.ReadJoinPassword(buffer));
    }

    /// <summary>
    /// TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003): the player validated the HuntaHolic room creation window. The
    /// socle reads, validates and logs the frame (room name, maximum member count, whether a password was
    /// supplied — the password itself is never logged) and answers nothing.
    ///
    /// Nothing is answered because no form of answer is established: neither rzu nor NGemity declares a server
    /// to client packet for 4003, and the only client reaction identified — the daily quota refusal, carried by
    /// a 16-bit field worth 0x20 inside a MSG_RESULT whose id is 4003 — has an unestablished wire layout, so no
    /// result frame is invented. A created room would only become visible through 4001/4002 (lot S2), which are
    /// not integrated yet. See docs/packet-specs/4003-huntaholic-create-instance.md §5.3.
    /// </summary>
    private void HandleHuntaholicCreateInstance(byte[] buffer)
    {
        if (!GameHuntaholicPackets.TryReadCreateInstance(buffer, out var request))
        {
            _logger.Warning("Malformed HuntaHolic create instance request received from {clientTag} (Length: {length})",
                ClientTag, buffer.Length);
            return;
        }

        _logger.Debug(
            "TM_CS_HUNTAHOLIC_CREATE_INSTANCE ({id}) Length: {length} received from {clientTag}: " +
            "name={name} maxMemberCount={maxMemberCount} hasPassword={hasPassword}",
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE, buffer.Length, ClientTag, request.Name,
            request.MaxMemberCount, request.HasPassword);
        _networkService.HuntaholicService?.CreateInstance(this, request.Name, request.MaxMemberCount,
            GameHuntaholicServerPackets.ReadCreatePassword(buffer));
    }

    private static readonly int HeaderLength = Marshal.SizeOf<Header>();

    /// <summary>The receive buffer's size: a frame larger than it can never be assembled.</summary>
    private const int MaxFrameLength = 32768;

    /// <summary>
    /// Every declared <see cref="GamePackets"/> id, indexed by id. <c>Enum.IsDefined</c> boxed the id and
    /// went through reflection on every packet received; this is one array read.
    /// </summary>
    private static readonly bool[] DefinedPackets = BuildDefinedPackets();

    private static bool[] BuildDefinedPackets()
    {
        var defined = new bool[ushort.MaxValue + 1];
        foreach (var id in Enum.GetValues<GamePackets>())
        {
            defined[(ushort)id] = true;
        }

        return defined;
    }

    public override void OnDataReceived(int bytesReceived)
    {
        var remainingData = bytesReceived;

        while (remainingData >= Marshal.SizeOf<Header>())
        {
            var header = new Header(Connection.Peek(HeaderLength));
            var isValidMsg = header.Checksum == header.CalculateChecksum();

            // A length shorter than its own header never advances the loop (Read(0) forever, on the I/O
            // thread), and one larger than the receive buffer can never complete.
            if (isValidMsg && (header.Length < HeaderLength || header.Length > MaxFrameLength))
            {
                _logger.Error("Invalid frame length {length} (ID: {id}) received from {clientTag}", header.Length,
                    header.ID, ClientTag);
                Connection.Disconnect();
                return;
            }

            if (header.Length > remainingData)
            {
                _logger.Verbose(
                    "Waiting for rest of packet from {clientTag} (ID: {id} Length: {length} Available: {remaining})",
                    ClientTag, header.ID, header.Length, remainingData);

                return;
            }

            if (!isValidMsg)
            {
                // The stream is desynchronised: nothing after this point can be framed. Disconnect and stop
                // here instead of throwing out of the receive callback.
                _logger.Error("Invalid Message received from {clientTag} !!!", ClientTag);
                Connection.Disconnect();
                return;
            }

            var msgBuffer = Connection.Read((int)header.Length);

            remainingData -= msgBuffer.Length;

            if (!DefinedPackets[header.ID])
            {
                _logger.Debug("Undefined packet ID: {id} Length: {length}) received from {clientTag}", header.ID, header.Length, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_NONE)
            {
                _logger.Verbose("Keepalive (TM_NONE) Length: {length} from {clientTag}", header.Length, ClientTag);
                continue;
            }

            // TM_CS_SECURITY_NO (9005) is declared so that the frame is read and bounded instead of being
            // dropped as an undefined id. Any arm for a declared id must run before the throwing switch
            // below: a member of GamePackets that reaches it breaks the receive loop. Nothing is answered
            // and nothing is verified.
            if (header.ID == (ushort)GamePackets.TM_CS_SECURITY_NO)
            {
                HandleSecurityNo(msgBuffer);
                continue;
            }

            // One booth gate in front of the whole chain: while a booth is open, every action the client
            // itself announces as refused (smsg_booth_not_use_item / _use_skill / _not_action) is answered
            // with 55 (ResultCode.NotActableWhileUsingBooth) and nothing else runs. TM_CS_STOP_BOOTH (701)
            // is deliberately outside the set: it is the way out of the lock.
            var boothGate = BoothRules.GateAction(ConnectionInfo.IsBoothOpen, header.ID);
            if (boothGate != ResultCode.Success)
            {
                _logger.Debug("{id} refused for {clientTag}: booth open ({result})", header.ID, ClientTag, boothGate);
                SendResult(header.ID, (ushort)boothGate);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_GAME_TIME)
            {
                SendGameTime();
                continue;
            }

            // TM_CS_CHANGE_SUMMON_NAME (323), the summon rename request. The 7.3 client does send it: its
            // only frame builder writes the id 0x143 and the length 0x1a in hard (SFrame.exe 0x48c5e0,
            // fiche §2.1), and the frame carries the new name and nothing else — no handle, no slot index,
            // so no target travels with it (fiche §3.3). This lot reads and journals the frame, and stops
            // there on purpose:
            //   * nothing is answered — none of rzu, NGemity and the client's incoming dispatcher holds a
            //     reply to 323, so neither a refusal nor a re-publication is invented (fiche §7(c));
            //   * no summon row is written — which of a character's two summons (MainSummon / SubSummon) a
            //     rename targets is not settled (fiche §7(d)), and neither the accepted length of a name nor
            //     its uniqueness is (fiche §7(e)), so no rename policy is applied rather than guessed;
            //   * the id is declared and routed all the same, so that a real frame is read and can never
            //     reach the "Unknown Packet Type" throw below.
            // The arm sits at the head of the chain rather than in the summon region of the tail, whose
            // insertion zone the sibling summon branches already share.
            // See docs/packet-specs/323-change-summon-name.md.
            if (header.ID == (ushort)GamePackets.TM_CS_CHANGE_SUMMON_NAME)
            {
                if (GameActionPackets.TryReadChangeSummonName(msgBuffer, out var summonName))
                {
                    _logger.Debug(
                        "TM_CS_CHANGE_SUMMON_NAME ({id}) Length: {length} received from {clientTag}: name=\"{name}\" (read only: target and name policy not established)",
                        header.ID, header.Length, ClientTag, summonName);
                }
                else
                {
                    _logger.Warning("Malformed TM_CS_CHANGE_SUMMON_NAME ({id}) Length: {length} received from {clientTag}",
                        header.ID, header.Length, ClientTag);
                }

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

            // TM_CS_HUNTAHOLIC_BEGIN_HUNTING (4011): the "start the hunt" gesture of the HuntaHolic instance
            // window. The frame is a bare 7-byte header, so it is read, bounded and logged, and nothing is
            // answered — the client has no receive arm for this id, so any answer would be logged by it as an
            // unknown message, and the server keeps no hunt state. It sits next to the region arms rather than
            // at the end of the chain, whose insertion zone the rest of the HuntaHolic family and the sibling
            // branches already share. It must stay before the throwing switch below: a member of GamePackets
            // that reaches it breaks the receive loop.
            // See docs/packet-specs/4011-huntaholic-begin-hunting.md.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING)
            {
                HandleHuntaholicBeginHunting(msgBuffer);
                continue;
            }

            // TM_SC_REGION_ACK is a server to client packet: the 7.3 client never sends it. An incoming one
            // is a protocol anomaly, not a request, so it is logged and dropped instead of reaching the
            // "Unknown Packet Type" throw below.
            // The duel's five server to client frames (4501, 4503-4506): never sent by the client.
            if (header.ID is (ushort)GamePackets.TM_SC_COMPETE_REQUEST or (ushort)GamePackets.TM_SC_COMPETE_ANSWER
                or (ushort)GamePackets.TM_SC_COMPETE_COUNTDOWN or (ushort)GamePackets.TM_SC_COMPETE_START
                or (ushort)GamePackets.TM_SC_COMPETE_END)
            {
                _logger.Warning("Server to client compete packet ({id}) received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_SC_REGION_ACK)
            {
                _logger.Warning("Server to client packet TM_SC_REGION_ACK ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            // TM_SC_GET_CHAOS (213) and TM_SC_ITEM_DROP_INFO (282): the two frames of a monster's death
            // (docs/packet-specs/socle-recompenses-monstres.md §3.2, §3.5). Both are server to client only —
            // the 7.3 client builds neither — so an incoming one is a protocol anomaly, logged and dropped
            // rather than reaching the "Unknown Packet Type" throw below. The arm sits next to the isolated
            // TM_SC_REGION_ACK one, out of the insertion zone the sibling branches share.
            if (header.ID is (ushort)GamePackets.TM_SC_GET_CHAOS or (ushort)GamePackets.TM_SC_ITEM_DROP_INFO)
            {
                _logger.Warning("Server to client packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            // TM_CS_SUMMON (304): rzu declares the frame, but the 7.3 client never builds it (summoning
            // goes through the summon creature skill, TM_CS_SKILL = 400) and NGemity has no handler for it
            // either ("Got unknown packet"). The id is declared and routed here so that a frame cannot reach
            // the "Unknown Packet Type" throw below; the frame is read and logged, and nothing is answered:
            // no reference sanctions a response and the client has no incoming handler for 304. The arm sits
            // next to the isolated TM_SC_REGION_ACK arm rather than at the end of the chain, whose insertion
            // zone the rest of the summon family and the sibling branches already share.
            // See docs/packet-specs/304-summon.md §5.3, §5.4.
            if (header.ID == (ushort)GamePackets.TM_CS_SUMMON)
            {
                // is_summon and card_handle are exposed raw: neither rzu nor NGemity says what they mean and
                // nothing here decides summoning, unsummoning or card consumption (the summon path already
                // exists through TM_CS_SKILL). A frame shorter than the declared 12 bytes cannot be read and
                // is only logged; no refusal is emitted, as no refusal rule is established (§5.3.4).
                if (GameActionPackets.TryReadSummon(msgBuffer, out var isSummon, out var cardHandle))
                {
                    // Five properties: guarded, or the argument array is built before the level check.
                    if (_logger.IsEnabled(LogEventLevel.Debug))
                    {
                        _logger.Debug(
                            "TM_CS_SUMMON ({id}) Length: {length} received from {clientTag}: is_summon={isSummon} card_handle={cardHandle}",
                            header.ID, header.Length, ClientTag, isSummon, cardHandle);
                    }
                }
                else
                {
                    _logger.Warning("Malformed TM_CS_SUMMON ({id}) Length: {length} received from {clientTag}",
                        header.ID, header.Length, ClientTag);
                }

                continue;
            }

            // TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004): the "enter this room" frame of the HuntaHolic lobby. It is
            // read, bounded and logged, and nothing is answered — the only refusal instrument the client
            // understands would print the room *creation* notice, and entering a room shows up through
            // 4001/4002 (lot S2) only. Like the TM_CS_SUMMON arm above, it sits next to the isolated
            // TM_SC_REGION_ACK arm rather than at the end of the chain, whose insertion zone the rest of the
            // HuntaHolic family and the sibling branches already share. It must stay before the throwing switch
            // below: a member of GamePackets that reaches it breaks the receive loop.
            // See docs/packet-specs/4004-huntaholic-join-instance.md.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE)
            {
                HandleHuntaholicJoinInstance(msgBuffer);
                continue;
            }

            // TM_CS_REQUEST (60) is declared so that the frame is read and bounded instead of being dropped
            // as an undefined id. Any arm for a declared id must run before the throwing switch below: a
            // member of GamePackets that reaches it breaks the receive loop. Nothing is answered and
            // nothing is executed — the command is opaque.
            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST)
            {
                HandleRequest(msgBuffer);
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

            // TM_CS_AUCTION_SELLING_LIST (1302) is the request of that same family, eleven bytes: its
            // arm sits with the responses above rather than with the unrelated handlers near the switch.
            // It must stay before the throwing switch below in any case: an id declared in GamePackets
            // with no arm reaches "Unknown Packet Type" and kills the receive loop.
            // See docs/packet-specs/1302-auction-selling-list.md §5.3.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST)
            {
                HandleAuctionSellingList(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM)
            {
                HandleTakeoutCommercialItem(msgBuffer);
                continue;
            }

            // TM_CS_AUCTION_BIDDED_LIST (1304) is the request of the same family as the 1302 one above,
            // eleven bytes and the same client code with another Id. It must stay before the throwing
            // switch below in any case: an id declared in GamePackets with no arm reaches "Unknown Packet
            // Type" and kills the receive loop. Its anchor here — between two unrelated arms rather than
            // right after the family's response guard — is deliberate: the sibling lots 1300
            // (immediately above the switch) and 1302 (right after the response guard) each take one of
            // those two places, so this one keeps the three-way merge of the three lots free of
            // conflicts (measured, sheet §14.6).
            // See docs/packet-specs/1304-auction-bidded-list.md §5.3.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST)
            {
                HandleAuctionBiddedList(msgBuffer);
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

            // TM_CS_HUNTAHOLIC_INSTANCE_LIST (4000): the lobby asks for a page of its room list. The frame is
            // read, bounded and logged, and nothing is answered — the rooms, the pagination and the
            // huntaholic_id the 4001 answer would need have no source in the repository. It must stay before the
            // throwing switch below: a member of GamePackets that reaches it breaks the receive loop.
            // See docs/packet-specs/4000-huntaholic-instance-list.md.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST)
            {
                HandleHuntaholicInstanceList(msgBuffer);
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
                _ = HandleInstanceGameScoreRequestAsync(msgBuffer);
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

            // TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003): the room creation frame of the HuntaHolic lobby. Read,
            // validated and logged, with no answer — the only client reaction identified (the daily quota
            // refusal) has no established wire form, and a created room shows up through 4001/4002 only.
            // See docs/packet-specs/4003-huntaholic-create-instance.md §5.3.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE)
            {
                HandleHuntaholicCreateInstance(msgBuffer);
                continue;
            }

            // TM_CS_OPEN_ITEM_SHOP (10000) is a request with an empty body: 7 bytes on the wire, header
            // only. The 7.3 client never emits it — opening the item shop is a purely local command that
            // builds the web shop URL from shop_url and the character it already knows — so, like
            // TM_SC_REGION_ACK above, an incoming frame is an anomaly: logged and dropped, nothing read past
            // the header and nothing answered. The four fields of TM_SC_OPEN_ITEM_SHOP (10001) are web shop
            // credentials Navislamia does not hold, so no answer is invented for it.
            if (header.ID == (ushort)GamePackets.TM_CS_OPEN_ITEM_SHOP)
            {
                _logger.Warning("TM_CS_OPEN_ITEM_SHOP ({id}) received from {clientTag}: no item shop backend is configured, ignoring",
                    header.ID, ClientTag);
                continue;
            }

            // TM_SC_SKILLCARD_INFO is the answer of the skill card requests (286), a server to client
            // packet: the 7.3 client never sends one. An incoming one is a protocol anomaly, so it is
            // logged and dropped instead of reaching the "Unknown Packet Type" throw below.
            if (header.ID == (ushort)GamePackets.TM_SC_SKILLCARD_INFO)
            {
                _logger.Warning("Server to client packet TM_SC_SKILLCARD_INFO ({id}) received from {clientTag}",
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
                if (_networkService.QuestService is not null) _ = _networkService.QuestService.RefreshAsync(this);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_LEARN_SKILL)
            {
                _ = HandleLearnSkillAsync(msgBuffer);
                continue;
            }

            if (header.ID is (ushort)GamePackets.TM_SC_STATE_RESULT or (ushort)GamePackets.TM_SC_ENERGY or (ushort)GamePackets.TM_SC_CANT_ATTACK)
            {
                _logger.Warning("Server-only state/energy/attack packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_SKILL)
            {
                HandleSkill(msgBuffer);
                continue;
            }

            // TM_CS_AUCTION_BID (1306): the bid of the auction house, the only act of the family whose
            // answer is not a TS_SC_AUCTION_* frame — the client reads a TM_SC_RESULT (0) carrying
            // request_msg_id = 1306. The nineteen-byte frame is read, bounded and logged, and nothing is
            // executed: the announcement it targets, its rules and its writer belong to lots that do not
            // exist here. It must stay before the throwing switch below, like every member of GamePackets
            // the client can send: reaching it breaks the receive loop.
            // Placed here, in the widest stretch of the chain no other open branch inserts into, to keep
            // the four lots of the family apart (1300 goes above the switch, 1302 after the auction
            // response guard, 1304 before the TM_SC_MIX_RESULT block). See
            // docs/packet-specs/1306-auction-bid.md §13.7.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_BID)
            {
                HandleAuctionBid(msgBuffer);
                continue;
            }

            // TM_CS_SUMMON_CARD_SKILL_LIST (452): the client asks for the skill list of the summon tied to
            // a creature card. The frame is read, bounded and logged, and nothing is answered — no
            // reference implements 452 and the card -> summon resolution its answer would need is not
            // established. It must stay before the throwing switch below: a member of GamePackets that
            // reaches it breaks the receive loop. See docs/packet-specs/452-summon-card-skill-list.md.
            if (header.ID == (ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST)
            {
                HandleSummonCardSkillList(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_REMOVE_STATE)
            {
                HandleRemoveState(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTON_ITEM)
            {
                _ = HandlePutonItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTON_CARD)
            {
                _ = HandlePutonCardAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTOFF_ITEM)
            {
                _ = HandlePutoffItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_HIDE_EQUIP_INFO)
            {
                _ = HandleHideEquipInfoAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_PUTON_ITEM_SET)
            {
                _ = HandlePutonItemSetAsync(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_PUTOFF_CARD)
            {
                HandlePutoffCard(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_DONATE_REWARD)
            {
                HandleDonateReward(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO)
            {
                HandleGetSummonSetupInfo(msgBuffer);
                continue;
            }

            // TM_EQUIP_SUMMON (303) travels both ways: the server's formation, and the client's validated
            // one. The incoming direction must be claimed here, or the declared id reaches the throw below.
            if (header.ID == (ushort)GamePackets.TM_EQUIP_SUMMON)
            {
                HandleEquipSummon(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_STORAGE)
            {
                _ = HandleStorageAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_USE_ITEM)
            {
                _ = HandleUseItemAsync(msgBuffer);
                continue;
            }

            // TS_TRADE (280): the player trade, every step in one 97-byte frame (docs/packet-specs/280-trade.md).
            if (header.ID == (ushort)GamePackets.TM_TRADE)
            {
                _ = HandleTradeAsync(msgBuffer);
                continue;
            }

            // TM_CS_AUCTION_INSTANT_PURCHASE (1308): the buy-now request of the auction house, the act
            // whose answer is not a TS_SC_AUCTION_* frame — no such frame exists for it in any reference,
            // and the client reads a generic TM_SC_RESULT (0) carrying request_msg_id = 1308. The
            // eleven-byte frame is read, bounded and logged, and nothing is executed: the announcement it
            // names, its writer and the rules of the exchange belong to lots that do not exist here. It
            // must stay before the throwing switch below, like every member of GamePackets the client can
            // send: reaching it breaks the receive loop.
            // Placed here, in a stretch of the chain no other branch of the family inserts into, to keep
            // the five lots apart (1300 goes above the switch, 1302 right after the family's response
            // guard, 1304 before the TM_SC_MIX_RESULT block, 1306 after the summon-card-skill arm). See
            // docs/packet-specs/1308-auction-instant-purchase.md §14.6.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE)
            {
                HandleAuctionInstantPurchase(msgBuffer);
                continue;
            }

            // TM_CS_BUY_ITEM (251): one frame per catalogue line the player validated, answered by the
            // open market's transaction side. It must stay before the throwing switch below: a member of
            // GamePackets that reaches it breaks the receive loop. See docs/packet-specs/251-buy-item.md.
            if (header.ID == (ushort)GamePackets.TM_CS_BUY_ITEM)
            {
                _ = HandleBuyItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_DROP_QUEST)
            {
                _ = HandleDropQuestAsync(msgBuffer);
                continue;
            }

            // TM_CS_QUEST_INFO (604): the quest the player selected in the list window, sent by
            // quest_info_button. The frame is read bounded and logged, and nothing is answered: the 7.3
            // client has no handler for the only candidate answer (602), NGemity declares 604 without
            // ever reading it, and 604 changes no state. The code is not validated against the
            // character's quests — the verdict would be read by nobody. See
            // docs/packet-specs/604-quest-info.md §5.3, §5.5.
            if (header.ID == (ushort)GamePackets.TM_CS_QUEST_INFO)
            {
                HandleQuestInfo(msgBuffer);
                continue;
            }

            // TM_CS_END_QUEST (605): the quest window's confirmation. Read, judged against the character's
            // quests and answered with a TS_SC_RESULT tagged 605; the database read is awaited off the
            // receive loop. See docs/packet-specs/605-end-quest.md.
            if (header.ID == (ushort)GamePackets.TM_CS_END_QUEST)
            {
                _ = HandleEndQuestAsync(msgBuffer);
                continue;
            }

            // TM_CS_SOULSTONE_CRAFT (260) has its own engine: it fills the chassis of the named item and
            // writes what the client asks for. See docs/packet-specs/260-soulstone-craft.md §5.
            if (header.ID == (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT)
            {
                _ = _networkService.SoulstoneCraftService.HandleAsync(this, msgBuffer);
                continue;
            }

            // The rest of the crafting and item-enchantment family (256, 262, 263, 264) goes through the
            // structural socle, which reads and bounds the frame, resolves the handles it names and
            // refuses: those crafting engines and their game policy are not written yet. One arm covers the
            // four ids so that no member of GamePackets reaches the "Unknown Packet Type" throw below.
            // See docs/packet-specs/socle-artisanat-objets.md §9.2.
            if (header.ID is (ushort)GamePackets.TM_CS_MIX or
                (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE or
                (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY or
                (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT)
            {
                _ = _networkService.CraftingSocleService.HandleAsync(this, header.ID, msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_SELL_ITEM)
            {
                _ = HandleSellItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_DONATE_ITEM)
            {
                _ = HandleDonateItemAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_BIND_SKILLCARD)
            {
                _ = HandleBindSkillCardAsync(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_UNBIND_SKILLCARD)
            {
                _ = HandleUnbindSkillCardAsync(msgBuffer);
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

            // TM_CS_AUCTION_REGISTER (1309): the registration request of the auction house, the act whose
            // answer is not a TS_SC_AUCTION_* frame — no such frame exists for it in any reference, and the
            // client reads a generic TM_SC_RESULT (0) carrying request_msg_id = 1309. The thirty-two byte
            // frame is read, bounded and logged, and nothing is executed: the announcement writer, the
            // durations, the tax and the refusal codes belong to lots that do not exist here. It must stay
            // before the throwing switch below, like every member of GamePackets the client can send:
            // reaching it breaks the receive loop.
            // Placed here, in a stretch of the chain no other branch of the family inserts into, to keep
            // the six lots apart (1300 goes above the switch, 1302 right after the family's response guard,
            // 1304 before the TM_SC_MIX_RESULT block, 1306 after the summon-card-skill arm, 1308 before the
            // TM_CS_DROP_QUEST arm). See docs/packet-specs/1309-auction-register.md §14.6.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_REGISTER)
            {
                HandleAuctionRegister(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_RANKING_TOP_RECORD)
            {
                HandleRankingTopRecord(msgBuffer);
                continue;
            }

            // TM_SC_RANKING_TOP_RECORD is a server to client packet: the 7.3 client only routes it as an
            // incoming packet and never sends it. An incoming one is a protocol anomaly, not a request, so
            // it is logged and dropped instead of reaching the "Unknown Packet Type" throw below.
            if (header.ID == (ushort)GamePackets.TM_SC_RANKING_TOP_RECORD)
            {
                _logger.Warning("Server to client packet TM_SC_RANKING_TOP_RECORD ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            // The three server to client frames of the familier (pet) socle: TM_SC_UNSUMMON_PET (350),
            // TM_SC_ADD_PET_INFO (351) and TM_SC_REMOVE_PET_INFO (352). The 7.3 client only routes them as
            // incoming frames — it builds none of them (docs/packet-specs/socle-familier-pet.md §6.2) — so an
            // incoming one is a protocol anomaly, not a request. Logged and dropped like TM_SC_REGION_ACK
            // above, so that no member of GamePackets reaches the throwing switch below.
            if (header.ID is (ushort)GamePackets.TM_SC_UNSUMMON_PET
                or (ushort)GamePackets.TM_SC_ADD_PET_INFO
                or (ushort)GamePackets.TM_SC_REMOVE_PET_INFO
                or (ushort)GamePackets.TM_SC_SHOW_SET_PET_NAME
                or (ushort)GamePackets.TM_SC_CHANGE_NAME)
            {
                _logger.Warning("Server to client packet {id} received from {clientTag}", header.ID, ClientTag);
                continue;
            }

            // TM_SC_TAMING_INFO (310): the taming attempt of a monster — mode, tamer_handle,
            // target_handle — and a server to client frame the 7.3 client only reads
            // (docs/packet-specs/socle-apprivoisement-invocation.md §3.1). Logged and dropped like the pet
            // frames above, so that no member of GamePackets reaches the throwing switch below.
            if (header.ID == (ushort)GamePackets.TM_SC_TAMING_INFO)
            {
                _logger.Warning("Server to client packet TM_SC_TAMING_INFO ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            // TM_CS_COMPETE_REQUEST (4500) and TM_CS_COMPETE_ANSWER (4502) are the two client to server frames of
            // the competition socle (lot C1): read, validated, then refused by a TS_SC_RESULT carrying a code the
            // 7.3 client displays. The five remaining ids of the family travel server to client and are not
            // emitted yet, so they are not declared in GamePackets either.
            if (header.ID == (ushort)GamePackets.TM_CS_COMPETE_REQUEST)
            {
                HandleCompeteRequest(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_COMPETE_ANSWER)
            {
                HandleCompeteAnswer(msgBuffer);
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

            if (header.ID == (ushort)GamePackets.TM_CS_START_BOOTH)
            {
                HandleStartBooth(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_STOP_BOOTH)
            {
                HandleStopBooth(msgBuffer);
                continue;
            }

            // The visibility socle of the booth: TM_CS_WATCH_BOOTH (702) is read, resolved against the
            // owner's inventory and answered with a TM_SC_WATCH_BOOTH (703); TM_CS_STOP_WATCH_BOOTH (704)
            // just forgets the observation. The 702 handler awaits the database, so it is fired and not
            // awaited here: the receive loop must not wait on it.
            // See docs/packet-specs/socle-booths-visibilite.md §5.2.
            if (header.ID == (ushort)GamePackets.TM_CS_WATCH_BOOTH)
            {
                _ = _networkService.BoothWatchService.HandleWatchAsync(this, msgBuffer,
                    _networkService.AuthorizedGameClients.Values);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH)
            {
                _networkService.BoothWatchService.HandleStopWatch(this, msgBuffer);
                continue;
            }

            // The booth trade (docs/packet-specs/705-buy-from-booth.md): 705 buys from a sell booth and
            // 706 sells to a buy booth, both awaiting the database, so fired and not awaited; 707 asks the
            // names of booths and is answered at once with 708.
            if (header.ID == (ushort)GamePackets.TM_CS_BUY_FROM_BOOTH)
            {
                _ = _networkService.BoothTradeService.HandleBuyAsync(this, msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_SELL_TO_BOOTH)
            {
                _ = _networkService.BoothTradeService.HandleSellAsync(this, msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_GET_BOOTHS_NAME)
            {
                _networkService.BoothTradeService.HandleGetBoothsName(this, msgBuffer);
                continue;
            }

            // 708, 709 and 710 only travel server to client: a client that sends one is logged and
            // ignored, so the declared ids never reach the throwing switch below.
            if (header.ID is (ushort)GamePackets.TM_SC_GET_BOOTHS_NAME or (ushort)GamePackets.TM_SC_BOOTH_CLOSED
                or (ushort)GamePackets.TM_SC_BOOTH_TRADE_INFO)
            {
                _logger.Warning("Server to client booth packet {id} received from {clientTag}, dropped", header.ID,
                    ClientTag);
                continue;
            }

            // TM_SC_WATCH_BOOTH (703) is a server to client packet: it is the only way the client can see
            // a booth, so it builds none of it. An incoming one is a protocol anomaly, not a request:
            // logged and dropped like TM_SC_REGION_ACK above, so that a declared member of GamePackets
            // never reaches the "Unknown Packet Type" throw below.
            if (header.ID == (ushort)GamePackets.TM_SC_WATCH_BOOTH)
            {
                _logger.Warning("Server to client packet TM_SC_WATCH_BOOTH ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            // TM_CS_CHECK_BOOTH_STARTABLE (711): the 7.3 client does build and send this 7 byte frame
            // (SFrame.exe builder VA 0x48CFD0, one call site 0x49A176) even though its incoming
            // dispatcher stops at 710, so the id must be both declared and routed here. The frame is
            // read and logged and nothing is answered — no reference holds an answer and the client
            // has no slot for one. It must stay before the throwing switch below: a member of
            // GamePackets that reaches it breaks the receive loop.
            // See docs/packet-specs/711-check-booth-startable.md §2.2 and §5.2.
            if (header.ID == (ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE)
            {
                HandleCheckBoothStartable(msgBuffer);
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

            // Header-only client packet with no known payload and no response in either reference
            // implementation. It must still be consumed here, because an id defined in GamePackets
            // that reaches the final switch below throws "Unknown Packet Type" inside the receive
            // loop. See docs/packet-specs/223-swap-equip.md.
            if (header.ID == (ushort)GamePackets.TM_CS_SWAP_EQUIP)
            {
                _logger.Debug("TM_CS_SWAP_EQUIP ({id}) Length: {length} received from {clientTag}",
                    header.ID, header.Length, ClientTag);
                // onSwapEquip: the main and spare weapon sets change places (docs/packet-specs/223-swap-equip.md).
                _ = _networkService.EquipmentService?.SwapAsync(this);
                continue;
            }

            // TM_CS_SET_PET_NAME (354), the pet (familier) rename request: the 7.3 client sends it from its name
            // box (SFrame.exe 0x48e170) with the handle the server itself put in TM_SC_SHOW_SET_PET_NAME (353)
            // and the typed name. PetSummonService renames the pet out when that handle is the one a 353
            // offered; nothing is answered — no reference holds a result frame for a pet rename, and a
            // refused name simply reopens the box. See docs/packet-specs/354-set-pet-name.md and
            // socle-familier-pet.md §17.
            if (header.ID == (ushort)GamePackets.TM_CS_SET_PET_NAME)
            {
                if (GameSummonPackets.TryReadSetPetName(msgBuffer, out var petHandle, out var petName))
                {
                    if (_logger.IsEnabled(LogEventLevel.Debug))
                    {
                        _logger.Debug(
                            "TM_CS_SET_PET_NAME ({id}) received from {clientTag}: handle={handle} name=\"{name}\"",
                            header.ID, ClientTag, petHandle, petName);
                    }

                    _ = _networkService.PetSummonService.RenameAsync(this, petHandle, petName);
                }
                else
                {
                    _logger.Warning(
                        "Malformed TM_CS_SET_PET_NAME ({id}) Length: {length} received from {clientTag}",
                        header.ID, header.Length, ClientTag);
                }

                continue;
            }

            // TM_CS_SET_PET_FILTER (355), the pet pickup filter of the client's options (PET_PICKUP_FILTER).
            // The value is kept and logged, never applied: its meaning is not established (socle-familier-pet.md
            // §11.4, §17).
            if (header.ID == (ushort)GamePackets.TM_CS_SET_PET_FILTER)
            {
                if (GamePetPackets.TryReadSetPetFilter(msgBuffer, out var filterHandle, out var filter))
                {
                    _networkService.PetSummonService.SetPickupFilter(this, filterHandle, filter);
                }
                else
                {
                    _logger.Warning("Malformed TM_CS_SET_PET_FILTER ({id}) Length: {length} received from {clientTag}",
                        header.ID, header.Length, ClientTag);
                }

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

            // TM_CS_AUCTION_CANCEL (1310): the withdrawal request of the auction house, read and logged
            // only. It must stay before the throwing switch below, like every member of GamePackets the
            // client can send: reaching it breaks the receive loop.
            // Placed here, between the anti-hack arm and the illegal-user arm, in a stretch no other open
            // branch of the family inserts into — each of the six siblings of 1310 chose another arm — so
            // the seven lots merge without touching one another. See
            // docs/packet-specs/1310-auction-cancel.md §14.6.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_CANCEL)
            {
                HandleAuctionCancel(msgBuffer);
                continue;
            }

            // TM_CS_CHECK_ILLEGAL_USER (57) is declared so that the frame is read instead of being dropped as
            // an undefined id. It must stay before the throwing switch below: a member of GamePackets that
            // reaches it breaks the receive loop.
            if (header.ID == (ushort)GamePackets.TM_CS_CHECK_ILLEGAL_USER)
            {
                HandleCheckIllegalUser(msgBuffer);
                continue;
            }

            // TM_CS_XTRAP_CHECK (59) is declared so that the frame is read and bounded instead of being
            // dropped as an undefined id. It must stay before the throwing switch below: a member of
            // GamePackets that reaches it breaks the receive loop. The arm answers nothing.
            if (header.ID == (ushort)GamePackets.TM_CS_XTRAP_CHECK)
            {
                HandleXtrapCheck(msgBuffer);
                continue;
            }

            // The creature farm (ferme de créatures) socle: TM_CS_REQUEST_FARM_INFO (6000),
            // TM_CS_FOSTER_CREATURE (6002), TM_CS_RETRIEVE_CREATURE (6004), TM_CS_NURSE_CREATURE (6006) and
            // TM_CS_REQUEST_FARM_MARKET (6008). The five frames are read and bounded and only 6000 is answered
            // (with an empty TM_SC_FARM_INFO, 6001): NavisLamia stores no farm, and the result frames
            // 6003/6005/6007 carry a `result` byte whose values no reference establishes. The arms must stay
            // before the throwing switch below — a member of GamePackets that reaches it breaks the receive
            // loop. See docs/packet-specs/socle-ferme-creatures.md.
            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_FARM_INFO)
            {
                HandleRequestFarmInfo(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_FOSTER_CREATURE)
            {
                HandleFosterCreature(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_RETRIEVE_CREATURE)
            {
                HandleRetrieveCreature(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_NURSE_CREATURE)
            {
                HandleNurseCreature(msgBuffer);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET)
            {
                HandleRequestFarmMarket(msgBuffer);
                continue;
            }

            // TM_SC_FARM_INFO (6001) is a server to client packet: the 7.3 client routes it on entry and builds
            // none. An incoming one is a protocol anomaly, not a request, so it is logged and dropped instead of
            // reaching the "Unknown Packet Type" throw below. See
            // docs/packet-specs/socle-ferme-creatures.md §5.3.
            if (header.ID == (ushort)GamePackets.TM_SC_FARM_INFO)
            {
                _logger.Warning("Server to client packet TM_SC_FARM_INFO ({id}) received from {clientTag}",
                    header.ID, ClientTag);
                continue;
            }

            // TM_CS_AUCTION_SEARCH (1300) is the auction house search request, 51 bytes. It must stay
            // before the throwing switch below: an id declared in GamePackets with no arm reaches
            // "Unknown Packet Type" and kills the receive loop. See
            // docs/packet-specs/1300-auction-search.md §5.2.
            if (header.ID == (ushort)GamePackets.TM_CS_AUCTION_SEARCH)
            {
                HandleAuctionSearch(msgBuffer);
                continue;
            }

            // TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the leave gesture of the HuntaHolic instance window. The
            // frame is a bare 7-byte header, so it is read, bounded and logged, and nothing is answered — the
            // client has no receive arm for this id and the server holds no lobby state to leave. It sits right
            // before the throwing switch rather than next to the other HuntaHolic arms so that its insertion
            // point stays out of the zone the sibling branches of the family already claim. It must stay before
            // the switch below: a member of GamePackets that reaches it breaks the receive loop.
            // See docs/packet-specs/4005-huntaholic-leave-instance.md.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE)
            {
                HandleHuntaholicLeaveInstance(msgBuffer);
                continue;
            }

            // The auction keeping box (1350/1352); 1351 is the server's answer and never reaches here.
            if (header.ID is (ushort)GamePackets.TM_CS_ITEM_KEEPING_LIST or (ushort)GamePackets.TM_CS_ITEM_KEEPING_TAKE)
            {
                HandleItemKeeping(msgBuffer, header.ID == (ushort)GamePackets.TM_CS_ITEM_KEEPING_TAKE);
                continue;
            }

            if (header.ID == (ushort)GamePackets.TM_SC_ITEM_KEEPING_LIST)
            {
                _logger.Warning("TM_SC_ITEM_KEEPING_LIST received from {clientTag}: a server packet, dropped", ClientTag);
                continue;
            }

            // TM_CS_HUNTAHOLIC_LEAVE_LOBBY (4008): declared by rzu, built by no constructor of the 7.3 client.
            if (header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_LOBBY)
            {
                HandleHuntaholicLeaveLobby(msgBuffer);
                continue;
            }

            // TM_CS_TURN_ON_PK_MODE (800): a header-only client frame (7 bytes, empty body) with no server
            // answer — the PK state travels on bit 11 of the status mask of TM_SC_STATUS_CHANGE (500) only,
            // exactly like the /pk command. It must stay before the throwing switch below: a member of
            // GamePackets that reaches it breaks the receive loop. Its twin TM_CS_TURN_OFF_PK_MODE (801) is
            // declared by its own branch; an undeclared id is logged and dropped, so nothing breaks meanwhile.
            // See docs/packet-specs/800-turn-on-pk-mode.md.
            if (header.ID == (ushort)GamePackets.TM_CS_TURN_ON_PK_MODE)
            {
                HandleTurnOnPkMode(msgBuffer);
                continue;
            }

            // TM_CS_TURN_OFF_PK_MODE (801): a header-only client frame (7 bytes, empty body) with no server
            // answer — the PK state travels on bit 11 of the status mask of TM_SC_STATUS_CHANGE (500) only,
            // exactly like the /pk command and its twin 800. It must stay before the throwing switch below: a
            // member of GamePackets that reaches it breaks the receive loop. 801 is the twin that belongs to
            // this branch; 800 is declared by its own branch, and an undeclared id is logged and dropped, so
            // nothing breaks meanwhile.
            // See docs/packet-specs/801-turn-off-pk-mode.md.
            if (header.ID == (ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE)
            {
                HandleTurnOffPkMode(msgBuffer);
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

                _ => throw new Exception($"Unknown Packet Type {header.ID}")
            };

            if (_logger.IsEnabled(LogEventLevel.Debug))
            {
                _logger.Debug("{name} ({id}) Length: {length} received from {clientTag}", msg.StructName, msg.Id,
                    msg.Length, ClientTag);
            }

            Actions.Execute(this, msg);
        }
    }
}
