using System.Runtime.InteropServices;

namespace Navislamia.Game.Network.Packets.Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_SC_LOGIN_RESULT
{
    public ushort Result;
    public uint Handle;
    public float X;
    public float Y;
    public float Z;
    public byte Layer;
    public float FaceDirection;
    public int RegionSize;
    public int Hp;
    public int Mp;
    public int MaxHp;
    public int MaxMp;
    public int Havoc;
    public int MaxHavoc;
    public int Sex;
    public int Race;
    public uint SkinColor;
    public int FaceId;
    public int HairId;
    public int FaceTextureId; // this client build carries the skin/face texture id here, before the name

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 19)]
    public string Name;

    public int CellSize;
    public int GuildId;
}
