using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Maps;
using Navislamia.Game.Maps.Entities;
using Navislamia.Game.Maps.Enums;
using Navislamia.Game.Maps.X2D;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// <c>TM_CS_ENTER_EVENT_AREA</c> (15) and <c>TM_CS_LEAVE_EVENT_AREA</c> (16). The packet is a hint
/// and the polygon is the authority, so the offsets and the decision table are pinned separately.
/// </summary>
[TestFixture]
public class EventAreaPacketTests
{
    private const int EnterPacketId = 15;
    private const int LeavePacketId = 16;

    [Test]
    public void EventAreaPackets_UseTheIdsTheClientSendsIn73()
    {
        ((ushort)GamePackets.TM_CS_ENTER_EVENT_AREA).Should().Be(EnterPacketId);
        ((ushort)GamePackets.TM_CS_LEAVE_EVENT_AREA).Should().Be(LeavePacketId);
    }

    [Test]
    public void GamePacketIds_StayUniqueWithTheEventAreaMembers()
    {
        Enum.GetValues<GamePackets>().Select(packet => (ushort)packet).Should().OnlyHaveUniqueItems();
    }

    [TestCase(EnterPacketId)]
    [TestCase(LeavePacketId)]
    public void EventAreaRequest_ReadsTheAreaIdAtOffsetSevenAndTheAreaIndexAtOffsetEleven(int packetId)
    {
        // Hand written 15 byte frame, no helper: Length (0..3), ID (4..5), Checksum (6),
        // event_area_id (7..10), area_index (11..14).
        var packet = new byte[]
        {
            15, 0, 0, 0,
            (byte)packetId, 0,
            0x00,
            0x04, 0x03, 0x02, 0x01,
            0x08, 0x07, 0x06, 0x05
        };

        GameEventAreaPackets.PacketLength.Should().Be(15);
        GameEventAreaPackets.TryReadEventAreaRequest(packet, out var request).Should().BeTrue();
        request.EventAreaId.Should().Be(0x01020304, "event_area_id is a little endian int32 at offset 7");
        request.AreaIndex.Should().Be(0x05060708, "area_index is a little endian int32 at offset 11");
    }

    [TestCase(7)]
    [TestCase(14)]
    [TestCase(16)]
    public void EventAreaRequest_RejectsAnyLengthOtherThanFifteen(int length)
    {
        var packet = new byte[length];

        GameEventAreaPackets.TryReadEventAreaRequest(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameEventAreaPackets.EventAreaRequest));
    }

    [TestCase(GamePackets.TM_CS_ENTER_EVENT_AREA)]
    [TestCase(GamePackets.TM_CS_LEAVE_EVENT_AREA)]
    public void EventAreaPackets_AreDispatchedBeforeTheUnknownPacketThrow(GamePackets packet)
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch
        // reaches the final switch and its `throw` kills the receive loop. Nothing smaller than a
        // source scan can check that without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf($"GamePackets.{packet}", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, $"{packet} needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, $"{packet} must be handled before the final switch throws");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory.FullName;
    }
}

[TestFixture]
public class EventAreaRulesTests
{
    [TestCase(true, true, true, EventAreaTransition.None)]
    [TestCase(true, true, false, EventAreaTransition.Entered)]
    [TestCase(true, false, true, EventAreaTransition.Ignored)]
    [TestCase(true, false, false, EventAreaTransition.Ignored)]
    [TestCase(false, false, true, EventAreaTransition.Left)]
    [TestCase(false, false, false, EventAreaTransition.None)]
    [TestCase(false, true, true, EventAreaTransition.Ignored)]
    [TestCase(false, true, false, EventAreaTransition.Ignored)]
    public void Resolve_OnlyMovesTheSessionWhenTheClaimIsVerified(bool isEnter, bool inside, bool isCurrentArea,
        EventAreaTransition expected)
    {
        EventAreaRules.Resolve(isEnter, inside, isCurrentArea).Should().Be(expected);
    }
}

[TestFixture]
public class EventAreaServiceTests
{
    private const int AreaId = 7;
    private const int OtherAreaId = 9;
    private const string ClientTag = "Game Client @test";

