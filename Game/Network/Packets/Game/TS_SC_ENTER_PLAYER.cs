using System.Runtime.InteropServices;

namespace Navislamia.Game.Network.Packets.Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_SC_ENTER_PLAYER
{
    public byte Type;
    public uint Handle;
    public float X;
    public float Y;
    public float Z;
    public byte Layer;
    public byte ObjType;

    public uint Status;
    public float FaceDirection;
    public int Hp;
    public int MaxHp;
    public int Mp;
    public int MaxMp;
    public int Level;
    public byte Race;
    public uint SkinColor;
    public byte IsFirstEnter;
    public int Energy;

    public byte Sex;
    public uint FaceId;
    public uint FaceTextureId;
    public uint HairId;
    public uint HairColorIndex;
    public uint HairColorRGB;
    public uint HideEquipFlag;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 19)]
    public string Name;

    public ushort JobId;
    public uint RideHandle;
    public uint GuildId;
}
