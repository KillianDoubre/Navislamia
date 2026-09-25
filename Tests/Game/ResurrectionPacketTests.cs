using System.Buffers.Binary;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// TM_CS_RESURRECTION (513) — the death/respawn foundation, see
/// docs/packet-specs/socle-mort-respawn.md. The frame is fixed at 12 bytes: the 7-byte header, the
/// 4-byte handle at offset 7 and the 1-byte type at offset 11. The dispatch is exercised against the
/// real receive loop on purpose: a member of <see cref="GamePackets"/> that no branch claims reaches
/// the final switch and throws "Unknown Packet Type" inside that loop.
/// </summary>
[TestFixture]
public class ResurrectionPacketTests
{
    private const uint Handle = 0x40000456u;
    private const int PacketLength = 12;

    private static byte[] ClientFrame(uint handle, ResurrectionType type, int length = PacketLength)
    {
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_RESURRECTION);

        if (length >= 11)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), handle);
        }

        if (length > 11)
        {
            packet[11] = (byte)type;
        }

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }

    [Test]
    public void EnumMember_SitsBetweenItsNeighbours()
    {
        ((ushort)GamePackets.TM_CS_TARGETING).Should().Be(511);
        ((ushort)GamePackets.TM_CS_RESURRECTION).Should().Be(513);
        ((ushort)GamePackets.TM_CS_MONSTER_RECOGNIZE).Should().Be(517);
    }

    [Test]
    public void ClientFrame_IsTheFixedTwelveByteEpic73Shape()
    {
        var packet = ClientFrame(Handle, ResurrectionType.UseNone);

        GameActionPackets.ResurrectionPacketLength.Should().Be(PacketLength);
        packet.Should().HaveCount(12, "the 7.3 frame is length, id, checksum, handle and type");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(12);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(513);
    }

    [Test]
    public void ClientFrame_HasTheHandleAtOffsetSevenAndTheTypeAtOffsetEleven()
    {
        var packet = ClientFrame(0x01020304u, ResurrectionType.UsePotion);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x01020304u);
        packet[11].Should().Be(2, "the type is the last byte of the frame, with no padding behind it");

        GameActionPackets.TryReadResurrection(packet, out var request).Should().BeTrue();
        request.Handle.Should().Be(0x01020304u);
        request.Type.Should().Be(ResurrectionType.UsePotion);
    }

    [Test]
    public void TryReadResurrection_ReadsEveryTypeTheReferenceDeclares()
    {
        ((byte)ResurrectionType.UseNone).Should().Be(0);
        ((byte)ResurrectionType.UseState).Should().Be(1);
        ((byte)ResurrectionType.UsePotion).Should().Be(2);
        ((byte)ResurrectionType.Compete).Should().Be(3);
        ((byte)ResurrectionType.Deathmatch).Should().Be(4);

        foreach (var type in Enum.GetValues<ResurrectionType>())
        {
            GameActionPackets.TryReadResurrection(ClientFrame(Handle, type), out var request)
                .Should().BeTrue();
            request.Type.Should().Be(type);
        }
    }

    [Test]
    public void TryReadResurrection_RefusesThePre61ThirteenByteShape()
    {
        var pre61 = ClientFrame(Handle, ResurrectionType.UseNone, PacketLength + 1);

        GameActionPackets.TryReadResurrection(pre61, out var request).Should().BeFalse(
            "the pre-6.1 shape adds use_state/use_potion behind the type, and its extra byte would misalign the stream");
        request.Should().Be(default(GameActionPackets.ResurrectionRequest));
    }

    [Test]
    public void TryReadResurrection_RefusesAShortFrame()
    {
        GameActionPackets.TryReadResurrection(ClientFrame(Handle, ResurrectionType.UseNone, 7), out _)
            .Should().BeFalse();
        GameActionPackets.TryReadResurrection(ClientFrame(Handle, ResurrectionType.UseNone, 11), out _)
            .Should().BeFalse();
    }

    [Test]
    public void CheckRequest_AcceptsTheTownPathForTheDeadOwnCharacter()
    {
        ResurrectionRules.CheckRequest(ResurrectionType.UseNone, Handle, 0)
            .Should().Be(ResultCode.Success);
    }

    [TestCase(0u)]
    [TestCase(Handle + 1)]
    [TestCase(0xFFFFFFFFu)]
    public void Resurrect_InTown_IgnoresTheFrameHandle(uint frameHandle)
    {
        // The 7.3 client's town button was refused with NotOwn in game (2026-09-23): the handle it sends
        // is not the character's. NGemity never reads it on this path, and neither does this server.
        var connection = new FrameConnection(ClientFrame(frameHandle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _, realWarp: false);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 0 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
        ConnectionInfoOf(client).CharacterHp.Should().Be(5000);
    }

    [Test]
    public void CheckRequest_RefusesALivingCharacter()
    {
        ResurrectionRules.CheckRequest(ResurrectionType.UseNone, Handle, 1)
            .Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void CheckRequest_AcceptsTheStatePathForTheDeadOwnCharacter()
    {
        ResurrectionRules.CheckRequest(ResurrectionType.UseState, Handle, 0).Should()
            .Be(ResultCode.Success);
    }

    [Test]
    public void CheckRequest_AcceptsTheItemPathForTheDeadOwnCharacter()
    {
        ResurrectionRules.CheckRequest(ResurrectionType.UsePotion, Handle, 0).Should()
            .Be(ResultCode.Success);
    }

    [Test]
    public void CheckRequest_RefusesTheTypesThatBelongToALaterLot()
    {
        foreach (var type in new[] { ResurrectionType.Compete, ResurrectionType.Deathmatch })
        {
            ResurrectionRules.CheckRequest(type, Handle, 0).Should().Be(ResultCode.NotActable);
        }
    }

    [Test]
    public void CheckRequest_RefusesASessionWithoutACharacter()
    {
        ResurrectionRules.CheckRequest(ResurrectionType.UseNone, 0, 0).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void RestoredVitals_AreTheRecomputedMaxima()
    {
        var (hp, mp) = ResurrectionRules.RestoredVitals(5000f, 800f);

        hp.Should().Be(5000);
        mp.Should().Be(800);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_RefusesASessionWithoutACharacter()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
    }

    [Test]
    public void OnDataReceived_AnswersInvalidArgumentOnAMalformedFrame()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone, PacketLength + 1));
        var client = NewGameClient(connection, out var services, realWarp: false);

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.InvalidArgument));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void Resurrect_WarpsToTheReturnPointRestoresTheVitalsAndAcknowledges()
    {
        const float respawnX = 94454f;
        const float respawnY = 126040f;

        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);
        Seed(client, new ConnectionInfo
        {
            CharacterHandle = Handle,
            CharacterHp = 0,
            CharacterMp = 0,
            CharacterName = "Resurrected",
            X = 1000f,
            Y = 2000f,
            Layer = 2,
            RespawnX = respawnX,
            RespawnY = respawnY,
            RespawnLayer = 5
        });

        client.OnDataReceived(connection.BytesAvailable);

        var info = ConnectionInfoOf(client);
        info.CharacterHp.Should().Be(5000, "the returned vitals are the recomputed maxima");
        info.CharacterMp.Should().Be(800);
        info.Layer.Should().Be(5, "the return point carries its own layer");

        // The warp frame is the real WarpService's, so the position on the wire is the return point.
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_WARP);
        BinaryPrimitives.ReadSingleLittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(respawnX);
        BinaryPrimitives.ReadSingleLittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(respawnY);
        connection.Sent[0][19].Should().Be(5);

        Properties(connection).Should().Equal(("hp", 5000L), ("mp", 800L));
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
        connection.Sent.Should().HaveCount(4, "warp, hp, mp and the acknowledgment");
    }

    [Test]
    public void ResurrectByState_ComesBackInPlaceConsumesTheStateAndAcknowledges()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseState));
        var client = NewGameClient(connection, out var services, realWarp: false);
        var info = new ConnectionInfo
        {
            CharacterHandle = Handle,
            CharacterHp = 0,
            CharacterMp = 100,
            X = 1000f,
            Y = 2000f,
            RespawnX = 94454f,
            RespawnY = 126040f
        };
        info.ActiveBuffs.Add(new ActiveBuff(7, ResurrectionStateId, 3472, 1, 0, 180_000));
        Seed(client, info);

        client.OnDataReceived(connection.BytesAvailable);

        info = ConnectionInfoOf(client);
        info.CharacterHp.Should().Be(250, "5 % of the 5000 maximum HP");
        info.CharacterMp.Should().Be(124, "the 100 kept plus 3 % of the 800 maximum MP");
        A.CallTo(() => services.SkillCast.RemoveState(client, ResurrectionStateId)).MustHaveHappenedOnceExactly();
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
        Properties(connection).Should().Equal(("hp", 250L), ("mp", 124L));
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
    }

    [Test]
    public void ResurrectByState_WithoutAResurrectionState_IsRefusedAndChangesNothing()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseState));
        var client = NewGameClient(connection, out var services, realWarp: false);
        var info = new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 0 };
        info.ActiveBuffs.Add(new ActiveBuff(3, 4001, 1011, 1, 0, 180_000));
        Seed(client, info);

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        ConnectionInfoOf(client).CharacterHp.Should().Be(0);
        A.CallTo(() => services.SkillCast.RemoveState(A<GameClient>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public void TrySelectState_KeepsTheHighestLevelResurrectionState()
    {
        var weak = new ResurrectionStateValues(0.05m, 0m, 0m, 0m);
        var strong = new ResurrectionStateValues(0m, 0.03m, 0m, 0.03m);
        var states = new[]
        {
            new ActiveBuff(1, 13472, 3472, 1, 0, 0),
            new ActiveBuff(2, 4001, 1011, 9, 0, 0),
            new ActiveBuff(3, 145226, 45424, 3, 0, 0)
        };

        bool Resolve(int stateId, out ResurrectionStateValues values)
        {
            values = stateId switch { 13472 => weak, 145226 => strong, _ => default };
            return stateId is 13472 or 145226;
        }

        ResurrectionRules.TrySelectState(states, Resolve, out var state, out var selected).Should().BeTrue();
        state.StateId.Should().Be(145226, "the reference keeps the highest level, not the first found");
        selected.Should().Be(strong);

        ResurrectionRules.TrySelectState(new[] { states[1] }, Resolve, out _, out _).Should().BeFalse();
        ResurrectionRules.TrySelectState(null, Resolve, out _, out _).Should().BeFalse();
    }

    [Test]
    public void VitalsByState_FollowsTheReferenceFormula()
    {
        var perLevel = new ResurrectionStateValues(0m, 0.03m, 0m, 0.03m);

        ResurrectionRules.VitalsByState(perLevel, 3, 5000f, 800f, 50).Should().Be((450, 122),
            "(0 + 0.03 x 3) x 5000 HP, and 50 + (0 + 0.03 x 3) x 800 MP");
    }

    [Test]
    public void VitalsByState_NeverLeavesTheCharacterDeadNorAboveItsMaxima()
    {
        var nothing = new ResurrectionStateValues(0m, 0m, 0m, 0m);
        var everything = new ResurrectionStateValues(2m, 0m, 2m, 0m);

        ResurrectionRules.VitalsByState(nothing, 1, 5000f, 800f, 0).Should().Be((1, 0));
        ResurrectionRules.VitalsByState(everything, 1, 5000f, 800f, 700).Should().Be((5000, 800));
    }

    [Test]
    public void Resurrect_DoesNothingOnALivingCharacter()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out var services, realWarp: false);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 4000 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
        A.CallTo(() => services.StatService.Compute(A<ConnectionInfo>._)).MustNotHaveHappened();
        ConnectionInfoOf(client).CharacterHp.Should().Be(4000);
    }

    [Test]
    public void Resurrect_RefusesTheLaterLotTypesWithoutMoving()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.Compete));
        var client = NewGameClient(connection, out var services, realWarp: false);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 0 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
        ConnectionInfoOf(client).CharacterHp.Should().Be(0, "a refused request changes nothing");
    }

    [Test]
    public void Resurrect_AnswersEveryCoalescedRequest()
    {
        var frame = ClientFrame(Handle, ResurrectionType.UseNone)
            .Concat(ClientFrame(Handle, ResurrectionType.UseNone))
            .ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection, out _);
        Seed(client, new ConnectionInfo
        {
            CharacterHandle = Handle,
            CharacterHp = 0,
            RespawnX = 94454f,
            RespawnY = 126040f
        });

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        Results(connection).Should().Equal(
            new[] { A574Result((ushort)ResultCode.Success), A574Result((ushort)ResultCode.NotActable) },
            "the first request raises the character, the second finds it alive");
    }

    private const int ResurrectionStateId = 13472;
    private const int ResurrectionScrollId = 603002;
    private const int ScrollHandle = 77;

    // --- RT_UsePotion: the Resurrection Scroll (docs/packet-specs/socle-effets-resurrection.md, lot R2) ---

    [Test]
    public void ResurrectByItem_ConsumesOneScrollAndComesBackInPlace()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UsePotion));
        var client = NewGameClient(connection, out var services, realWarp: false);
        services.CarryScrolls(3);
        Seed(client, new ConnectionInfo
        {
            CharacterHandle = Handle, CharacterName = "Dead", CharacterHp = 0, CharacterMp = 100
        });

        client.OnDataReceived(connection.BytesAvailable);

        // Skill 6001 level 1: 10 % of the 5000 max HP; var2 = 0 gives no MP, the 100 kept stay.
        ConnectionInfoOf(client).CharacterHp.Should().Be(500);
        ConnectionInfoOf(client).CharacterMp.Should().Be(100);
        Properties(connection).Should().Equal(("hp", 500L), ("mp", 100L));
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();

        // The stack update (255: handle then the count left) leaves before the vitals and the result.
        var ids = connection.Sent.Select(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)))
            .ToList();
        ids.First().Should().Be((ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT);
        var update = connection.Sent[0];
        BinaryPrimitives.ReadUInt32LittleEndian(update.AsSpan(7, 4)).Should().Be(ScrollHandle);
        BinaryPrimitives.ReadInt64LittleEndian(update.AsSpan(11, 8)).Should().Be(2);
    }

    [Test]
    public void ResurrectByItem_TheLastScrollIsDestroyed()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UsePotion));
        var client = NewGameClient(connection, out var services, realWarp: false);
        services.CarryScrolls(1);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterName = "Dead", CharacterHp = 0 });

        client.OnDataReceived(connection.BytesAvailable);

        var destroy = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(destroy.AsSpan(4, 2)).Should()
            .Be((ushort)GamePackets.TM_SC_DESTROY_ITEM);
        BinaryPrimitives.ReadUInt32LittleEndian(destroy.AsSpan(7, 4)).Should().Be(ScrollHandle);
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
    }

    [Test]
    public void ResurrectByItem_WithoutAResurrectionItem_IsRefusedAndChangesNothing()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UsePotion));
        var client = NewGameClient(connection, out var services, realWarp: false);
        services.CarryOnly(new ItemEntity { Id = 5, ItemResourceId = 601000, Amount = 10 });
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterName = "Dead", CharacterHp = 0 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        ConnectionInfoOf(client).CharacterHp.Should().Be(0);
        Properties(connection).Should().BeEmpty();
    }

    [Test]
    public void ResurrectByItem_ASecondRequestWhileTheFirstIsConsuming_IsRefused()
    {
        var frame = ClientFrame(Handle, ResurrectionType.UsePotion)
            .Concat(ClientFrame(Handle, ResurrectionType.UsePotion))
            .ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection, out var services, realWarp: false);
        var release = services.CarryScrollsBehindAGate(3);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterName = "Dead", CharacterHp = 0 });

        client.OnDataReceived(frame.Length);

        // The first request is waiting on the database: the second finds it in progress.
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));

        release();
        StorageTestHarness.WaitFor(() => Results(connection).Count == 2);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable),
            A574Result((ushort)ResultCode.Success));
        A.CallTo(() => services.Characters.ConsumeFirstAsync(A<string>._, A<Func<ItemEntity, bool>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void VitalsBySkill_FollowsBothReferenceFormulas()
    {
        // EF_RESURRECTION (504): max HP x var0 x level, max MP x var1 x level.
        ResurrectionRules.VitalsBySkill(SkillEffectType.Resurrection, new[] { 0.1m, 0.05m }, 2, 5000f, 800f, 0)
            .Should().Be((1000, 80));

        // EF_RESURRECTION_WITH_RECOVER (30501): max HP x (var0 + var1 x level), max MP x (var2 + var3 x level).
        ResurrectionRules.VitalsBySkill(SkillEffectType.ResurrectionWithRecover, new[] { 0.1m, 0.02m, 0.1m, 0m },
            5, 5000f, 800f, 10).Should().Be((1000, 90));
    }

    [Test]
    public void VitalsBySkill_NeverLeavesTheCharacterDeadNorAboveItsMaxima()
    {
        ResurrectionRules.VitalsBySkill(SkillEffectType.Resurrection, new[] { 0m, 0m }, 1, 5000f, 800f, 0)
            .Should().Be((1, 0));
        ResurrectionRules.VitalsBySkill(SkillEffectType.Resurrection, new[] { 3m, 3m }, 1, 5000f, 800f, 700)
            .Should().Be((5000, 800));
    }

    [Test]
    public void ResurrectionItemCatalog_ResolvesTheScrollThroughItsSkillEffectSlot()
    {
        // 603002 as the 9.4 data carries it: opt_type_0 = 5 (Skill), opt_var1_0 = 6001, opt_var2_0 = 1.
        var scroll = new ItemEffectFields(ResurrectionScrollId, ItemType.Etc, new short[4], new decimal[4],
            new decimal[4], new short[] { 5, 0, 0, 0 }, new[] { 6001m, 0m, 0m, 0m }, new[] { 1m, 0m, 0m, 0m });
        // The Creature Resurrection Scroll points at 6013, which targets summons only: the repository never
        // returns it, so the item is not a resurrection item for a character.
        var creatureScroll = scroll with { Id = 608406, OptVar1 = new[] { 6013m, 0m, 0m, 0m } };
        var skills = new[] { new ResurrectionSkillRow(6001, 504, new[] { 0.1m, 0m }) };

        var catalog = ResurrectionItemCatalog.Build(new[] { scroll, creatureScroll }, skills);

        catalog.Should().ContainKey(ResurrectionScrollId).And.NotContainKey(608406);
        var item = catalog[ResurrectionScrollId];
        (item.SkillId, item.SkillLevel, item.Effect).Should().Be((6001, 1, SkillEffectType.Resurrection));
    }

    private static (ushort RequestId, ushort Result) A574Result(ushort result) =>
        ((ushort)GamePackets.TM_CS_RESURRECTION, result);

    private static List<(ushort RequestId, ushort Result)> Results(FrameConnection connection) =>
        connection.Sent
            .Where(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
                             == (ushort)GamePackets.TM_SC_RESULT)
            .Select(packet => (
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2))))
            .ToList();

    private static List<(string Name, long Value)> Properties(FrameConnection connection) =>
        connection.Sent
            .Where(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
                             == (ushort)GamePackets.TM_SC_PROPERTY)
            .Select(packet => (
                Encoding.ASCII.GetString(packet, 12, 16).TrimEnd('\0'),
                BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(28, 8))))
            .ToList();

    /// <summary>
    /// The tests live in another assembly, so the internal connection state has to be reached by
    /// reflection. The client reads it per packet, so replacing it seeds the whole session.
    /// </summary>
    private static void Seed(GameClient client, ConnectionInfo info)
    {
        var property = SessionState();

        property.Should().NotBeNull("ConnectionInfo is expected to stay the single session state holder");
        property!.SetValue(client, info);
    }

    private static ConnectionInfo ConnectionInfoOf(GameClient client) =>
        (ConnectionInfo)SessionState().GetValue(client)!;

    private static PropertyInfo SessionState() => typeof(Client).GetProperty(nameof(ConnectionInfo),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static GameClient NewGameClient(FrameConnection connection, out TestServices services,
        bool realWarp = true)
    {
        services = new TestServices();

        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "resurrection-test-key" }),
            A.Fake<ICharacterService>(),
            A.Fake<IBannedWordsRepository>(),
            services.StatService,
            Options.Create(new ServerOptions()),
            A.Fake<INpcSpawnService>(),
            A.Fake<INpcDialogService>(),
            A.Fake<IMonsterSpawnService>(),
            A.Fake<ICombatService>(),
            A.Fake<ILevelingService>(),
            A.Fake<ISkillService>(),
            A.Fake<IEquipmentService>(),
            A.Fake<IInventoryService>(),
            A.Fake<IGroundItemService>(),
            A.Fake<ISkillCastService>(),
            A.Fake<IFieldPropService>(),
            A.Fake<IItemUseService>(),
            A.Fake<IMarketSellService>(),
            A.Fake<IWorldLocationService>(),
            new ResurrectionService(realWarp ? services.WarpService : services.WarpCalls,
                services.StatService, services.StateCatalog, services.SkillCast, services.Characters,
                services.ResurrectionItems),
            A.Fake<IEventAreaService>(),
            A.Fake<ICraftingSocleService>(),
            A.Fake<IStorageService>(),
            A.Fake<IQuestService>(),
            A.Fake<IGmCommandService>(),
                A.Fake<Navislamia.Game.Services.Pets.IPetSummonService>());

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

    /// <summary>
    /// The fakes the assertions reach for. The warp service is the real one, so the frame it sends —
    /// and not merely the call — proves where the character reappears; the refused requests are run
    /// against the fake instead, to prove they never reach it.
    /// </summary>
    private sealed class TestServices
    {
        public IStatService StatService { get; } = A.Fake<IStatService>();

        public IWarpService WarpService { get; }

        public IWarpService WarpCalls { get; } = A.Fake<IWarpService>();

        public IStateCatalog StateCatalog { get; } = A.Fake<IStateCatalog>();

        public ISkillCastService SkillCast { get; } = A.Fake<ISkillCastService>();

        public ICharacterService Characters { get; } = A.Fake<ICharacterService>();

        public IResurrectionItemCatalog ResurrectionItems { get; } = A.Fake<IResurrectionItemCatalog>();

        /// <summary>The bag holds a stack of Resurrection Scrolls; the service consumes one of them.</summary>
        public void CarryScrolls(long amount) =>
            CarryOnly(new ItemEntity { Id = ScrollHandle, ItemResourceId = ResurrectionScrollId, Amount = amount });

        /// <summary>
        /// Plays the consumption against one carried item: the service's own predicate decides whether it
        /// matches, and a matching item loses one unit — what CharacterService.ConsumeFirstAsync does.
        /// </summary>
        public void CarryOnly(ItemEntity item) =>
            A.CallTo(() => Characters.ConsumeFirstAsync(A<string>._, A<Func<ItemEntity, bool>>._))
                .ReturnsLazily((string _, Func<ItemEntity, bool> match) =>
                    Task.FromResult<(ItemEntity Item, long Remaining)?>(match(item) ? (item, item.Amount - 1) : null));

        /// <summary>As <see cref="CarryScrolls"/>, but the consumption waits until the returned action runs.</summary>
        public Action CarryScrollsBehindAGate(long amount)
        {
            var gate = new TaskCompletionSource();
            var scroll = new ItemEntity { Id = ScrollHandle, ItemResourceId = ResurrectionScrollId, Amount = amount };
            A.CallTo(() => Characters.ConsumeFirstAsync(A<string>._, A<Func<ItemEntity, bool>>._))
                .ReturnsLazily(async (string _, Func<ItemEntity, bool> match) =>
                {
                    await gate.Task;
                    return match(scroll) ? (scroll, scroll.Amount - 1) : ((ItemEntity, long)?)null;
                });
            return () => gate.SetResult();
        }

        public TestServices()
        {
            var scroll = new ResurrectionItem(ResurrectionScrollId, 6001, 1, SkillEffectType.Resurrection,
                new[] { 0.1m, 0m });
            A.CallTo(() => ResurrectionItems.TryGet(A<int>._, out scroll)).Returns(false);
            A.CallTo(() => ResurrectionItems.TryGet(ResurrectionScrollId, out scroll))
                .Returns(true).AssignsOutAndRefParameters(scroll);

            // State 13472 as the 9.4 data carries it: 5 % of the HP and 3 % of the MP, flat.
            var resurrection = new ResurrectionStateValues(0.05m, 0m, 0.03m, 0m);
            A.CallTo(() => StateCatalog.TryGetResurrection(ResurrectionStateId, out resurrection))
                .Returns(true).AssignsOutAndRefParameters(resurrection);

            A.CallTo(() => StatService.Compute(A<ConnectionInfo>._)).Returns(
                new CharacterStatResult(new StatBlock { MaxHp = 5000f, MaxMp = 800f }, new StatBlock()));

            WarpService = new WarpService(A.Fake<INpcSpawnService>(), A.Fake<IMonsterSpawnService>(),
                A.Fake<IFieldPropService>(), A.Fake<ICombatService>(),
                A.Fake<Navislamia.Game.Services.Pets.IPetSummonService>());
        }
    }

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by
    /// byte and records everything the receive loop pushes back.
    /// </summary>
    private sealed class FrameConnection : Connection
    {
        private readonly byte[] _frame;
        private int _offset;

        public FrameConnection(byte[] frame)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            _frame = frame;
        }

        public List<byte[]> Sent { get; } = new();

        public int BytesAvailable => _frame.Length - _offset;

        public override ReadOnlySpan<byte> Peek(int length) => new(_frame, _offset, length);

        public override byte[] Read(int input)
        {
            var length = Math.Min(BytesAvailable, input);
            var read = _frame.AsSpan(_offset, length).ToArray();
            _offset += length;
            return read;
        }

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }
}
