using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Navislamia.AuthServer.Crypto;
using Navislamia.Game.Network.Clients;

namespace Navislamia.LoadTest;

/// <summary>
/// The frames a bot sends and the fields it reads, at the Epic 7.3 sizes the server reads them at
/// (CLAUDE.md, the packet sheets of docs/packet-specs). Header: <c>uint Length</c>, <c>ushort ID</c>,
/// <c>byte Checksum</c> = sum of the first six bytes.
/// </summary>
public static class Frames
{
    public delegate void PayloadWriter(Span<byte> payload);

    public const int HeaderSize = 7;

    // Auth (client side of AuthServer.Protocol.AuthClientPackets).
    public const ushort AuthResult = 10000;
    public const ushort AuthVersion = 10001;
    public const ushort AuthAccount = 10010;
    public const ushort AuthServerListRequest = 10021;
    public const ushort AuthServerList = 10022;
    public const ushort AuthSelectServer = 10023;
    public const ushort AuthSelectServerResult = 10024;

    // Game.
    public const ushort Result = 0;
    public const ushort Login = 1;
    public const ushort TimeSync = 2;
    public const ushort Enter = 3;
    public const ushort LoginResult = 4;
    public const ushort MoveRequest = 5;
    public const ushort Move = 8;
    public const ushort Leave = 9;
    public const ushort Warp = 12;
    public const ushort ChatRequest = 20;
    public const ushort ChatLocal = 21;
    public const ushort Disconnect = 28;
    public const ushort Version = 50;
    public const ushort AttackRequest = 100;
    public const ushort AttackEvent = 101;
    public const ushort CantAttack = 102;
    public const ushort CancelAction = 150;
    public const ushort StatusChange = 500;
    public const ushort Property = 507;
    public const ushort Resurrection = 513;
    public const ushort CharacterList = 2001;
    public const ushort CreateCharacter = 2002;
    public const ushort CharacterListResult = 2004;
    public const ushort AccountWithAuth = 2005;

