using System.Buffers.Binary;

namespace Vistumbler.Infrastructure.Export.M8b;

/// <summary>
/// SipHash-2-4 (Aumasson and Bernstein, 2012), written from the published algorithm. Magic 8 Ball files hash
/// each 6-byte MAC with it under an all-zero key.
/// </summary>
public static class SipHash24
{
    public static ulong Hash(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        ulong k0 = BinaryPrimitives.ReadUInt64LittleEndian(key[..8]);
        ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(key[8..16]);
        ulong v0 = 0x736f6d6570736575UL ^ k0;
        ulong v1 = 0x646f72616e646f6dUL ^ k1;
        ulong v2 = 0x6c7967656e657261UL ^ k0;
        ulong v3 = 0x7465646279746573UL ^ k1;

        int whole = data.Length - data.Length % 8;
        for (int i = 0; i < whole; i += 8)
        {
            ulong m = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i, 8));
            v3 ^= m;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= m;
        }

        // The last block: the remaining bytes, with the length in the top byte
        ulong last = (ulong)data.Length << 56;
        for (int i = 0; i < data.Length - whole; i++)
            last |= (ulong)data[whole + i] << (8 * i);
        v3 ^= last;
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        v0 ^= last;

        v2 ^= 0xff;
        for (int i = 0; i < 4; i++) Round(ref v0, ref v1, ref v2, ref v3);
        return v0 ^ v1 ^ v2 ^ v3;
    }

    private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
    {
        v0 += v1; v1 = ulong.RotateLeft(v1, 13); v1 ^= v0; v0 = ulong.RotateLeft(v0, 32);
        v2 += v3; v3 = ulong.RotateLeft(v3, 16); v3 ^= v2;
        v0 += v3; v3 = ulong.RotateLeft(v3, 21); v3 ^= v0;
        v2 += v1; v1 = ulong.RotateLeft(v1, 17); v1 ^= v2; v2 = ulong.RotateLeft(v2, 32);
    }
}