    private static readonly PointF[] Square =
    {
        new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f)
    };

    private static readonly PointF[] FarSquare =
    {
        new(1000f, 1000f), new(1100f, 1000f), new(1100f, 1100f), new(1000f, 1100f)
    };

    private EventAreaService _service;

    [SetUp]
    public void SetUp() =>
        _service = new EventAreaService(new FakeMapService(new EventAreaInfo(AreaId, Square)));

    [Test]
    public void EnterPacket_InsideThePolygon_MarksTheAreaCurrent()
    {
        var session = Session(50f, 50f);

        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(AreaId);
    }

    [Test]
    public void EnterPacket_Twice_ChangesNothingTheSecondTime()
    {
        var session = Session(50f, 50f);

        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true).Should().BeTrue();
        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true).Should().BeFalse(
            "the session is already in that area, entering again is not a transition");
        session.CurrentEventAreaId.Should().Be(AreaId);
    }

    [Test]
    public void EnterPacket_FromOutsideThePolygon_IsIgnored()
    {
        var session = Session(500f, 500f);

        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true).Should().BeFalse(
            "the client claim contradicts the position the server holds");
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void EnterPacket_ForAnAreaTheMapDoesNotHave_IsIgnored()
    {
        var session = Session(50f, 50f);

        _service.HandlePacket(session, ClientTag, Packet(15, 4242, 0), isEnter: true).Should().BeFalse();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void EnterPacket_WithAFourteenByteFrame_IsIgnored()
    {
        var session = Session(50f, 50f);
        var packet = new byte[14];

        _service.HandlePacket(session, ClientTag, packet, isEnter: true).Should().BeFalse();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void LeavePacket_TheCurrentAreaFromOutside_ClearsIt()
    {
        var session = Session(50f, 50f);
        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true);

        session.X = 500f;
        session.Y = 500f;

        _service.HandlePacket(session, ClientTag, Packet(16, AreaId, 0), isEnter: false).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void LeavePacket_WhileStillInsideThePolygon_IsIgnored()
    {
        var session = Session(50f, 50f);
        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true);

        _service.HandlePacket(session, ClientTag, Packet(16, AreaId, 0), isEnter: false).Should().BeFalse(
            "the server position is still inside the area");
        session.CurrentEventAreaId.Should().Be(AreaId);
    }

    [Test]
    public void LeavePacket_WithoutACurrentArea_ChangesNothing()
    {
        var session = Session(500f, 500f);

        _service.HandlePacket(session, ClientTag, Packet(16, AreaId, 0), isEnter: false).Should().BeFalse();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void Refresh_EntersTheAreaWithNoClientPacket()
    {
        var session = Session(50f, 50f);

        _service.Refresh(session, ClientTag).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(AreaId);

        _service.Refresh(session, ClientTag).Should().BeFalse("the area is already current");
    }

    [Test]
    public void Refresh_LeavesTheAreaWhenThePositionMovesOut()
    {
        var session = Session(50f, 50f);
        _service.Refresh(session, ClientTag);

        session.X = 500f;
        session.Y = 500f;

        _service.Refresh(session, ClientTag).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void Refresh_OutsideEveryArea_ChangesNothingWhenNoAreaIsCurrent()
    {
        var session = Session(500f, 500f);

        _service.Refresh(session, ClientTag).Should().BeFalse();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void Refresh_SwitchesToTheAreaContainingTheNewPosition()
    {
        var service = new EventAreaService(new FakeMapService(
            new EventAreaInfo(AreaId, Square), new EventAreaInfo(OtherAreaId, FarSquare)));
        var session = Session(50f, 50f);

        service.Refresh(session, ClientTag).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(AreaId);

        session.X = 1050f;
        session.Y = 1050f;

        service.Refresh(session, ClientTag).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(OtherAreaId);
    }

    [Test]
    public void Refresh_FarFromTheLoadedArea_ReportsALeave()
    {
        // The format carries no map, so a session that jumps to another map is treated as leaving the
        // area: nothing in the packet or in EventAreaInfo tells the two maps apart (NON ETABLI 6).
        var session = Session(50f, 50f);
        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true);

        session.X = 1050f;
        session.Y = 1050f;

        _service.Refresh(session, ClientTag).Should().BeTrue();
        session.CurrentEventAreaId.Should().Be(0);
    }

    [Test]
    public void ClearCharacterSession_ResetsTheCurrentEventArea()
    {
        var session = Session(50f, 50f);
        _service.HandlePacket(session, ClientTag, Packet(15, AreaId, 0), isEnter: true);

        session.ClearCharacterSession();

        session.CurrentEventAreaId.Should().Be(0);
    }

    private static ConnectionInfo Session(float x, float y) => new() { X = x, Y = y };

    private static byte[] Packet(int packetId, int eventAreaId, int areaIndex)
    {
        var packet = new byte[GameEventAreaPackets.PacketLength];

        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), GameEventAreaPackets.PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)packetId);

        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;

        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), eventAreaId);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), areaIndex);

        return packet;
    }

    private sealed class FakeMapService : IMapService
    {
        private readonly Dictionary<int, EventAreaInfo> _areas = new();

        public FakeMapService(params EventAreaInfo[] areas)
        {
            foreach (var area in areas)
            {
                _areas[area.Id] = area;
            }
        }

        public void Start(string directory) { }

        public bool TryGetEventArea(int eventAreaId, out EventAreaInfo eventArea) =>
            _areas.TryGetValue(eventAreaId, out eventArea);

        public EventAreaInfo[] GetEventAreas() => _areas.Values.ToArray();
    }
}

