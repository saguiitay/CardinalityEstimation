#if !NETCOREAPP3_0_OR_GREATER
namespace System.Numerics;

internal static class BitOperations
{
    public static uint LeadingZeroCount(ulong x)
    {
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
        x |= x >> 32;

        x -= x >> 1 & 0x5555555555555555;
        x = (x >> 2 & 0x3333333333333333) + (x & 0x3333333333333333);
        x = (x >> 4) + x & 0x0f0f0f0f0f0f0f0f;
        x += x >> 8;
        x += x >> 16;
        x += x >> 32;

        const int numLongBits = sizeof(long) * 8;
        return numLongBits - (uint)(x & 0x0000007f);
    }
}
#endif
