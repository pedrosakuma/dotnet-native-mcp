using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class NativeCallGraphDiskCacheTests : IDisposable
{
    // Each test gets its own isolated directory so tests don't interfere.
    private readonly string _cacheDir;
    private readonly string? _originalEnvXdg;
    private readonly string? _originalEnvDisable;

    public NativeCallGraphDiskCacheTests()
    {
        _originalEnvXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        _originalEnvDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");

        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "dotnet-native-mcp-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_cacheDir);

        // Point the cache to our isolated test directory via XDG_CACHE_HOME.
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _cacheDir);
        // Make sure caching is enabled by default.
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _originalEnvXdg);
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", _originalEnvDisable);

        try { Directory.Delete(_cacheDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------

    private static Dictionary<ulong, IReadOnlyList<CallSite>> MakeSampleIndex()
    {
        var cs1 = new CallSite("0000000000400010", "caller_a", "caller_a_demangled", "call", "0x400020", "e810000000");
        var cs2 = new CallSite("0000000000400050", null, null, "jmp", "0x400020", "eb01");
        return new Dictionary<ulong, IReadOnlyList<CallSite>>
        {
            [0x400020UL] = [cs1, cs2],
            [0x400080UL] = [new CallSite("0000000000400080", "caller_b", null, "call", "0x400080", "e801000000")],
        };
    }

    // ---------------------------------------------------------------------------
    // Round-trip: write then read back
    // ---------------------------------------------------------------------------

    [Fact]
    public void WriteAndRead_RoundTrips_IndexEquality()
    {
        var original = MakeSampleIndex();
        var path = NativeCallGraphDiskCache.GetCachePath("deadbeefcafe");

        NativeCallGraphDiskCache.Write(path, original);
        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeTrue();
        read.Should().NotBeNull();
        read!.Keys.Should().BeEquivalentTo(original.Keys);
        foreach (var key in original.Keys)
        {
            read[key].Should().BeEquivalentTo(original[key]);
        }
    }

    // ---------------------------------------------------------------------------
    // Wrong magic -> cache miss, no exception
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryRead_WrongMagic_ReturnsFalse()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("badbad01");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write a header with a wrong magic prefix.
        var garbage = new byte[64];
        garbage[0] = (byte)'X';
        garbage[1] = (byte)'Y';
        garbage[2] = (byte)'Z';
        garbage[3] = (byte)'W';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(garbage.AsSpan(4), 2);
        File.WriteAllBytes(path, garbage);

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse();
        read.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Old NXR2 magic -> cache miss (format is incompatible)
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryRead_OldNxr2Magic_ReturnsFalse()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("badbad04");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write NXR2 (old format) — must be rejected to avoid silently reading incompatible data.
        var header = new byte[8];
        header[0] = (byte)'N';
        header[1] = (byte)'X';
        header[2] = (byte)'R';
        header[3] = (byte)'2'; // old magic, not NXR3
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 2);
        File.WriteAllBytes(path, header);

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse("NXR2 format must be rejected after magic bump to NXR3");
        read.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Wrong version -> cache miss, no exception
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryRead_WrongVersion_ReturnsFalse()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("badbad02");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Correct NXR3 magic but unknown version 99.
        var header = new byte[8];
        header[0] = (byte)'N';
        header[1] = (byte)'X';
        header[2] = (byte)'R';
        header[3] = (byte)'3'; // correct NXR3 magic
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 99);
        File.WriteAllBytes(path, header);

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse();
        read.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Corrupted JSON body -> cache miss, no exception
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryRead_CorruptedBody_ReturnsFalse()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("badbad03");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var header = new byte[8];
        header[0] = (byte)'N';
        header[1] = (byte)'X';
        header[2] = (byte)'R';
        header[3] = (byte)'3'; // correct NXR3 magic
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 3);
        var body = "{this is not valid json!!!"u8.ToArray();
        File.WriteAllBytes(path, [.. header, .. body]);

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse();
        read.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // File does not exist -> cache miss, no exception
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("nonexistent");

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse();
        read.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Concurrent writers don't crash
    // ---------------------------------------------------------------------------

    [Fact]
    public void Write_ConcurrentWriters_DoNotCrash()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("concurrent");
        var index = MakeSampleIndex();

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            try { NativeCallGraphDiskCache.Write(path, index); }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(10));

        exceptions.Should().BeEmpty();

        // The file should be readable after concurrent writes.
        var found = NativeCallGraphDiskCache.TryRead(path, out var read);
        found.Should().BeTrue();
        read.Should().NotBeNull();
    }

    // ---------------------------------------------------------------------------
    // DOTNET_NATIVE_MCP_XREF_CACHE=0 -> IsEnabled returns false
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsEnabled_WhenEnvVarIsZero_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "0");
        NativeCallGraphDiskCache.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WhenEnvVarIsUnset_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", null);
        NativeCallGraphDiskCache.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WhenEnvVarIsOne_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "1");
        NativeCallGraphDiskCache.IsEnabled.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // GetCachePath honors XDG_CACHE_HOME
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetCachePath_HonorsXdgCacheHome()
    {
        var xdg = "/some/custom/xdg";
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", xdg);

        var path = NativeCallGraphDiskCache.GetCachePath("abc123");

        path.Should().StartWith(xdg);
        path.Should().Contain("dotnet-native-mcp");
        path.Should().EndWith("abc123.xref");
    }

    [Fact]
    public void GetCachePath_WithoutXdgCacheHome_UsesHomeDir()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", null);

        var path = NativeCallGraphDiskCache.GetCachePath("abc123");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        path.Should().StartWith(home);
        path.Should().Contain(".cache");
        path.Should().Contain("dotnet-native-mcp");
        path.Should().EndWith("abc123.xref");
    }

    // ---------------------------------------------------------------------------
    // Cross-refs round-trip
    // ---------------------------------------------------------------------------

    [Fact]
    public void WriteAndRead_WithCrossRefs_RoundTrips()
    {
        var original = MakeSampleIndex();
        var crossRef = new CrossImageCallSite(
            "0000000000401010", "caller_x", null, "call", "0x400020", "e810000000",
            "cafebabe", "/lib/caller.so");
        var crossRefs = new Dictionary<string, IReadOnlyList<CrossImageCallSite>>
        {
            ["cafebabe::lib_func"] = [crossRef],
        };

        var path = NativeCallGraphDiskCache.GetCachePath("crossreftest");

        NativeCallGraphDiskCache.Write(path, original, crossRefs);
        var found = NativeCallGraphDiskCache.TryRead(path, out var readIndex, out var readCrossRefs);

        found.Should().BeTrue();
        readIndex.Should().NotBeNull();
        readCrossRefs.Should().NotBeNull();
        readCrossRefs!.Should().ContainKey("cafebabe::lib_func");

        var sites = readCrossRefs!["cafebabe::lib_func"];
        sites.Should().ContainSingle();
        sites[0].CallerImageBuildId.Should().Be("cafebabe");
        sites[0].CallerImagePath.Should().Be("/lib/caller.so");
        sites[0].SourceAddressHex.Should().Be("0000000000401010");
    }

    [Fact]
    public void WriteAndRead_WithNullCrossRefs_RoundTripsWithoutCrossRefs()
    {
        var original = MakeSampleIndex();
        var path = NativeCallGraphDiskCache.GetCachePath("nocrossref");

        NativeCallGraphDiskCache.Write(path, original, null);
        var found = NativeCallGraphDiskCache.TryRead(path, out var readIndex, out var readCrossRefs);

        found.Should().BeTrue();
        readIndex.Should().NotBeNull();
        readCrossRefs.Should().BeNull();
    }

    [Fact]
    public void MakeCrossRefKey_IncludesAllComponents()
    {
        var key = NativeCallGraphDiskCache.MakeCrossRefKey("deadbeef", "liblib.so", "lib_func");
        key.Should().Be("deadbeef:liblib.so:lib_func");
    }

    [Fact]
    public void MakeCrossRefKey_NullLibrary_UsesEmptyString()
    {
        var key = NativeCallGraphDiskCache.MakeCrossRefKey("deadbeef", null, "lib_func");
        key.Should().Be("deadbeef::lib_func");
    }

    [Fact]
    public void WriteAndRead_WithMachOMetadata_RoundTrips()
    {
        var original = MakeSampleIndex();
        var machO = new MachOCrossImageMetadata(
            new Dictionary<ulong, string> { [0x2000UL] = "foo" },
            new Dictionary<string, ulong>(StringComparer.Ordinal) { ["foo"] = 0x40UL });
        var path = NativeCallGraphDiskCache.GetCachePath("macho-metadata");

        NativeCallGraphDiskCache.Write(path, original, null, machO);
        var found = NativeCallGraphDiskCache.TryRead(path, out var readIndex, out var readCrossRefs, out var readMachO);

        found.Should().BeTrue();
        readIndex.Should().NotBeNull();
        readCrossRefs.Should().BeNull();
        readMachO.Should().NotBeNull();
        readMachO!.StubTargets.Should().ContainKey(0x2000UL);
        readMachO.StubTargets[0x2000UL].Should().Be("foo");
        readMachO.Exports.Should().ContainKey("foo");
        readMachO.Exports["foo"].Should().Be(0x40UL);
    }

    [Fact]
    public void TryRead_OversizedFile_TreatedAsCacheMissAndDeleted()
    {
        var path = NativeCallGraphDiskCache.GetCachePath("oversize-cache");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var fs = File.Create(path))
        {
            fs.WriteByte((byte)'N');
            fs.WriteByte((byte)'X');
            fs.WriteByte((byte)'R');
            fs.WriteByte((byte)'3');
            fs.SetLength(ResourceLimits.MaxXrefCacheBytes + 1);
        }

        var found = NativeCallGraphDiskCache.TryRead(path, out var read);

        found.Should().BeFalse();
        read.Should().BeNull();
        File.Exists(path).Should().BeFalse();
    }
}
