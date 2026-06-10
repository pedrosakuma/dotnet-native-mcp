using System.Buffers.Binary;

namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// Decodes the variable-length integer encoding used by the runtime's
/// NativeFormat writer. A faithful, allocation-free port of
/// <c>Internal.NativeFormat.NativePrimitiveDecoder</c>
/// (<c>src/coreclr/tools/Common/Internal/NativeFormat/NativeFormatReader.cs</c>),
/// re-expressed over <see cref="ReadOnlySpan{Byte}"/> + a <c>ref uint</c> cursor
/// instead of raw <c>byte*</c> pointers.
/// </summary>
/// <remarks>
/// The low bits of the first byte select the encoding width:
/// <list type="bullet">
/// <item><description><c>xxxxxxx0</c> — 1 byte, value in the high 7 bits.</description></item>
/// <item><description><c>xxxxxx01</c> — 2 bytes.</description></item>
/// <item><description><c>xxxxx011</c> — 3 bytes.</description></item>
/// <item><description><c>xxxx0111</c> — 4 bytes.</description></item>
/// <item><description><c>xxx01111</c> — 5 bytes (the next 4 bytes are a raw little-endian uint32).</description></item>
/// </list>
/// The <c>Long</c> variants extend this: when the low 5 bits are all set
/// (<c>0b11111</c>) and bit 5 is clear, the next 8 bytes are a raw little-endian
/// uint64. Every read is bounds-checked against the blob length (<c>end</c>); an
/// out-of-range access throws <see cref="NativeFormatException"/> rather than
/// reading past the blob.
/// </remarks>
internal static class NativePrimitiveDecoder
{
    public static byte ReadUInt8(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();
        return blob[(int)offset++];
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset > end || end - offset < 2)
            throw OutOfRange();
        var value = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice((int)offset, 2));
        offset += 2;
        return value;
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset > end || end - offset < 4)
            throw OutOfRange();
        var value = BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice((int)offset, 4));
        offset += 4;
        return value;
    }

    public static ulong ReadUInt64(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset > end || end - offset < 8)
            throw OutOfRange();
        var value = BinaryPrimitives.ReadUInt64LittleEndian(blob.Slice((int)offset, 8));
        offset += 8;
        return value;
    }

    public static uint DecodeUnsigned(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();

        uint value;
        uint val = blob[(int)offset];
        if ((val & 1) == 0)
        {
            value = val >> 1;
            offset += 1;
        }
        else if ((val & 2) == 0)
        {
            if (offset + 1 >= end)
                throw OutOfRange();
            value = (val >> 2) |
                    ((uint)blob[(int)offset + 1] << 6);
            offset += 2;
        }
        else if ((val & 4) == 0)
        {
            if (offset + 2 >= end)
                throw OutOfRange();
            value = (val >> 3) |
                    ((uint)blob[(int)offset + 1] << 5) |
                    ((uint)blob[(int)offset + 2] << 13);
            offset += 3;
        }
        else if ((val & 8) == 0)
        {
            if (offset + 3 >= end)
                throw OutOfRange();
            value = (val >> 4) |
                    ((uint)blob[(int)offset + 1] << 4) |
                    ((uint)blob[(int)offset + 2] << 12) |
                    ((uint)blob[(int)offset + 3] << 20);
            offset += 4;
        }
        else if ((val & 16) == 0)
        {
            offset += 1;
            value = ReadUInt32(blob, ref offset, end);
        }
        else
        {
            throw OutOfRange();
        }

        return value;
    }

    public static int DecodeSigned(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();

        int value;
        int val = blob[(int)offset];
        if ((val & 1) == 0)
        {
            value = (sbyte)val >> 1;
            offset += 1;
        }
        else if ((val & 2) == 0)
        {
            if (offset + 1 >= end)
                throw OutOfRange();
            value = (val >> 2) |
                    ((sbyte)blob[(int)offset + 1] << 6);
            offset += 2;
        }
        else if ((val & 4) == 0)
        {
            if (offset + 2 >= end)
                throw OutOfRange();
            value = (val >> 3) |
                    (blob[(int)offset + 1] << 5) |
                    ((sbyte)blob[(int)offset + 2] << 13);
            offset += 3;
        }
        else if ((val & 8) == 0)
        {
            if (offset + 3 >= end)
                throw OutOfRange();
            value = (val >> 4) |
                    (blob[(int)offset + 1] << 4) |
                    (blob[(int)offset + 2] << 12) |
                    ((sbyte)blob[(int)offset + 3] << 20);
            offset += 4;
        }
        else if ((val & 16) == 0)
        {
            offset += 1;
            value = (int)ReadUInt32(blob, ref offset, end);
        }
        else
        {
            throw OutOfRange();
        }

        return value;
    }

    public static ulong DecodeUnsignedLong(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();

        byte val = blob[(int)offset];
        if ((val & 31) != 31)
            return DecodeUnsigned(blob, ref offset, end);

        if ((val & 32) == 0)
        {
            offset += 1;
            return ReadUInt64(blob, ref offset, end);
        }

        throw OutOfRange();
    }

    public static long DecodeSignedLong(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();

        byte val = blob[(int)offset];
        if ((val & 31) != 31)
            return DecodeSigned(blob, ref offset, end);

        if ((val & 32) == 0)
        {
            offset += 1;
            return (long)ReadUInt64(blob, ref offset, end);
        }

        throw OutOfRange();
    }

    /// <summary>Advances <paramref name="offset"/> past one encoded integer without decoding it.</summary>
    public static void SkipInteger(ReadOnlySpan<byte> blob, ref uint offset, uint end)
    {
        if (offset >= end)
            throw OutOfRange();

        byte val = blob[(int)offset];
        uint width;
        if ((val & 1) == 0) width = 1;
        else if ((val & 2) == 0) width = 2;
        else if ((val & 4) == 0) width = 3;
        else if ((val & 8) == 0) width = 4;
        else if ((val & 16) == 0) width = 5;
        else if ((val & 32) == 0) width = 9;
        else throw OutOfRange();

        if (offset + width > end)
            throw OutOfRange();
        offset += width;
    }

    private static NativeFormatException OutOfRange() =>
        new("NativeFormat decode ran past the end of the blob or hit a malformed encoding.");
}