    public static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    public static byte[] Build(ushort id, int payloadLength, PayloadWriter? write = null)
    {
        var frame = new byte[HeaderSize + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += frame[i];
        frame[6] = checksum;
        write?.Invoke(frame.AsSpan(HeaderSize));
        return frame;
    }

    public static void Ascii(Span<byte> destination, string value)
    {
        destination.Clear();
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length - 1)).CopyTo(destination);
    }

    public static string ReadAscii(ReadOnlySpan<byte> source)
    {
        var end = source.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? source : source[..end]);
    }

    // ---- auth ----

    public static byte[] AuthVersionFrame() => Build(AuthVersion, 20, p => Ascii(p, "200701120"));

    public static byte[] AuthAccountFrame(string account, string password) => Build(AuthAccount, 122, p =>
    {
        Ascii(p[..61], account);
        DesPasswordCipher.EncryptPassword(password).CopyTo(p[61..]);
    });

    public static byte[] AuthServerListFrame() => Build(AuthServerListRequest, 0);

    public static byte[] AuthSelectServerFrame(ushort index) =>
        Build(AuthSelectServer, 4, p => BinaryPrimitives.WriteUInt32LittleEndian(p, index));

    /// <summary>TS_AC_RESULT: request id @7, result @9.</summary>
    public static ushort AuthResultCode(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(9));

    /// <summary>The servers of TS_AC_SERVER_LIST: count @9, then 302-byte entries from @11.</summary>
    public static List<(ushort Index, string Ip, int Port)> ServerList(byte[] f)
    {
        var servers = new List<(ushort, string, int)>();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(9));
        for (var i = 0; i < count && 11 + (i + 1) * 302 <= f.Length; i++)
        {
            var e = f.AsSpan(11 + i * 302, 302);
            servers.Add((BinaryPrimitives.ReadUInt16LittleEndian(e), ReadAscii(e.Slice(280, 16)),
                BinaryPrimitives.ReadInt32LittleEndian(e[296..])));
        }

        return servers;
    }

    /// <summary>TS_AC_SELECT_SERVER: result @7, one-time key @9.</summary>
    public static (ushort Result, long Key) SelectServer(byte[] f) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(7)), BinaryPrimitives.ReadInt64LittleEndian(f.AsSpan(9)));

    // ---- game: lobby ----

    public static byte[] VersionFrame() => Build(Version, 20, p => Ascii(p, "200701120"));

    public static byte[] AccountWithAuthFrame(string account, long key) => Build(AccountWithAuth, 69, p =>
    {
        Ascii(p[..61], account);
        BinaryPrimitives.WriteInt64LittleEndian(p[61..], key);
    });

    public static byte[] CharacterListFrame(string account) => Build(CharacterList, 61, p => Ascii(p, account));

    public static byte[] CreateCharacterFrame(LobbyCharacterInfo info)
    {
        var size = Marshal.SizeOf<LobbyCharacterInfo>();
        var payload = new byte[size];
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, memory, false);
            Marshal.Copy(memory, payload, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }

        return Build(CreateCharacter, size, p => payload.CopyTo(p));
    }

    /// <summary>The names and races of TS_SC_CHARACTER_LIST: count @13, then LobbyCharacterInfo records from @15.</summary>
    public static List<(string Name, int Race)> Characters(byte[] f)
    {
        var size = Marshal.SizeOf<LobbyCharacterInfo>();
        var nameOffset = (int)Marshal.OffsetOf<LobbyCharacterInfo>(nameof(LobbyCharacterInfo.Name));
        var raceOffset = (int)Marshal.OffsetOf<LobbyCharacterInfo>(nameof(LobbyCharacterInfo.Race));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(13));
        var list = new List<(string, int)>();
        for (var i = 0; i < count && 15 + (i + 1) * size <= f.Length; i++)
        {
            var record = f.AsSpan(15 + i * size, size);
            list.Add((ReadAscii(record.Slice(nameOffset, 19)), BinaryPrimitives.ReadInt32LittleEndian(record[raceOffset..])));
        }

        return list;
    }

    public static byte[] LoginFrame(string name, int race) => Build(Login, 20, p =>
    {
        Ascii(p[..19], name);
        p[19] = (byte)race;
    });

    /// <summary>TS_SC_RESULT: request id @7, result @9.</summary>
    public static (ushort Request, ushort Code) ResultOf(byte[] f) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(7)), BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(9)));

    /// <summary>TS_SC_LOGIN_RESULT: result @7, handle @9, x @13, y @17, hp @34.</summary>
    public static (ushort Result, uint Handle, float X, float Y, int Hp) LoginResultOf(byte[] f) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(7)), BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(9)),
            BinaryPrimitives.ReadSingleLittleEndian(f.AsSpan(13)), BinaryPrimitives.ReadSingleLittleEndian(f.AsSpan(17)),
            BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(34)));

    // ---- game: world ----

    public static byte[] TimeSyncFrame(uint clientTick) =>
        Build(TimeSync, 4, p => BinaryPrimitives.WriteUInt32LittleEndian(p, clientTick));

    /// <summary>TM_CS_MOVE_REQUEST: handle, x, y, cur_time, speed byte, count, waypoints — 26 + 8 × n.</summary>
    public static byte[] MoveRequestFrame(uint handle, float x, float y, uint tick, float toX, float toY) =>
        Build(MoveRequest, 27, p =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(p, handle);
            BinaryPrimitives.WriteSingleLittleEndian(p[4..], x);
            BinaryPrimitives.WriteSingleLittleEndian(p[8..], y);
            BinaryPrimitives.WriteUInt32LittleEndian(p[12..], tick);
            BinaryPrimitives.WriteUInt16LittleEndian(p[17..], 1);
            BinaryPrimitives.WriteSingleLittleEndian(p[19..], toX);
            BinaryPrimitives.WriteSingleLittleEndian(p[23..], toY);
        });

    /// <summary>TM_CS_CHAT_REQUEST: target[21], request id, count (message + NUL), type, message.</summary>
    public static byte[] ChatFrame(string message)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        return Build(ChatRequest, 24 + bytes.Length + 1, p =>
        {
            p[22] = (byte)(bytes.Length + 1);
            p[23] = 0;
            bytes.CopyTo(p[24..]);
        });
    }

    public static byte[] AttackFrame(uint handle, uint target) => Build(AttackRequest, 8, p =>
    {
        BinaryPrimitives.WriteUInt32LittleEndian(p, handle);
        BinaryPrimitives.WriteUInt32LittleEndian(p[4..], target);
    });

    public static byte[] CancelActionFrame(uint handle) =>
        Build(CancelAction, 4, p => BinaryPrimitives.WriteUInt32LittleEndian(p, handle));

    public static byte[] ResurrectionFrame(uint handle) =>
        Build(Resurrection, 5, p => BinaryPrimitives.WriteUInt32LittleEndian(p, handle));

    public static uint U32(byte[] f, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(offset));
    public static int I32(byte[] f, int offset) => BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(offset));
    public static float F32(byte[] f, int offset) => BinaryPrimitives.ReadSingleLittleEndian(f.AsSpan(offset));

    /// <summary>The last waypoint of a TS_SC_MOVE (count @17, waypoints from @19), or null without one.</summary>
    public static (float X, float Y)? MoveDestination(byte[] f)
    {
        if (f.Length < 19) return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(17));
        if (count == 0 || f.Length < 19 + count * 8) return null;
        var last = 19 + (count - 1) * 8;
        return (F32(f, last), F32(f, last + 4));
    }

    /// <summary>TS_SC_ATTACK_EVENT: attacker @7, target @11, count @21, 61-byte hits from @22, target hp at +37.</summary>
    public static (uint Attacker, uint Target, int? TargetHp) AttackOf(byte[] f)
    {
        var count = f.Length > 21 ? f[21] : 0;
        int? hp = count > 0 && f.Length >= 22 + 61 * count ? I32(f, 22 + 61 * (count - 1) + 37) : null;
        return (U32(f, 7), U32(f, 11), hp);
    }

    /// <summary>TS_SC_PROPERTY of a number: handle @7, name[16] @12, value @28.</summary>
    public static (uint Handle, string Name, long Value) PropertyOf(byte[] f) =>
        (U32(f, 7), ReadAscii(f.AsSpan(12, 16)), BinaryPrimitives.ReadInt64LittleEndian(f.AsSpan(28)));
}
