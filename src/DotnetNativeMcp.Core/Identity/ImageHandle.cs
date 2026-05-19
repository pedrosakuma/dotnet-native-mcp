using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace DotnetNativeMcp.Core.Identity;

/// <summary>
/// Opaque image identity handle in the format <c>i:&lt;buildIdHex&gt;:&lt;binaryNameHash&gt;</c>.
/// <list type="bullet">
///   <item><description><c>buildIdHex</c> — lowercase hex of ELF NT_GNU_BUILD_ID, or PE CodeView GUID+Age, or SHA-256 prefix of the file bytes.</description></item>
///   <item><description><c>binaryNameHash</c> — CRC32 hex of the file name (not path), for human disambiguation.</description></item>
/// </list>
/// </summary>
public sealed record ImageHandle
{
    private const string Prefix = "i:";

    /// <summary>Raw lowercase hex build-id.</summary>
    public string BuildIdHex { get; }

    /// <summary>CRC32 hex of the file name.</summary>
    public string NameHash { get; }

    /// <summary>Full opaque handle string.</summary>
    public string Value { get; }

    private ImageHandle(string buildIdHex, string nameHash)
    {
        BuildIdHex = buildIdHex;
        NameHash = nameHash;
        Value = $"{Prefix}{buildIdHex}:{nameHash}";
    }

    /// <summary>Creates an <see cref="ImageHandle"/> from pre-computed parts.</summary>
    public static ImageHandle From(string buildIdHex, string fileName)
    {
        var nameHash = ComputeNameHash(fileName);
        return new ImageHandle(buildIdHex, nameHash);
    }

    /// <summary>Parses a handle string. Returns <c>null</c> if the format is invalid.</summary>
    public static ImageHandle? TryParse(string? value)
    {
        if (value is null) return null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var rest = value[Prefix.Length..];
        var colonIdx = rest.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx <= 0) return null;
        var buildIdHex = rest[..colonIdx];
        var nameHash = rest[(colonIdx + 1)..];
        if (nameHash.Length == 0) return null;
        return new ImageHandle(buildIdHex, nameHash);
    }

    /// <summary>Parses a handle string, throwing on failure.</summary>
    public static ImageHandle Parse(string value) =>
        TryParse(value) ?? throw new FormatException($"Invalid ImageHandle format: '{value}'");

    private static string ComputeNameHash(string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(System.IO.Path.GetFileName(fileName));
        var crc = new Crc32();
        crc.Append(bytes);
        Span<byte> hash = stackalloc byte[4];
        crc.GetCurrentHash(hash);
        // CRC32 returns little-endian; read as uint for consistent rendering.
        var value = BinaryPrimitives.ReadUInt32LittleEndian(hash);
        return value.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
