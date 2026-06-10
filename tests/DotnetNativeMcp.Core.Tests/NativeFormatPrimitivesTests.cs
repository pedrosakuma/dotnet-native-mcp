using DotnetNativeMcp.Core.R2R.NativeFormat;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class NativeFormatPrimitivesTests
{
    // -----------------------------------------------------------------------
    // Round-trip: encode with a faithful port of the runtime's
    // NativePrimitiveEncoder, then decode and assert equality.
    // -----------------------------------------------------------------------

    public static TheoryData<uint> UnsignedValues() => new()
    {
        0u, 1u, 5u, 63u, 64u, 127u, 128u, 129u,
        8191u, 8192u, 16383u, 16384u,
        0x1FFFFFu, 0x200000u, 0x0FFFFFFFu, 0x10000000u,
        0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFEu, 0xFFFFFFFFu,
    };

    [Theory]
    [MemberData(nameof(UnsignedValues))]
    public void DecodeUnsigned_RoundTrips(uint value)
    {
        var w = new NfWriter();
        w.WriteUnsigned(value);
        var reader = new NativeReader(w.ToMemory());

        var next = reader.DecodeUnsigned(0, out var decoded);

        decoded.Should().Be(value);
        next.Should().Be((uint)w.Length, "the cursor should land exactly past the encoded value");
    }

    public static TheoryData<int> SignedValues() => new()
    {
        0, 1, -1, 5, -5, 63, -64, 64, -65, 127, -128, 128,
        8191, -8192, 8192, 16383, -16384,
        0x1FFFFF, -0x200000, 0x200000,
        0x0FFFFFFF, -0x10000000,
        int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1,
    };

    [Theory]
    [MemberData(nameof(SignedValues))]
    public void DecodeSigned_RoundTrips(int value)
    {
        var w = new NfWriter();
        w.WriteSigned(value);
        var reader = new NativeReader(w.ToMemory());

        reader.DecodeSigned(0, out var decoded);

        decoded.Should().Be(value);
    }

    public static TheoryData<ulong> UnsignedLongValues() => new()
    {
        0ul, 1ul, 127ul, 0xFFFFFFFFul,
        0x1_0000_0000ul, 0xDEAD_BEEF_CAFEul,
        ulong.MaxValue, ulong.MaxValue - 1,
    };

    [Theory]
    [MemberData(nameof(UnsignedLongValues))]
    public void DecodeUnsignedLong_RoundTrips(ulong value)
    {
        var w = new NfWriter();
        w.WriteUnsignedLong(value);
        var reader = new NativeReader(w.ToMemory());

        reader.DecodeUnsignedLong(0, out var decoded);

        decoded.Should().Be(value);
    }

    public static TheoryData<long> SignedLongValues() => new()
    {
        0L, 1L, -1L, int.MaxValue, int.MinValue,
        (long)int.MaxValue + 1, (long)int.MinValue - 1,
        long.MaxValue, long.MinValue, 0x1234_5678_9ABCL,
    };

    [Theory]
    [MemberData(nameof(SignedLongValues))]
    public void DecodeSignedLong_RoundTrips(long value)
    {
        var w = new NfWriter();
        w.WriteSignedLong(value);
        var reader = new NativeReader(w.ToMemory());

        reader.DecodeSignedLong(0, out var decoded);

        decoded.Should().Be(value);
    }

    [Fact]
    public void DecodeUnsigned_RandomFuzz_RoundTrips()
    {
        var rng = new Random(20260610);
        for (int i = 0; i < 5000; i++)
        {
            uint value = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
            var w = new NfWriter();
            w.WriteUnsigned(value);
            var reader = new NativeReader(w.ToMemory());
            reader.DecodeUnsigned(0, out var decoded);
            decoded.Should().Be(value);
        }
    }

    [Fact]
    public void DecodeSigned_RandomFuzz_RoundTrips()
    {
        var rng = new Random(776655);
        for (int i = 0; i < 5000; i++)
        {
            int value = rng.Next(int.MinValue, int.MaxValue);
            var w = new NfWriter();
            w.WriteSigned(value);
            var reader = new NativeReader(w.ToMemory());
            reader.DecodeSigned(0, out var decoded);
            decoded.Should().Be(value);
        }
    }

    // -----------------------------------------------------------------------
    // Encoding-width vectors (hand-verified against the runtime encoder).
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0u, 1)]            // 1-byte form
    [InlineData(5u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]          // 2-byte form
    [InlineData(16383u, 2)]
    [InlineData(16384u, 3)]        // 3-byte form
    [InlineData(0x1FFFFFu, 3)]
    [InlineData(0x200000u, 4)]     // 4-byte form
    [InlineData(0x0FFFFFFFu, 4)]
    [InlineData(0x10000000u, 5)]   // 5-byte (raw uint32) form
    [InlineData(0xFFFFFFFFu, 5)]
    public void WriteUnsigned_UsesExpectedWidth(uint value, int expectedBytes)
    {
        var w = new NfWriter();
        w.WriteUnsigned(value);
        w.Length.Should().Be(expectedBytes);
    }

    [Fact]
    public void DecodeUnsigned_OneByteForm_HasExpectedRawByte()
    {
        // value 5 -> 5 * 2 = 0x0A (low bit clear).
        var reader = new NativeReader(new byte[] { 0x0A }.AsMemory());
        reader.DecodeUnsigned(0, out var decoded).Should().Be(1u);
        decoded.Should().Be(5u);
    }

    // -----------------------------------------------------------------------
    // Fixed-width reads and offset handling.
    // -----------------------------------------------------------------------

    [Fact]
    public void FixedWidthReads_AreLittleEndian()
    {
        var bytes = new byte[] { 0x78, 0x56, 0x34, 0x12, 0xEF, 0xBE, 0xAD, 0xDE };
        var reader = new NativeReader(bytes.AsMemory());

        reader.ReadUInt8(0).Should().Be(0x78);
        reader.ReadUInt16(0).Should().Be(0x5678);
        reader.ReadUInt32(0).Should().Be(0x12345678u);
        reader.ReadUInt64(0).Should().Be(0xDEADBEEF12345678ul);
    }

    [Fact]
    public void FixedWidthReads_DoNotAdvanceCallerOffset()
    {
        var reader = new NativeReader(new byte[] { 1, 2, 3, 4 }.AsMemory());
        // Two reads at the same offset must return the same value.
        reader.ReadUInt16(1).Should().Be(reader.ReadUInt16(1));
    }

    // -----------------------------------------------------------------------
    // Truncation / out-of-range hardening: malformed input must throw the
    // sentinel NativeFormatException, never read past the blob.
    // -----------------------------------------------------------------------

    [Fact]
    public void DecodeUnsigned_TruncatedMultiByte_Throws()
    {
        // Encode a 3-byte value, then hand the reader only the first byte.
        var w = new NfWriter();
        w.WriteUnsigned(16384u); // 3-byte form
        var truncated = w.ToArray()[..1];
        var reader = new NativeReader(truncated.AsMemory());

        var act = () => reader.DecodeUnsigned(0, out _);
        act.Should().Throw<NativeFormatException>();
    }

    [Fact]
    public void DecodeUnsigned_TruncatedFiveByteForm_Throws()
    {
        // First byte 0x0F selects the raw-uint32 form but only 2 of 4 bytes follow.
        var reader = new NativeReader(new byte[] { 0x0F, 0x11, 0x22 }.AsMemory());

        var act = () => reader.DecodeUnsigned(0, out _);
        act.Should().Throw<NativeFormatException>();
    }

    [Fact]
    public void ReadUInt32_PastEnd_Throws()
    {
        var reader = new NativeReader(new byte[] { 1, 2, 3 }.AsMemory());
        var act = () => reader.ReadUInt32(0);
        act.Should().Throw<NativeFormatException>();
    }

    [Fact]
    public void ReadUInt8_AtEnd_Throws()
    {
        var reader = new NativeReader(new byte[] { 1 }.AsMemory());
        var act = () => reader.ReadUInt8(1);
        act.Should().Throw<NativeFormatException>();
    }

    [Fact]
    public void EmptyBlob_AnyRead_Throws()
    {
        var reader = new NativeReader(ReadOnlyMemory<byte>.Empty);
        ((Action)(() => reader.ReadUInt8(0))).Should().Throw<NativeFormatException>();
        ((Action)(() => reader.DecodeUnsigned(0, out _))).Should().Throw<NativeFormatException>();
    }

    [Fact]
    public void DecodeUnsignedLong_TruncatedEightByteForm_Throws()
    {
        // 0x1F marker selects the raw-uint64 form; supply too few bytes.
        var reader = new NativeReader(new byte[] { 0x1F, 1, 2, 3 }.AsMemory());
        var act = () => reader.DecodeUnsignedLong(0, out _);
        act.Should().Throw<NativeFormatException>();
    }

    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(uint.MaxValue - 1)]
    [InlineData(uint.MaxValue - 3)]
    [InlineData(uint.MaxValue - 7)]
    public void FixedWidthReads_WrappedOffset_ThrowNativeFormatException(uint offset)
    {
        // A near-uint.MaxValue offset must NOT wrap the bounds check
        // (offset + N) and surface as ArgumentOutOfRangeException from Slice;
        // it must be rejected as a NativeFormatException.
        var reader = new NativeReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.AsMemory());
        ((Action)(() => reader.ReadUInt16(offset))).Should().Throw<NativeFormatException>();
        ((Action)(() => reader.ReadUInt32(offset))).Should().Throw<NativeFormatException>();
        ((Action)(() => reader.ReadUInt64(offset))).Should().Throw<NativeFormatException>();
    }

    // -----------------------------------------------------------------------
    // SkipInteger
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(UnsignedValues))]
    public void SkipInteger_AdvancesPastTheValue(uint value)
    {
        var w = new NfWriter();
        w.WriteUnsigned(value);
        w.WriteUnsigned(0xABu); // sentinel after the skipped value
        var reader = new NativeReader(w.ToMemory());

        var afterSkip = reader.SkipInteger(0);
        reader.DecodeUnsigned(afterSkip, out var sentinel);

        sentinel.Should().Be(0xABu);
    }

    // -----------------------------------------------------------------------
    // NativeParser
    // -----------------------------------------------------------------------

    [Fact]
    public void NativeParser_Default_IsNull()
    {
        var parser = default(NativeParser);
        parser.IsNull.Should().BeTrue();
    }

    [Fact]
    public void NativeParser_DecodesSequenceOfValues()
    {
        var w = new NfWriter();
        w.WriteUnsigned(3u);   // count
        w.WriteUnsigned(10u);
        w.WriteUnsigned(20u);
        w.WriteUnsigned(30u);
        var reader = new NativeReader(w.ToMemory());

        var parser = new NativeParser(reader, 0);
        parser.IsNull.Should().BeFalse();

        var count = parser.GetSequenceCount();
        count.Should().Be(3u);

        var values = new List<uint>();
        for (uint i = 0; i < count; i++)
            values.Add(parser.GetUnsigned());

        values.Should().Equal(10u, 20u, 30u);
    }

    [Fact]
    public void NativeParser_GetRelativeOffset_FollowsForwardDelta()
    {
        // At offset 0 we encode a signed delta of +4. The relative target is
        // the position of the delta (0) plus the delta value.
        var w = new NfWriter();
        w.WriteSigned(4);                       // forward delta from offset 0
        while (w.Length < 4) w.WriteByte(0xCC); // pad so target byte 4 is the value
        w.WriteUnsigned(99u);                   // payload at offset 4 (encoded as 0x...)
        var reader = new NativeReader(w.ToMemory());

        var parser = new NativeParser(reader, 0);
        var target = parser.GetRelativeOffset();

        target.Should().Be(4u);
        var payload = new NativeParser(reader, target);
        payload.GetUnsigned().Should().Be(99u);
    }

    [Fact]
    public void NativeParser_GetParserFromRelativeOffset_PositionsAtTarget()
    {
        var w = new NfWriter();
        w.WriteSigned(2);          // delta +2 from offset 0
        w.WriteByte(0x00);         // padding at offset 1 (encoded width of WriteSigned(2) is 1 byte, so offset moves to 1)
        w.WriteUnsigned(0x55u);    // payload at offset 2
        var reader = new NativeReader(w.ToMemory());

        var parser = new NativeParser(reader, 0);
        var child = parser.GetParserFromRelativeOffset();

        child.GetUnsigned().Should().Be(0x55u);
    }

    // -----------------------------------------------------------------------
    // Test-side encoder: faithful port of the runtime's NativePrimitiveEncoder
    // (src/coreclr/tools/Common/Internal/NativeFormat/NativeFormatWriter.cs).
    // -----------------------------------------------------------------------

    private sealed class NfWriter
    {
        private readonly List<byte> _bytes = new();

        public int Length => _bytes.Count;

        public void WriteByte(byte b) => _bytes.Add(b);

        public void WriteUInt32(uint d)
        {
            _bytes.Add((byte)d);
            _bytes.Add((byte)(d >> 8));
            _bytes.Add((byte)(d >> 16));
            _bytes.Add((byte)(d >> 24));
        }

        public void WriteUInt64(ulong d)
        {
            WriteUInt32((uint)d);
            WriteUInt32((uint)(d >> 32));
        }

        public void WriteUnsigned(uint d)
        {
            unchecked
            {
                if (d < 128)
                {
                    WriteByte((byte)(d * 2));
                }
                else if (d < 128 * 128)
                {
                    WriteByte((byte)(d * 4 + 1));
                    WriteByte((byte)(d >> 6));
                }
                else if (d < 128 * 128 * 128)
                {
                    WriteByte((byte)(d * 8 + 3));
                    WriteByte((byte)(d >> 5));
                    WriteByte((byte)(d >> 13));
                }
                else if (d < 128 * 128 * 128 * 128)
                {
                    WriteByte((byte)(d * 16 + 7));
                    WriteByte((byte)(d >> 4));
                    WriteByte((byte)(d >> 12));
                    WriteByte((byte)(d >> 20));
                }
                else
                {
                    WriteByte(15);
                    WriteUInt32(d);
                }
            }
        }

        public void WriteSigned(int i)
        {
            unchecked
            {
                uint d = (uint)i;
                if (d + 64u < 128u)
                {
                    WriteByte((byte)(d * 2));
                }
                else if (d + 64u * 128u < 128u * 128u)
                {
                    WriteByte((byte)(d * 4 + 1));
                    WriteByte((byte)(d >> 6));
                }
                else if (d + 64u * 128u * 128u < 128u * 128u * 128u)
                {
                    WriteByte((byte)(d * 8 + 3));
                    WriteByte((byte)(d >> 5));
                    WriteByte((byte)(d >> 13));
                }
                else if (d + 64u * 128u * 128u * 128u < 128u * 128u * 128u * 128u)
                {
                    WriteByte((byte)(d * 16 + 7));
                    WriteByte((byte)(d >> 4));
                    WriteByte((byte)(d >> 12));
                    WriteByte((byte)(d >> 20));
                }
                else
                {
                    WriteByte(15);
                    WriteUInt32(d);
                }
            }
        }

        public void WriteUnsignedLong(ulong d)
        {
            if ((uint)d == d)
            {
                WriteUnsigned((uint)d);
                return;
            }
            WriteByte(0x1F);
            WriteUInt64(d);
        }

        public void WriteSignedLong(long i)
        {
            if ((int)i == i)
            {
                WriteSigned((int)i);
                return;
            }
            WriteByte(0x1F);
            WriteUInt64((ulong)i);
        }

        public byte[] ToArray() => _bytes.ToArray();

        public ReadOnlyMemory<byte> ToMemory() => _bytes.ToArray().AsMemory();
    }
}
