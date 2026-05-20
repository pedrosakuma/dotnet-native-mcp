using System.Text;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Strings;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class StringExtractorTests
{
    [Fact]
    public void Extract_FindsAsciiStringBetweenNonPrintables()
    {
        byte[] bytes = [0x00, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x01];

        var results = StringExtractor.Extract(bytes, 0x2000, ".rodata", 5, ascii: true, utf16: false).ToList();

        results.Should().ContainSingle();
        results[0].SectionName.Should().Be(".rodata");
        results[0].RvaHex.Should().Be("0000000000002001");
        results[0].Encoding.Should().Be("ascii");
        results[0].Length.Should().Be(5);
        results[0].Value.Should().Be("hello");
    }

    [Fact]
    public void Extract_FindsUtf16LeString()
    {
        var bytes = new byte[] { 0xFF }
            .Concat(Encoding.Unicode.GetBytes("hello"))
            .Concat(new byte[] { 0xFF })
            .ToArray();

        var results = StringExtractor.Extract(bytes, 0x3000, ".rdata", 5, ascii: false, utf16: true).ToList();

        results.Should().ContainSingle();
        results[0].RvaHex.Should().Be("0000000000003001");
        results[0].Encoding.Should().Be("utf16le");
        results[0].Length.Should().Be(5);
        results[0].Value.Should().Be("hello");
    }

    [Fact]
    public void Extract_RespectsMinLengthBoundary()
    {
        byte[] bytes = [(byte)'t', (byte)'e', (byte)'s', (byte)'t'];

        var included = StringExtractor.Extract(bytes, 0x4000, ".rodata", 4, ascii: true, utf16: false).ToList();
        var excluded = StringExtractor.Extract(bytes, 0x4000, ".rodata", 5, ascii: true, utf16: false).ToList();

        included.Should().ContainSingle();
        excluded.Should().BeEmpty();
    }

    [Fact]
    public void Extract_FindsMixedAsciiAndUtf16Strings()
    {
        var bytes = new byte[] { 0x00 }
            .Concat(Encoding.ASCII.GetBytes("alpha"))
            .Concat(new byte[] { 0x00, 0xFF })
            .Concat(Encoding.Unicode.GetBytes("beta"))
            .Concat(new byte[] { 0xFF })
            .ToArray();

        var results = StringExtractor.Extract(bytes, 0x5000, ".rodata", 4, ascii: true, utf16: true).ToList();

        results.Should().ContainSingle(result => result.Encoding == "ascii" && result.Value == "alpha");
        results.Should().ContainSingle(result => result.Encoding == "utf16le" && result.Value == "beta");
    }

    [Fact]
    public void ExtractStrings_MissingHandle_ReturnsBinaryNotFound()
    {
        var tools = new NativeTools(new FakeRegistry(), new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());

        var result = tools.ExtractStrings("missing");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    [Theory]
    [InlineData(0, 200, "ascii")]
    [InlineData(4097, 200, "ascii")]
    [InlineData(1, 0, "ascii")]
    [InlineData(1, 5001, "ascii")]
    [InlineData(1, 200, "bogus")]
    public void ExtractStrings_InvalidArguments_ReturnInvalidArgument(int minLength, int pageSize, string encodings)
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage((".rodata", 0x1000UL, Encoding.ASCII.GetBytes("alpha\0beta"))));
        var tools = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var handle = registry.List().Single().Handle.Value;

        var result = tools.ExtractStrings(handle, minLength: minLength, encodings: encodings, pageSize: pageSize);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    [Fact]
    public void ExtractStrings_Pagination_ReturnsNextCursorAndHint()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage((".rodata", 0x1000UL, Encoding.ASCII.GetBytes("alpha\0bravo\0charlie\0"))));
        var tools = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var handle = registry.List().Single().Handle.Value;

        var result = tools.ExtractStrings(handle, minLength: 5, encodings: "ascii", pageSize: 2);

        result.IsError.Should().BeFalse();
        result.Data!.Strings.Select(row => row.Value).Should().Equal("alpha", "bravo");
        result.Data.TotalCount.Should().Be(3);
        result.Data.NextCursor.Should().Be(2);
        result.Hints.Should().ContainSingle(hint => hint.NextTool == "extract_strings");
        result.Hints[0].SuggestedArguments!["cursor"].Should().Be(2);
    }

    [Fact]
    public void ExtractStrings_RespectsSectionFilter()
    {
        var registry = new FakeRegistry();
        registry.Add(CreateImage(
            (".rodata", 0x1000UL, Encoding.ASCII.GetBytes("alpha\0")),
            (".data", 0x2000UL, Encoding.ASCII.GetBytes("omega\0"))));
        var tools = new NativeTools(registry, new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var handle = registry.List().Single().Handle.Value;

        var result = tools.ExtractStrings(handle, minLength: 5, encodings: "ascii", section: ".data");

        result.IsError.Should().BeFalse();
        result.Data!.Strings.Should().ContainSingle();
        result.Data.Strings[0].SectionName.Should().Be(".data");
        result.Data.Strings[0].Value.Should().Be("omega");
    }

    [Fact]
    public void ExtractStrings_SampleAot_ReturnsUtf16HiLiteral()
    {
        if (FixturePaths.SampleAot is not { } fixturePath || !File.Exists(fixturePath))
            return;

        var tools = new NativeTools(new NativeBinaryRegistry(), new DotnetNativeMcp.Core.Xref.NativeCallGraphCache(), new SourceResolver());
        var loadResult = (NativeResult<LoadNativeBinaryResult>)tools.LoadNativeBinary(path: fixturePath);
        loadResult.IsError.Should().BeFalse();

        var result = tools.ExtractStrings(
            loadResult.Data!.ImageHandle,
            minLength: 2,
            encodings: "utf16le",
            section: ".rodata",
            pageSize: 5000);

        result.IsError.Should().BeFalse();
        result.Data!.Strings.Should().Contain(row => row.Encoding == "utf16le" && row.Value == "hi");
    }

    private static NativeImage CreateImage(params (string Name, ulong VirtualAddress, byte[] Bytes)[] sections)
    {
        List<NativeSection> nativeSections = [];
        List<byte> rawBytes = [];
        ulong fileOffset = 0;

        foreach (var (name, virtualAddress, bytes) in sections)
        {
            rawBytes.AddRange(bytes);
            nativeSections.Add(new NativeSection(name, virtualAddress, (ulong)bytes.Length, fileOffset, (ulong)bytes.Length));
            fileOffset += (ulong)bytes.Length;
        }

        return new NativeImage(
            ImageHandle.From("deadbeef", "synthetic.bin"),
            "/synthetic.bin",
            BinaryFormat.Elf,
            Architecture.X64,
            nativeSections,
            [],
            rawBytes.ToArray(),
            0);
    }

    private sealed class FakeRegistry : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeImage> _images = new(StringComparer.OrdinalIgnoreCase);

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null) =>
            throw new NotSupportedException();

        public bool TryGet(string imageHandle, out NativeImage? image) =>
            _images.TryGetValue(imageHandle, out image);

        public void RegisterHint(string path, string? buildId = null) { }

        public IReadOnlyList<NativeImage> List() => [.. _images.Values];

        public void Add(NativeImage image) => _images[image.Handle.Value] = image;
    }
}
