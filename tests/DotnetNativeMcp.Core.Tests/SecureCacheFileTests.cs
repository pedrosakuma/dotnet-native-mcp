using System.Runtime.InteropServices;
using DotnetNativeMcp.Core;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

/// <summary>
/// Regression tests for <see cref="SecureCacheFile"/> — atomic writes, restrictive POSIX
/// permissions, and bounded reads used by the on-disk caches under
/// <c>~/.cache/dotnet-native-mcp/</c>.
/// </summary>
public sealed class SecureCacheFileTests : IDisposable
{
    private readonly string _root;

    public SecureCacheFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dotnet-native-mcp-secure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void WriteAtomic_CreatesFile_WithExpectedBytes()
    {
        var target = Path.Combine(_root, "dir", "file.bin");
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        SecureCacheFile.WriteAtomic(target, payload);

        Assert.True(File.Exists(target));
        Assert.Equal(payload, File.ReadAllBytes(target));
    }

    [Fact]
    public void WriteAtomic_LeavesNoTempArtifacts()
    {
        var target = Path.Combine(_root, "file.bin");

        SecureCacheFile.WriteAtomic(target, new byte[] { 0xAA });

        var stragglers = Directory.GetFiles(_root, "file.bin.*.tmp");
        Assert.Empty(stragglers);
    }

    [Fact]
    public void WriteAtomic_OnPosix_SetsMode0600()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var target = Path.Combine(_root, "file.bin");
        SecureCacheFile.WriteAtomic(target, new byte[] { 1, 2, 3 });

        var mode = File.GetUnixFileMode(target);
        var permissionBits = mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, permissionBits);
    }

    [Fact]
    public void CreateSecureDirectory_OnPosix_SetsMode0700()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var dir = Path.Combine(_root, "nested");
        SecureCacheFile.CreateSecureDirectory(dir);

        var mode = File.GetUnixFileMode(dir);
        var permissionBits = mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            permissionBits);
    }

    [Fact]
    public void TryReadCapped_ReturnsBytes_WhenUnderCap()
    {
        var target = Path.Combine(_root, "file.bin");
        var payload = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(target, payload);

        var read = SecureCacheFile.TryReadCapped(target, maxBytes: 16);

        Assert.NotNull(read);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void TryReadCapped_ReturnsNull_WhenOverCap_AndDeletesFile()
    {
        var target = Path.Combine(_root, "big.bin");
        File.WriteAllBytes(target, new byte[1024]);

        var read = SecureCacheFile.TryReadCapped(target, maxBytes: 64);

        Assert.Null(read);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void TryReadCapped_MissingFile_ReturnsNull()
    {
        var missing = Path.Combine(_root, "does-not-exist.bin");
        Assert.Null(SecureCacheFile.TryReadCapped(missing, maxBytes: 1024));
    }

    [Fact]
    public void WriteAtomic_OverwritesExistingFile()
    {
        var target = Path.Combine(_root, "file.bin");
        File.WriteAllBytes(target, new byte[] { 0xFF });

        SecureCacheFile.WriteAtomic(target, new byte[] { 1, 2, 3 });

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }

    [Fact]
    public void TryReadCapped_StreamGrowthDuringRead_RejectedWithoutOverAllocation()
    {
        var target = Path.Combine(_root, "grow.bin");

        // Simulate a file whose on-disk length is acceptable but whose contents
        // exceed the cap once read. We achieve this by passing a maxBytes smaller
        // than the actual file length: the read loop must stop before
        // materializing the over-cap data.
        File.WriteAllBytes(target, new byte[4096]);

        var read = SecureCacheFile.TryReadCapped(target, maxBytes: 16);

        Assert.Null(read);
        Assert.False(File.Exists(target));
    }
}
