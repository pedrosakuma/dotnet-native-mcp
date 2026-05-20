using DotnetNativeMcp.Core.Symbols;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Tests for <see cref="EmbeddedPdbDiskCache"/> — disk cache for extracted embedded PDB bytes.
/// </summary>
public sealed class EmbeddedPdbDiskCacheTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string? _originalEnvXdg;
    private readonly string? _originalEnvDisable;

    public EmbeddedPdbDiskCacheTests()
    {
        _originalEnvXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        _originalEnvDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE");

        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "dotnet-native-mcp-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_cacheDir);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _cacheDir);
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _originalEnvXdg);
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", _originalEnvDisable);
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
    }

    // -------------------------------------------------------------------------
    // IsEnabled
    // -------------------------------------------------------------------------

    [Fact]
    public void IsEnabled_WhenEnvVarIsZero_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", "0");
        Assert.False(EmbeddedPdbDiskCache.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEnvVarIsUnset_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", null);
        Assert.True(EmbeddedPdbDiskCache.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEnvVarIsOne_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", "1");
        Assert.True(EmbeddedPdbDiskCache.IsEnabled);
    }

    // -------------------------------------------------------------------------
    // GetCachePath
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCachePath_HonorsXdgCacheHome()
    {
        var xdg = "/some/custom/xdg";
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", xdg);
        var path = EmbeddedPdbDiskCache.GetCachePath("abc123");
        Assert.StartsWith(xdg, path, StringComparison.Ordinal);
        Assert.Contains("dotnet-native-mcp", path, StringComparison.Ordinal);
        Assert.EndsWith("abc123.pdb", path, StringComparison.Ordinal);
    }

    [Fact]
    public void GetCachePath_WithoutXdgCacheHome_UsesHomeDir()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", null);
        var path = EmbeddedPdbDiskCache.GetCachePath("abc123");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(home, path, StringComparison.Ordinal);
        Assert.Contains(".cache", path, StringComparison.Ordinal);
        Assert.Contains("dotnet-native-mcp", path, StringComparison.Ordinal);
        Assert.EndsWith("abc123.pdb", path, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // TryRead — missing file
    // -------------------------------------------------------------------------

    [Fact]
    public void TryRead_MissingFile_ReturnsNull()
    {
        var path = EmbeddedPdbDiskCache.GetCachePath("nonexistent999");
        Assert.Null(EmbeddedPdbDiskCache.TryRead(path));
    }

    // -------------------------------------------------------------------------
    // TryRead — invalid content (not a PDB)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryRead_NonPdbContent_ReturnsNull()
    {
        var path = EmbeddedPdbDiskCache.GetCachePath("badcontent");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04, 0x05]);
        Assert.Null(EmbeddedPdbDiskCache.TryRead(path));
    }

    // -------------------------------------------------------------------------
    // Write → TryRead round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteAndRead_ValidPdbBytes_RoundTrips()
    {
        // Synthesize a minimal fake "PDB" with BSJB magic.
        var pdbBytes = MakeMinimalPdbBytes();
        var path = EmbeddedPdbDiskCache.GetCachePath("roundtrip123");

        EmbeddedPdbDiskCache.Write(path, pdbBytes);
        var read = EmbeddedPdbDiskCache.TryRead(path);

        Assert.NotNull(read);
        Assert.Equal(pdbBytes, read);
    }

    // -------------------------------------------------------------------------
    // Cache file is created on write
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_CreatesFileOnDisk()
    {
        var pdbBytes = MakeMinimalPdbBytes();
        var path = EmbeddedPdbDiskCache.GetCachePath("fileexists456");

        Assert.False(File.Exists(path));
        EmbeddedPdbDiskCache.Write(path, pdbBytes);
        Assert.True(File.Exists(path));
    }

    // -------------------------------------------------------------------------
    // Disable knob: DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE=0 → IsEnabled false
    // -------------------------------------------------------------------------

    [Fact]
    public void IsEnabled_Disabled_MeansCallerShouldSkipCache()
    {
        // When disabled, callers are expected to not call TryRead/Write.
        // Verify the flag is observable.
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", "0");
        Assert.False(EmbeddedPdbDiskCache.IsEnabled);

        // Re-enable.
        Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_PDB_EXTRACT_CACHE", null);
        Assert.True(EmbeddedPdbDiskCache.IsEnabled);
    }

    // -------------------------------------------------------------------------
    // Concurrent writes do not crash
    // -------------------------------------------------------------------------

    [Fact]
    public void Write_ConcurrentWriters_DoNotCrash()
    {
        var pdbBytes = MakeMinimalPdbBytes();
        var path = EmbeddedPdbDiskCache.GetCachePath("concurrent_pdb");
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            try { EmbeddedPdbDiskCache.Write(path, pdbBytes); }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(10));

        Assert.Empty(exceptions);
        // File should be readable afterwards.
        Assert.NotNull(EmbeddedPdbDiskCache.TryRead(path));
    }

    // -------------------------------------------------------------------------
    // End-to-end: extract from fixture, cache to disk, re-read without re-parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void EndToEnd_ExtractAndCache_EmbeddedPdbFixture()
    {
        var dllPath = FixturePaths.EmbeddedPdbDll;
        if (dllPath is null)
            return; // fixture not built — skip

        var buildId = "testbuildid_" + Guid.NewGuid().ToString("N");
        var cachePath = EmbeddedPdbDiskCache.GetCachePath(buildId);

        // Should not be cached yet.
        Assert.Null(EmbeddedPdbDiskCache.TryRead(cachePath));

        // Extract from binary.
        var bytes = File.ReadAllBytes(dllPath);
        var pdbBytes = EmbeddedPdbExtractor.TryExtractFromPe(new ReadOnlyMemory<byte>(bytes));
        Assert.NotNull(pdbBytes);

        // Write to cache.
        EmbeddedPdbDiskCache.Write(cachePath, pdbBytes);
        Assert.True(File.Exists(cachePath));

        // Re-read from cache — must return the same bytes.
        var cached = EmbeddedPdbDiskCache.TryRead(cachePath);
        Assert.NotNull(cached);
        Assert.Equal(pdbBytes, cached);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal byte array that starts with BSJB magic (the portable PDB
    /// header signature), so <see cref="EmbeddedPdbDiskCache.TryRead"/> accepts it.
    /// </summary>
    private static byte[] MakeMinimalPdbBytes()
    {
        // BSJB magic = 0x424A5342 LE
        var bytes = new byte[16];
        bytes[0] = 0x42; bytes[1] = 0x53; bytes[2] = 0x4A; bytes[3] = 0x42;
        return bytes;
    }
}
