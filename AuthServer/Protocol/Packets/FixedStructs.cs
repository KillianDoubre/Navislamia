using System.Runtime.InteropServices;

namespace Navislamia.AuthServer.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_CA_VERSION
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
    public string Version;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_AC_RESULT
{
    public ushort RequestMsgId;
    public ushort Result;
    public int LoginFlag;

    public TS_AC_RESULT(ushort requestMsgId, ushort result, int loginFlag)
    {
        RequestMsgId = requestMsgId;
        Result = result;
        LoginFlag = loginFlag;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_CA_ACCOUNT
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 61)]
    public string Account;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 61)]
    public byte[] Password;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_CA_SELECT_SERVER
{
    public uint ServerIdx;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_AC_SELECT_SERVER
{
    public ushort Result;
    public long OneTimeKey;
    public uint PendingTime;

    public TS_AC_SELECT_SERVER(ushort result, long oneTimeKey, uint pendingTime)
    {
        Result = result;
        OneTimeKey = oneTimeKey;
        PendingTime = pendingTime;
    }
}
