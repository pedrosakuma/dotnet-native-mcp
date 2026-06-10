namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// Forward cursor over a <see cref="NativeReader"/>, mirroring
/// <c>Internal.NativeFormat.NativeParser</c>. Each <c>Get*</c> call decodes one
/// value at the current <see cref="Offset"/> and advances past it. Relative
/// offsets are signed deltas from the position of the delta itself, matching the
/// runtime's <c>GetRelativeOffset</c> convention.
/// </summary>
internal struct NativeParser : IEquatable<NativeParser>
{
    private readonly NativeReader _reader;
    private uint _offset;

    public NativeParser(NativeReader reader, uint offset)
    {
        _reader = reader;
        _offset = offset;
    }

    /// <summary>True when this parser carries no reader (the default value).</summary>
    public readonly bool IsNull => _reader is null;

    public readonly NativeReader Reader => _reader;

    public uint Offset
    {
        readonly get => _offset;
        set => _offset = value;
    }

    public byte GetUInt8()
    {
        byte value = _reader.ReadUInt8(_offset);
        _offset += 1;
        return value;
    }

    public uint GetUnsigned()
    {
        _offset = _reader.DecodeUnsigned(_offset, out uint value);
        return value;
    }

    public ulong GetUnsignedLong()
    {
        _offset = _reader.DecodeUnsignedLong(_offset, out ulong value);
        return value;
    }

    public int GetSigned()
    {
        _offset = _reader.DecodeSigned(_offset, out int value);
        return value;
    }

    public long GetSignedLong()
    {
        _offset = _reader.DecodeSignedLong(_offset, out long value);
        return value;
    }

    /// <summary>
    /// Decodes a signed delta and returns the absolute offset it points to
    /// (delta added to the position the delta was read from).
    /// </summary>
    public uint GetRelativeOffset()
    {
        uint pos = _offset;
        _offset = _reader.DecodeSigned(_offset, out int delta);
        return pos + (uint)delta;
    }

    public void SkipInteger()
    {
        _offset = _reader.SkipInteger(_offset);
    }

    /// <summary>Returns a new parser positioned at the target of a relative offset.</summary>
    public NativeParser GetParserFromRelativeOffset() =>
        new(_reader, GetRelativeOffset());

    public uint GetSequenceCount() => GetUnsigned();

    public readonly bool Equals(NativeParser other) =>
        ReferenceEquals(_reader, other._reader) && _offset == other._offset;

    public override readonly bool Equals(object? obj) =>
        obj is NativeParser other && Equals(other);

    public override readonly int GetHashCode() =>
        HashCode.Combine(_reader, _offset);

    public static bool operator ==(NativeParser left, NativeParser right) => left.Equals(right);

    public static bool operator !=(NativeParser left, NativeParser right) => !left.Equals(right);
}
