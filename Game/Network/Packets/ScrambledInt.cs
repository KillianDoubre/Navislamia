namespace Navislamia.Game.Network.Packets;

public static class ScrambledInt
{
    private static readonly byte[] EncodeMap = new byte[32];
    private static readonly byte[] DecodeMap = new byte[32];

    static ScrambledInt()
    {
        for (byte i = 0; i < 32; i++)
        {
            EncodeMap[i] = i;
        }

        byte j = 3;
        for (byte i = 0; i < 32; i++)
        {
            (EncodeMap[i], EncodeMap[j]) = (EncodeMap[j], EncodeMap[i]);
            j = (byte)((j + i + 3) % 32);
        }

        for (byte i = 0; i < 32; i++)
        {
            DecodeMap[EncodeMap[i]] = i;
        }
    }

    public static uint Encode(uint value)
    {
        uint result = 0;
        for (var i = 0; i < 32; i++, value >>= 1)
        {
            if ((value & 1) != 0)
            {
                result |= 1u << EncodeMap[i];
            }
        }

        return result;
    }

    public static uint Decode(uint value)
    {
        uint result = 0;
        for (var i = 0; i < 32; i++, value >>= 1)
        {
            if ((value & 1) != 0)
            {
                result |= 1u << DecodeMap[i];
            }
        }

        return result;
    }
}
