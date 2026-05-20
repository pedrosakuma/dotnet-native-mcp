using System.IO.Compression;
using System.Reflection.PortableExecutable;

namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Extracts an embedded portable PDB from a managed PE binary that was published with
/// <c>&lt;DebugType&gt;embedded&lt;/DebugType&gt;</c>.
///
/// <para>
/// The embedded PDB format stores an 8-byte header (magic <c>0x4244504D</c>,
/// then 4-byte uncompressed size) followed by a raw
/// <see cref="DeflateStream"/> payload. The version fields (MajorVersion/MinorVersion)
/// live in the debug directory entry itself. After decompression the result is a
/// standard Portable PDB stream (BSJB magic at offset 0).
/// </para>
///
/// <para>
/// NativeAOT ELF binaries do not embed PDB data in a known section — the investigation
/// found no <c>__pdb__</c> or <c>.note.debug-pdb</c> section in the ILC output. This
/// path is a documented no-op pending evidence of such a section.
/// </para>
/// </summary>
public static class EmbeddedPdbExtractor
{
    // Embedded PDB magic: 'MPDB' as a little-endian uint32.
    private const uint EmbeddedPdbMagic = 0x4244504D;
    // BSJB portable PDB magic.
    private const uint PortablePdbMagic = 0x424A5342;
    // Header size: 4 (magic) + 4 (uncompressed size). Version fields are in the
    // debug directory entry itself, not in the data payload.
    private const int EmbeddedPdbHeaderSize = 8;
    // Maximum decompressed size guard (32 MiB).
    private const int MaxDecompressedSize = 32 * 1024 * 1024;

    /// <summary>
    /// Attempts to extract an embedded portable PDB from the given PE file bytes.
    /// Returns the raw decompressed PDB bytes (BSJB magic at offset 0) on success,
    /// or <c>null</c> when no embedded PDB is found or the input is not a valid managed PE.
    /// </summary>
    public static byte[]? TryExtractFromPe(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length < 2) return null;
        var span = bytes.Span;
        // Must start with MZ.
        if (span[0] != 0x4D || span[1] != 0x5A) return null;

        try
        {
            using var ms = new MemoryStream(bytes.ToArray(), writable: false);
            using var pe = new PEReader(ms, PEStreamOptions.Default);

            var debugEntries = pe.ReadDebugDirectory();
            foreach (var entry in debugEntries)
            {
                if (entry.Type != DebugDirectoryEntryType.EmbeddedPortablePdb) continue;
                if (entry.DataSize < EmbeddedPdbHeaderSize) continue;

                var dataPointer = entry.DataPointer;
                if (dataPointer < 0 || dataPointer + entry.DataSize > bytes.Length) continue;

                var raw = bytes.Slice(dataPointer, entry.DataSize).Span;
                var magic = ReadU32(raw, 0);
                if (magic != EmbeddedPdbMagic) continue;

                var uncompressedSize = ReadI32(raw, 4);
                if (uncompressedSize <= 0 || uncompressedSize > MaxDecompressedSize) continue;

                var compressedSize = entry.DataSize - EmbeddedPdbHeaderSize;
                if (compressedSize <= 0) continue;

                var compressed = raw.Slice(EmbeddedPdbHeaderSize, compressedSize).ToArray();
                var pdbBytes = Decompress(compressed, uncompressedSize);
                if (pdbBytes is null) continue;

                // Validate: must be a portable PDB.
                if (pdbBytes.Length < 4 || ReadU32(pdbBytes.AsSpan(), 0) != PortablePdbMagic) continue;
                return pdbBytes;
            }
        }
        catch
        {
            // Silently return null on any parse error.
        }

        return null;
    }

    private static byte[]? Decompress(byte[] compressed, int uncompressedSize)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var output = new byte[uncompressedSize];
            int total = 0;
            while (total < uncompressedSize)
            {
                int read = deflate.Read(output, total, uncompressedSize - total);
                if (read == 0) return null;
                total += read;
            }
            return output;
        }
        catch
        {
            return null;
        }
    }

    private static uint ReadU32(ReadOnlySpan<byte> d, int o) =>
        (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

    private static int ReadI32(ReadOnlySpan<byte> d, int o) =>
        d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
}
