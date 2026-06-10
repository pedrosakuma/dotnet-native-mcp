namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// A sparse, index-addressable array encoded in the runtime's NativeFormat. A
/// faithful, safe port of <c>ILCompiler.Reflection.ReadyToRun.NativeArray</c>
/// (itself based on <c>NativeFormat::NativeArray</c> in
/// <c>src/coreclr/vm/nativeformatreader.h</c>).
/// </summary>
/// <remarks>
/// The array is stored as a header (<c>nElements</c> + <c>entryIndexSize</c>),
/// a flat index of block start offsets — one per 16-element block — and a
/// bit-tree per block. <see cref="TryGetAt"/> navigates the bit-tree to recover
/// the per-element payload offset, returning <see langword="false"/> for absent
/// elements. Every underlying read is bounds-checked by <see cref="NativeReader"/>,
/// so a malformed blob throws <see cref="NativeFormatException"/> rather than
/// reading out of range.
/// </remarks>
internal sealed class NativeArray
{
    private const uint BlockSize = 16;

    private readonly NativeReader _reader;
    private readonly uint _baseOffset;
    private readonly uint _nElements;
    private readonly byte _entryIndexSize;

    public NativeArray(NativeReader reader, uint offset)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _baseOffset = reader.DecodeUnsigned(offset, out var val);
        _nElements = val >> 2;
        _entryIndexSize = (byte)(val & 3);
    }

    /// <summary>Number of element slots in the array (some may be absent).</summary>
    public uint Count => _nElements;

    /// <summary>
    /// Recovers the payload offset for <paramref name="index"/>. Returns
    /// <see langword="false"/> when the element is not present in the array.
    /// </summary>
    public bool TryGetAt(uint index, out uint payloadOffset)
    {
        payloadOffset = 0;
        if (index >= _nElements)
            return false;

        uint offset;
        if (_entryIndexSize == 0)
        {
            var i = _baseOffset + index / BlockSize;
            offset = _reader.ReadUInt8(i);
        }
        else if (_entryIndexSize == 1)
        {
            var i = _baseOffset + 2 * (index / BlockSize);
            offset = _reader.ReadUInt16(i);
        }
        else
        {
            var i = _baseOffset + 4 * (index / BlockSize);
            offset = _reader.ReadUInt32(i);
        }

        offset += _baseOffset;

        for (var bit = BlockSize >> 1; bit > 0; bit >>= 1)
        {
            var offset2 = _reader.DecodeUnsigned(offset, out var val);
            if ((index & bit) != 0)
            {
                if ((val & 2) != 0)
                {
                    offset += val >> 2;
                    continue;
                }
            }
            else
            {
                if ((val & 1) != 0)
                {
                    offset = offset2;
                    continue;
                }
            }

            // Not found, unless this is a matching special leaf node.
            if ((val & 3) == 0 && (val >> 2) == (index & (BlockSize - 1)))
            {
                offset = offset2;
                break;
            }

            return false;
        }

        payloadOffset = offset;
        return true;
    }
}
