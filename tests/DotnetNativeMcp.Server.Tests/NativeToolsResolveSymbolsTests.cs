using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

public class NativeToolsResolveSymbolsTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NativeImage CreateImage(
        string buildId,
        string fileName,
        params NativeSymbol[] symbols)
    {
        var handle = ImageHandle.From(buildId, fileName);
        var section = new NativeSection(".text", 0x1000, 0x100, 0, 0x100);
        return new NativeImage(handle, fileName, BinaryFormat.Elf, Architecture.X64, [section], symbols, new byte[0x100], 0);
    }

    private static NativeTools MakeTools(params NativeImage[] images) =>
        new NativeTools(new TestBinaryRegistry(images), new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

    // ---------------------------------------------------------------------------
    // Bad handle → top-level error
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_BadHandle_ReturnsTopLevelBinaryNotFound()
    {
        var tools = MakeTools();

        var result = tools.ResolveSymbols("unknown-handle", ["1010"]);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Empty addresses → success with empty list
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_EmptyAddressList_ReturnsEmptyResolutions()
    {
        var image = CreateImage("aabb", "empty.so");
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, []);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // N=1 single address — success path
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_SingleValidAddress_ResolvesSymbol()
    {
        var image = CreateImage(
            "aabb",
            "single.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", NativeAotSymbolDemangler.Demangle("S_P_MyApp_Program__Main"), 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1010"]);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().ContainSingle();
        var row = result.Data.Resolutions[0];
        row.MangledName.Should().Be("S_P_MyApp_Program__Main");
        row.Displacement.Should().Be(0);
        row.Error.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Mixed valid + invalid addresses → per-row errors, top-level success
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_MixedAddresses_PerRowOutcomesAndDisassembleHint()
    {
        var image = CreateImage(
            "aabb",
            "mixed.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", NativeAotSymbolDemangler.Demangle("S_P_MyApp_Program__Main"), 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1015", "not-hex", "0x1050"]);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().HaveCount(3);

        var rows = result.Data.Resolutions;
        rows[0].MangledName.Should().Be("S_P_MyApp_Program__Main");
        rows[0].Displacement.Should().Be(5);
        rows[0].Error.Should().BeNull();

        rows[1].Error.Should().NotBeNull();
        rows[1].Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);

        rows[2].Error.Should().NotBeNull();
        rows[2].Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);

        result.Hints.Should().ContainSingle();
        result.Hints[0].NextTool.Should().Be("disassemble");
    }

    // ---------------------------------------------------------------------------
    // Many addresses (N≥5)
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_ManyAddresses_AllResolved()
    {
        var symbols = Enumerable.Range(0, 5).Select(i =>
            new NativeSymbol(i, $"S_P_Method_{i}", $"Method_{i}", (ulong)(0x1010 + i * 0x20), 0x20, ".text", true))
            .ToArray();
        var image = CreateImage("ccdd", "many.so", symbols);
        var tools = MakeTools(image);

        var addresses = symbols.Select(s => $"0x{s.Rva:x}").ToList();
        var result = tools.ResolveSymbols(image.Handle.Value, addresses);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().HaveCount(5);
        result.Data.Resolutions.Should().AllSatisfy(row => row.Error.Should().BeNull());
        result.Data.Resolutions.Select(r => r.MangledName).Should()
            .Equal(symbols.Select(s => s.Name));
    }

    // ---------------------------------------------------------------------------
    // Hex with 0x prefix
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_HexAddressWithPrefix_Resolves()
    {
        var image = CreateImage(
            "eeff",
            "prefix.so",
            new NativeSymbol(0, "S_P_Prefix_Method", "Prefix_Method", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1010"]);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().ContainSingle();
        result.Data.Resolutions[0].MangledName.Should().Be("S_P_Prefix_Method");
    }

    // ---------------------------------------------------------------------------
    // Decimal address string
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_DecimalAddressString_Resolves()
    {
        // 0x1010 = decimal 4112
        var image = CreateImage(
            "eeff",
            "decimal.so",
            new NativeSymbol(0, "S_P_Decimal_Method", "Decimal_Method", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["4112"]); // decimal 4112 == 0x1010

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().ContainSingle();
        result.Data.Resolutions[0].MangledName.Should().Be("S_P_Decimal_Method");
    }

    // ---------------------------------------------------------------------------
    // Decimal and hex for the same address → same result
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_DecimalAndHexSameAddress_ProduceSameResult()
    {
        // 0x1234 = 4660 decimal
        var image = CreateImage(
            "1234",
            "sameaddr.so",
            new NativeSymbol(0, "S_P_Same_Method", "Same_Method", 0x1234, 0x20, ".text", true));
        var tools = MakeTools(image);

        var hexResult = tools.ResolveSymbols(image.Handle.Value, ["0x1234"]);
        var decResult = tools.ResolveSymbols(image.Handle.Value, ["4660"]); // 4660 decimal == 0x1234

        hexResult.IsError.Should().BeFalse();
        decResult.IsError.Should().BeFalse();
        hexResult.Data!.Resolutions[0].MangledName.Should()
            .Be(decResult.Data!.Resolutions[0].MangledName);
        hexResult.Data.Resolutions[0].ResolvedRvaHex.Should()
            .Be(decResult.Data.Resolutions[0].ResolvedRvaHex);
    }

    // ---------------------------------------------------------------------------
    // Address outside all sections → AddressOutOfRange per row
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_AddressOutsideAllSections_ReturnsAddressOutOfRange()
    {
        var image = CreateImage("aabb", "oob.so",
            new NativeSymbol(0, "S_P_SomeMethod", "SomeMethod", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["3000"]);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().ContainSingle();
        result.Data.Resolutions[0].Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    // ---------------------------------------------------------------------------
    // Address in section but no symbol → SymbolNotFound per row
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_AddressInSectionNoSymbol_ReturnsSymbolNotFound()
    {
        var image = CreateImage("aabb", "nosym.so",
            new NativeSymbol(0, "S_P_SomeMethod", "SomeMethod", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1050"]); // in .text but beyond symbol range

        result.IsError.Should().BeFalse();
        var row = result.Data!.Resolutions[0];
        row.Error!.Kind.Should().Be(ErrorKinds.SymbolNotFound);
        row.SectionName.Should().Be(".text");
    }

    // ---------------------------------------------------------------------------
    // Exceeding max count → top-level InvalidArgument
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_ExceedsMaxAddresses_ReturnsInvalidArgument()
    {
        var image = CreateImage("aabb", "max.so");
        var tools = MakeTools(image);
        var addresses = Enumerable.Range(0, 201).Select(i => i.ToString()).ToList();

        var result = tools.ResolveSymbols(image.Handle.Value, addresses);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Round-trip: RVAs from list_native_symbols resolve back to same names
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_RoundTripsListNativeSymbolsRvas()
    {
        var image = CreateImage(
            "eeff",
            "roundtrip.so",
            new NativeSymbol(0, "S_P_RoundTrip_First", NativeAotSymbolDemangler.Demangle("S_P_RoundTrip_First"), 0x1010, 0x10, ".text", true),
            new NativeSymbol(1, "S_P_RoundTrip_Second", NativeAotSymbolDemangler.Demangle("S_P_RoundTrip_Second"), 0x1030, 0x10, ".text", true));
        var tools = MakeTools(image);

        var listed = tools.ListNativeSymbols(image.Handle.Value, pageSize: 10);
        listed.IsError.Should().BeFalse();

        var addresses = listed.Data!.Symbols.Select(s => "0x" + s.RvaHex).ToList();
        var resolved = tools.ResolveSymbols(image.Handle.Value, addresses);

        resolved.IsError.Should().BeFalse();
        resolved.Data!.Resolutions.Select(r => r.MangledName).Should()
            .Equal(listed.Data.Symbols.Select(s => s.Name));
    }

    // ---------------------------------------------------------------------------
    // loadBase (ASLR rebasing) — the NativeFrame handoff path
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveSymbols_AslrAddressWithoutLoadBase_ReturnsAddressOutOfRange()
    {
        var image = CreateImage("aabb", "aslr.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", "MyApp.Program.Main", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        // Producer observed the PIE binary loaded at an ASLR base; the absolute VA is far past
        // every section once rebased against the on-disk base (0).
        var result = tools.ResolveSymbols(image.Handle.Value, ["0x7f0000001010"]);

        result.IsError.Should().BeFalse();
        result.Data!.Resolutions.Should().ContainSingle();
        result.Data.Resolutions[0].Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
    }

    [Fact]
    public void ResolveSymbols_AslrAddressWithLoadBase_ResolvesSymbol()
    {
        var image = CreateImage("aabb", "aslr.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", "MyApp.Program.Main", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(
            image.Handle.Value,
            ["0x7f0000001015"],
            loadBase: "0x7f0000000000");

        result.IsError.Should().BeFalse();
        var row = result.Data!.Resolutions.Should().ContainSingle().Subject;
        row.Error.Should().BeNull();
        row.MangledName.Should().Be("S_P_MyApp_Program__Main");
        row.Displacement.Should().Be(5);
        row.ResolvedRvaHex.Should().Be(0x1015UL.ToString("x16"));
    }

    [Fact]
    public void ResolveSymbols_LoadBaseAcceptsPrefixedAndBareHex()
    {
        var image = CreateImage("aabb", "aslr.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", "MyApp.Program.Main", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        const ulong loadBase = 0x7f0000000000UL;
        var address = (loadBase + 0x1010).ToString("x16");

        var prefixed = tools.ResolveSymbols(image.Handle.Value, [address], loadBase: "0x7f0000000000");
        var bare = tools.ResolveSymbols(image.Handle.Value, [address], loadBase: "7f0000000000");

        prefixed.IsError.Should().BeFalse();
        bare.IsError.Should().BeFalse();
        prefixed.Data!.Resolutions[0].MangledName.Should().Be("S_P_MyApp_Program__Main");
        bare.Data!.Resolutions[0].MangledName.Should().Be("S_P_MyApp_Program__Main");
        prefixed.Data.Resolutions[0].ResolvedRvaHex.Should().Be(bare.Data.Resolutions[0].ResolvedRvaHex);
    }

    [Fact]
    public void ResolveSymbols_AllDigitHexLoadBase_IsNotMisreadAsDecimal()
    {
        // 0x1000000 is all decimal digits ("16777216"); as bare hex it must be read as 0x1000000,
        // not as decimal. address = loadBase + 0x1010 must rebase to RVA 0x1010.
        const ulong loadBase = 0x1000000UL;
        var image = CreateImage("aabb", "digits.so",
            new NativeSymbol(0, "S_P_Digits_Method", "Digits.Method", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var address = "0x" + (loadBase + 0x1010).ToString("x");
        var result = tools.ResolveSymbols(image.Handle.Value, [address], loadBase: "1000000");

        result.IsError.Should().BeFalse();
        var row = result.Data!.Resolutions.Should().ContainSingle().Subject;
        row.Error.Should().BeNull();
        row.MangledName.Should().Be("S_P_Digits_Method");
        row.ResolvedRvaHex.Should().Be(0x1010UL.ToString("x16"));
    }

    [Fact]
    public void ResolveSymbols_AddressBelowLoadBase_ReturnsPerRowAddressOutOfRange()
    {
        var image = CreateImage("aabb", "aslr.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", "MyApp.Program.Main", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        // An address below the supplied loadBase cannot map into the image; it must NOT silently
        // resolve via the RVA fallback to the symbol at 0x1010.
        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1010"], loadBase: "0x7f0000000000");

        result.IsError.Should().BeFalse();
        var row = result.Data!.Resolutions.Should().ContainSingle().Subject;
        row.Error!.Kind.Should().Be(ErrorKinds.AddressOutOfRange);
        row.MangledName.Should().BeNull();
    }

    [Fact]
    public void ResolveSymbols_InvalidLoadBase_ReturnsTopLevelInvalidArgument_DoesNotThrow()
    {
        var image = CreateImage("aabb", "aslr.so",
            new NativeSymbol(0, "S_P_MyApp_Program__Main", "MyApp.Program.Main", 0x1010, 0x20, ".text", true));
        var tools = MakeTools(image);

        var result = tools.ResolveSymbols(image.Handle.Value, ["0x1010"], loadBase: "not-a-number");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Test registry
    // ---------------------------------------------------------------------------

    private sealed class TestBinaryRegistry(params NativeImage[] images) : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images =
            images.ToDictionary(image => image.Handle.Value, StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public DotnetNativeMcp.Core.NativeResult<string> RegisterHint(string path, string? buildId = null) => DotnetNativeMcp.Core.NativeResult.Ok("registered", path);

        public bool TryGet(string imageHandle, out NativeImage? image)
        {
            var found = _images.TryGetValue(imageHandle, out var resolved);
            image = resolved;
            return found;
        }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];
    }
}
