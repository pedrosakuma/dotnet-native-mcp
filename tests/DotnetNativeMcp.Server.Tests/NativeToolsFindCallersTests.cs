using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class NativeToolsFindCallersTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal x64 image with a single .text section.
    /// The <paramref name="code"/> is placed at RVA 0 (file offset 0).
    /// Symbols can be injected to test name-based lookup.
    /// </summary>
    private static NativeImage CreateImage(
        byte[] code,
        ulong imageBase = 0x400000,
        Architecture arch = Architecture.X64,
        params NativeSymbol[] symbols)
    {
        var handle = ImageHandle.From("testfc", "test.so");
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        return new NativeImage(handle, "test.so", BinaryFormat.Elf, arch,
            [section], symbols, new ReadOnlyMemory<byte>(code), imageBase);
    }

    private static NativeTools MakeTools(params NativeImage[] images)
    {
        var cache = new NativeCallGraphCache();
        return new NativeTools(new TestBinaryRegistry(images), cache, new SourceResolver());
    }

    // ---------------------------------------------------------------------------
    // Bad handle → binary_not_found
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_BadHandle_ReturnsBinaryNotFound()
    {
        var tools = MakeTools();

        var result = tools.FindNativeCallers("bad-handle", "main");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Empty target → invalid_argument
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_EmptyTarget_ReturnsInvalidArgument()
    {
        var image = CreateImage([0x90]);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "   ");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // ARM64 image with BL → succeeds and finds caller
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_Arm64ImageWithBL_ReturnsCallers()
    {
        // ARM64: BL +4 (94000001) at VA 0x400000, target = 0x400004 (within section).
        // Use a unique handle so the disk xref cache does not collide with x64 tests.
        var handle = ImageHandle.From("testfc-arm64bl", "arm64bl.elf");
        var code = new byte[] { 0x01, 0x00, 0x00, 0x94, 0x1F, 0x20, 0x03, 0xD5 };
        var section = new NativeSection(".text", 0, (ulong)code.Length, 0, (ulong)code.Length);
        var image = new NativeImage(handle, "arm64bl.elf", BinaryFormat.Elf, Architecture.Arm64,
            [section], [], new ReadOnlyMemory<byte>(code), 0x400000);

        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "0x400004");

        result.IsError.Should().BeFalse();
        result.Data!.Callers.Should().HaveCount(1);
        result.Data.Callers[0].Mnemonic.Should().Be("bl");
    }

    // ---------------------------------------------------------------------------
    // Unknown symbol name → symbol_not_found
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_UnknownSymbolName_ReturnsSymbolNotFound()
    {
        var image = CreateImage([0x90, 0xC3]);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "no_such_symbol");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
    }

    // ---------------------------------------------------------------------------
    // Address outside all sections → address_out_of_range
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_AddressOutOfRange_ReturnsAddressOutOfRange()
    {
        var image = CreateImage([0x90, 0xC3]);
        var tools = MakeTools(image);

        // Address 0x1 is an RVA that maps outside the 2-byte section.
        var result = tools.FindNativeCallers(image.Handle.Value, "0x1");

        // The section has VirtualSize=2, so RVA 1 is still inside (byte 0 and 1).
        // Use an RVA outside the section (e.g. RVA 10 >> imageBase 0x400000).
        var result2 = tools.FindNativeCallers(image.Handle.Value, "0xDEAD");

        result2.IsError.Should().BeTrue();
        result2.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ---------------------------------------------------------------------------
    // No callers found → success with empty list
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_NoCaller_ReturnsEmptyCallerList()
    {
        // NOP + RET only — no branch instructions; the target at RVA 0 has no callers.
        var code = new byte[] { 0x90, 0xC3 };
        var sym = new NativeSymbol(0, "my_func", "my_func", 0, 2, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "my_func");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(0);
        result.Data.Callers.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Happy path: CALL targeting a known symbol
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_BySymbolName_ReturnsCallerSite()
    {
        // Layout (imageBase = 0x400000):
        //   offset 0: E8 05 00 00 00  → CALL 0x40000A  (caller)
        //   offset 5: 90              → NOP
        //   offset 6: 90 90 90 90     → 4× NOP padding
        //   offset 10 (0x40000A):  C3 → RET  ← target function
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,  // CALL 0x40000A
            0x90, 0x90, 0x90, 0x90, 0x90,  // 5 NOPs
            0xC3,                          // RET (target)
        };

        // Symbol "my_target" at RVA 10 (= VA 0x40000A)
        var sym = new NativeSymbol(0, "my_target", "my_target", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "my_target");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.TargetSymbol.Should().Be("my_target");
        result.Data.TargetAddressHex.Should().Be("000000000040000a");

        var site = result.Data.Callers[0];
        site.Mnemonic.Should().Be("call");
        site.RawBytes.Should().Be("e805000000");
        site.SourceAddressHex.Should().Be("0000000000400000");
    }

    // ---------------------------------------------------------------------------
    // Happy path: address-based lookup (hex with 0x prefix)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ByHexAddress_ReturnsCallerSite()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,  // CALL 0x40000A
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var image = CreateImage(code);
        var tools = MakeTools(image);

        // Target by VA
        var result = tools.FindNativeCallers(image.Handle.Value, "0x40000a");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.Callers[0].Mnemonic.Should().Be("call");
    }

    // ---------------------------------------------------------------------------
    // Happy path: address-based lookup (decimal)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ByDecimalAddress_ReturnsCallerSite()
    {
        // imageBase = 0x400000 = 4194304
        // target RVA = 10, target VA = 4194304 + 10 = 4194314
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var image = CreateImage(code);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "4194314");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // Hints: happy path should include disassemble hint
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_WithCallers_ProvidesDisassembleHint()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "tgt");

        result.IsError.Should().BeFalse();
        result.Hints.Should().ContainSingle();
        result.Hints[0].NextTool.Should().Be("disassemble");
    }

    // ---------------------------------------------------------------------------
    // resolveSource=true → Source field is null only when no PDB (expected in unit tests)
    // resolveSource=false → Source field is always null on every CallSite
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_ResolveSourceTrue_SourceIsNullWhenNoPdb()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        // resolveSource=true (default) — no PDB present, so Source is null but no error.
        var result = tools.FindNativeCallers(image.Handle.Value, "tgt", resolveSource: true);

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        // Source will be null because no PDB is available in the test image.
        result.Data.Callers[0].Source.Should().BeNull();
    }

    [Fact]
    public void FindNativeCallers_ResolveSourceFalse_SourceIsNullOnEveryCallSite()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result = tools.FindNativeCallers(image.Handle.Value, "tgt", resolveSource: false);

        result.IsError.Should().BeFalse();
        result.Data!.TotalCallers.Should().Be(1);
        result.Data.Callers.Should().OnlyContain(s => s.Source == null);
    }

    // ---------------------------------------------------------------------------
    // Cache: second call returns cached result (no re-scan)
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_SecondCall_ReturnsCachedResult()
    {
        var code = new byte[]
        {
            0xE8, 0x05, 0x00, 0x00, 0x00,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xC3,
        };
        var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
        var image = CreateImage(code, symbols: sym);
        var tools = MakeTools(image);

        var result1 = tools.FindNativeCallers(image.Handle.Value, "tgt");
        var result2 = tools.FindNativeCallers(image.Handle.Value, "tgt");

        result1.IsError.Should().BeFalse();
        result2.IsError.Should().BeFalse();
        result1.Data!.TotalCallers.Should().Be(result2.Data!.TotalCallers);
    }


    // ---------------------------------------------------------------------------
    // Disk cache: hit/miss coverage
    // ---------------------------------------------------------------------------

    [Fact]
    public void FindNativeCallers_DiskCacheHit_ReturnsSameResult()
    {
        // Arrange: isolated cache directory via XDG_CACHE_HOME.
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "dotnet-native-mcp-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        var prevXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var prevDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", cacheDir);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", null);

            var code = new byte[]
            {
                0xE8, 0x05, 0x00, 0x00, 0x00,
                0x90, 0x90, 0x90, 0x90, 0x90,
                0xC3,
            };
            var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
            var image = CreateImage(code, symbols: sym);

            // First call -- cache miss --> builds and persists.
            var tools1 = MakeTools(image);
            var result1 = tools1.FindNativeCallers(image.Handle.Value, "tgt");

            // Second call with a *fresh* in-memory cache -- must hit disk.
            var tools2 = MakeTools(image);
            var result2 = tools2.FindNativeCallers(image.Handle.Value, "tgt");

            result1.IsError.Should().BeFalse();
            result2.IsError.Should().BeFalse();
            result1.Data!.TotalCallers.Should().Be(result2.Data!.TotalCallers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", prevXdg);
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", prevDisable);
            try { Directory.Delete(cacheDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void FindNativeCallers_DiskCacheDisabled_SkipsDisk()
    {
        var prevDisable = Environment.GetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", "0");

            var code = new byte[]
            {
                0xE8, 0x05, 0x00, 0x00, 0x00,
                0x90, 0x90, 0x90, 0x90, 0x90,
                0xC3,
            };
            var sym = new NativeSymbol(0, "tgt", "tgt", 10, 1, ".text", true);
            var image = CreateImage(code, symbols: sym);
            var tools = MakeTools(image);

            // Should succeed even when cache is disabled.
            var result = tools.FindNativeCallers(image.Handle.Value, "tgt");

            result.IsError.Should().BeFalse();
            result.Data!.TotalCallers.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_NATIVE_MCP_XREF_CACHE", prevDisable);
        }
    }

    // ---------------------------------------------------------------------------
    // Test registry
    // ---------------------------------------------------------------------------

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(img => img.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public void RegisterHint(string path, string? buildId = null) { }

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