[TestFixture]
public class EventAreaContainmentTests
{
    private static readonly PointF[] Square =
    {
        new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f)
    };

    [Test]
    public void IsIncluded_AnswersWhetherAPointIsInsideThePolygon()
    {
        var area = new EventAreaInfo(1, Square);

        area.Area.IsIncluded(50f, 50f).Should().BeTrue();
        area.Area.IsIncluded(10f, 90f).Should().BeTrue();
        area.Area.IsIncluded(500f, 50f).Should().BeFalse();
        area.Area.IsIncluded(50f, 500f).Should().BeFalse();
        area.Area.IsIncluded(-50f, 50f).Should().BeFalse();
    }

    [Test]
    public void IsIncluded_OnAVertex_AnswersOutsideBecausePointFHasNoValueEquality()
    {
        // The engine compares a point in the polygon with `==`; the ported PointF is a class without
        // an operator, so that comparison is reference equality and the vertex shortcut never fires.
        // The ray casting still answers correctly everywhere but exactly on a vertex (reserve).
        var area = new EventAreaInfo(1, Square);

        area.Area.IsIncluded(0f, 0f).Should().BeFalse();
        area.Area.IsIncluded(new PointF(0f, 0f)).Should().BeFalse();
    }

    [Test]
    public void IsIncluded_OnAPolygonWithLessThanThreePoints_IsAlwaysFalse()
    {
        // PolygonF swallows an invalid polygon: it keeps no points and answers false, so a malformed
        // .nfe area can never be entered.
        var area = new EventAreaInfo(1, new[] { new PointF(0f, 0f), new PointF(100f, 100f) });

        area.Area.Size().Should().Be(0);
        area.Area.IsIncluded(50f, 50f).Should().BeFalse();
    }

    [Test]
    public void Contains_IsVertexEqualityAndMustNotBeUsedAsAContainmentTest()
    {
        // The packet spec cites PolygonF.Contains for the containment check; it is a comparison
        // against the vertex list, which is why EventAreaService goes through IsIncluded.
        var area = new EventAreaInfo(1, Square);

        area.Area.Contains(new PointF(0f, 0f)).Should().BeFalse("a fresh PointF is not one of the vertices");
        area.Area.Contains(Square[0]).Should().BeTrue("the very instance of a vertex is");
        area.Area.Contains(new PointF(50f, 50f)).Should().BeFalse("a point inside the polygon is not a vertex");
    }

    // The two tests below pin the port fixes in LineF.IntersectCcw that make the point in polygon
    // above possible; the C# carried a comparison against ccw123 and an inverted Y range precheck.
    [Test]
    public void IntersectCcw_ReportsCrossingSegmentsAsIntersecting()
    {
        LineF.IntersectCcw(new PointF(0f, 0f), new PointF(10f, 10f), new PointF(0f, 10f), new PointF(10f, 0f))
            .Should().Be(IntersectResult.INTERSECT);
    }

    [Test]
    public void IntersectCcw_ReportsSegmentsWithDisjointYRangesAsSeparate()
    {
        LineF.IntersectCcw(new PointF(0f, 0f), new PointF(10f, 0f), new PointF(0f, 50f), new PointF(10f, 50f))
            .Should().Be(IntersectResult.SEPERATE);
    }
}
