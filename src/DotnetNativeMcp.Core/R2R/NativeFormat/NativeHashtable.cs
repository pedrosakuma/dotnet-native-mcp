namespace DotnetNativeMcp.Core.R2R.NativeFormat;

/// <summary>
/// A NativeFormat hashtable. A safe port of
/// <c>ILCompiler.Reflection.ReadyToRun.NativeHashtable</c> (itself based on
/// <c>NativeFormat::NativeHashtable</c> in <c>src/coreclr/vm/nativeformatreader.h</c>).
/// The table is a header byte (<c>numberOfBucketsShift</c> + <c>entryIndexSize</c>)
/// followed by a flat bucket-offset index and the entry payloads. Each entry slot
/// is a low-hashcode byte followed by a signed relative offset to the entry's
/// payload. <see cref="EnumerateAllEntries"/> walks every bucket and yields a
/// <see cref="NativeParser"/> positioned at each entry's payload.
/// </summary>
/// <remarks>
/// All reads go through the bounds-checked <see cref="NativeReader"/>, so a malformed
/// blob throws <see cref="NativeFormatException"/> rather than reading out of range.
/// </remarks>
internal readonly struct NativeHashtable
{
    private readonly NativeReader _reader;
    private readonly uint _baseOffset;
    private readonly uint _bucketMask;
    private readonly byte _entryIndexSize;

    public NativeHashtable(NativeReader reader, NativeParser parser, uint endOffset)
    {
        uint header = parser.GetUInt8();
        _baseOffset = parser.Offset;
        _reader = reader;

        var numberOfBucketsShift = (int)(header >> 2);
        if (numberOfBucketsShift > 31)
            throw new NativeFormatException("NativeHashtable bucket-shift is out of range.");
        _bucketMask = (uint)((1 << numberOfBucketsShift) - 1);

        var entryIndexSize = (byte)(header & 3);
        if (entryIndexSize > 2)
            throw new NativeFormatException("NativeHashtable entry-index size is out of range.");
        _entryIndexSize = entryIndexSize;

        EndOffset = endOffset;
    }

    /// <summary>Offset (relative to the blob) at which the table's data ends.</summary>
    public uint EndOffset { get; }

    private NativeParser GetParserForBucket(uint bucket, out uint endOffset)
    {
        uint start, end;

        if (_entryIndexSize == 0)
        {
            var bucketOffset = _baseOffset + bucket;
            start = _reader.ReadUInt8(bucketOffset);
            end = _reader.ReadUInt8(bucketOffset + 1);
        }
        else if (_entryIndexSize == 1)
        {
            var bucketOffset = _baseOffset + 2 * bucket;
            start = _reader.ReadUInt16(bucketOffset);
            end = _reader.ReadUInt16(bucketOffset + 2);
        }
        else
        {
            var bucketOffset = _baseOffset + 4 * bucket;
            start = _reader.ReadUInt32(bucketOffset);
            end = _reader.ReadUInt32(bucketOffset + 4);
        }

        endOffset = end + _baseOffset;
        return new NativeParser(_reader, _baseOffset + start);
    }

    public AllEntriesEnumerator EnumerateAllEntries(uint maxScan) => new(this, maxScan);

    /// <summary>
    /// Walks every bucket and yields one <see cref="NativeParser"/> per entry,
    /// positioned at the entry's payload. The bucket count comes from the untrusted
    /// header, so the traversal is bounded by <c>maxScan</c> total steps (entry yields
    /// plus empty-bucket advances); when the cap is hit <see cref="Truncated"/> is set
    /// and enumeration stops.
    /// </summary>
    internal struct AllEntriesEnumerator
    {
        private readonly NativeHashtable _table;
        private readonly uint _maxScan;
        private NativeParser _parser;
        private uint _currentBucket;
        private uint _endOffset;
        private uint _steps;

        internal AllEntriesEnumerator(NativeHashtable table, uint maxScan)
        {
            _table = table;
            _maxScan = maxScan == 0 ? 1 : maxScan;
            _currentBucket = 0;
            _steps = 0;
            Truncated = false;
            _parser = table.GetParserForBucket(0, out _endOffset);
        }

        /// <summary><c>true</c> when the scan cap was reached before all buckets were walked.</summary>
        public bool Truncated { get; private set; }

        public NativeParser GetNext()
        {
            for (; ; )
            {
                if (_steps >= _maxScan)
                {
                    Truncated = true;
                    return default;
                }

                _steps++;

                if (_parser.Offset < _endOffset)
                {
                    // Each entry slot is [low-hashcode byte][signed relative offset to
                    // the payload]. Consume the hashcode (unused here) and resolve the
                    // payload offset; _parser advances to the next slot.
                    _parser.GetUInt8();
                    var entryOffset = _parser.GetRelativeOffset();
                    return new NativeParser(_table._reader, entryOffset);
                }

                if (_currentBucket >= _table._bucketMask)
                    return default;

                _currentBucket++;
                _parser = _table.GetParserForBucket(_currentBucket, out _endOffset);
            }
        }
    }
}
