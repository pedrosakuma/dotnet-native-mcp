namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// Random-access reader over a NativeFormat blob (typically one R2R section's
/// bytes). A safe port of <c>Internal.NativeFormat.NativeReader</c>: offsets are
/// relative to the start of the supplied blob and every read is bounds-checked
/// against its length. Fixed-width reads peek at an offset without advancing it;
/// the <c>Decode*</c> / <c>SkipInteger</c> methods return the offset of the byte
/// immediately past the value they consumed.
/// </summary>
internal sealed class NativeReader
{
    private readonly ReadOnlyMemory<byte> _blob;
    private readonly uint _size;

    public NativeReader(ReadOnlyMemory<byte> blob)
    {
        // Mirror the runtime guard: cap the blob so that offset + lookAhead
        // arithmetic can never overflow a uint.
        if ((uint)blob.Length >= uint.MaxValue / 4)
            throw new NativeFormatException("NativeFormat blob is too large.");

        _blob = blob;
        _size = (uint)blob.Length;
    }

    /// <summary>Length of the blob, in bytes.</summary>
    public uint Size => _size;

    public byte ReadUInt8(uint offset)
    {
        var local = offset;
        return NativePrimitiveDecoder.ReadUInt8(_blob.Span, ref local, _size);
    }

    public ushort ReadUInt16(uint offset)
    {
        var local = offset;
        return NativePrimitiveDecoder.ReadUInt16(_blob.Span, ref local, _size);
    }

    public uint ReadUInt32(uint offset)
    {
        var local = offset;
        return NativePrimitiveDecoder.ReadUInt32(_blob.Span, ref local, _size);
    }

    public ulong ReadUInt64(uint offset)
    {
        var local = offset;
        return NativePrimitiveDecoder.ReadUInt64(_blob.Span, ref local, _size);
    }

    public uint DecodeUnsigned(uint offset, out uint value)
    {
        value = NativePrimitiveDecoder.DecodeUnsigned(_blob.Span, ref offset, _size);
        return offset;
    }

    public uint DecodeSigned(uint offset, out int value)
    {
        value = NativePrimitiveDecoder.DecodeSigned(_blob.Span, ref offset, _size);
        return offset;
    }

    public uint DecodeUnsignedLong(uint offset, out ulong value)
    {
        value = NativePrimitiveDecoder.DecodeUnsignedLong(_blob.Span, ref offset, _size);
        return offset;
    }

    public uint DecodeSignedLong(uint offset, out long value)
    {
        value = NativePrimitiveDecoder.DecodeSignedLong(_blob.Span, ref offset, _size);
        return offset;
    }

    public uint SkipInteger(uint offset)
    {
        NativePrimitiveDecoder.SkipInteger(_blob.Span, ref offset, _size);
        return offset;
    }
}
