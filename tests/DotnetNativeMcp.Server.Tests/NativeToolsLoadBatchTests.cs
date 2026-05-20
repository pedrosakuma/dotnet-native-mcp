using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Identity;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Xref;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Server.Tools;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Server.Tests;

/// <summary>Tests for the batch/manifest mode of <c>load_native_binary</c>.</summary>
public class NativeToolsLoadBatchTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NativeTools MakeTools(BatchableTestRegistry? registry = null) =>
        new NativeTools(registry ?? new BatchableTestRegistry(), new NativeCallGraphCache(), new SourceResolver());

    // ---------------------------------------------------------------------------
    // Validation: both path + entries → invalid_argument
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadNativeBinary_BothPathAndEntries_ReturnsInvalidArgument()
    {
        var tools = MakeTools();
        var result = tools.LoadNativeBinary(path: "/some/path.so", entries: []);

        var err = result.Should().BeOfType<NativeResult<LoadNativeBinaryResult>>().Subject;
        err.IsError.Should().BeTrue();
        err.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        err.Error.Message.Should().Contain("both");
    }

    [Fact]
    public void LoadNativeBinary_NeitherPathNorEntries_ReturnsInvalidArgument()
    {
        var tools = MakeTools();
        var result = tools.LoadNativeBinary();

        var err = result.Should().BeOfType<NativeResult<LoadNativeBinaryResult>>().Subject;
        err.IsError.Should().BeTrue();
        err.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Empty batch → 0/0 summary
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadNativeBinary_EmptyEntries_ReturnsBatchResultWithZeroCount()
    {
        var tools = MakeTools();
        var result = tools.LoadNativeBinary(entries: []);

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();
        ok.Data!.Entries.Should().BeEmpty();
        ok.Data.LoadedCount.Should().Be(0);
        ok.Data.TotalCount.Should().Be(0);
        ok.Summary.Should().Contain("0 of 0");
    }

    // ---------------------------------------------------------------------------
    // Lazy mode (default) — all entries registered, no handle returned
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadNativeBinary_LazyBatch_AllEntriesRegisteredWithNullHandle()
    {
        var registry = new BatchableTestRegistry();
        var tools = MakeTools(registry);

        var result = tools.LoadNativeBinary(
            entries:
            [
                new BatchManifestEntry("/a/binary1.so", "Binary1"),
                new BatchManifestEntry("/a/binary2.so", null, "deadbeef"),
            ],
            mode: "lazy");

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();
        ok.Data!.TotalCount.Should().Be(2);
        ok.Data.LoadedCount.Should().Be(2);
        ok.Summary.Should().Contain("Registered 2 of 2");

        ok.Data.Entries.Should().AllSatisfy(e =>
        {
            e.Status.Should().Be("registered");
            e.BinaryHandle.Should().BeNull();
            e.Error.Should().BeNull();
        });

        registry.Hints.Should().ContainKey("/a/binary1.so");
        registry.Hints.Should().ContainKey("/a/binary2.so");
        registry.Hints["/a/binary2.so"].Should().Be("deadbeef");
    }

    [Fact]
    public void LoadNativeBinary_LazyBatch_EmptyPathEntryFails()
    {
        var tools = MakeTools();
        var result = tools.LoadNativeBinary(
            entries: [new BatchManifestEntry("  ")],
            mode: "lazy");

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();
        ok.Data!.TotalCount.Should().Be(1);
        ok.Data.LoadedCount.Should().Be(0);
        ok.Data.Entries[0].Status.Should().Be("failed");
        ok.Data.Entries[0].Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Eager mode — entries loaded; build-id mismatch → per-entry failure
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadNativeBinary_EagerBatch_SuccessfulEntry_ReturnsHandle()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/good.so", null,
            NativeResult.Ok("ok", MakeImage("aabb", "good.so")));

        var tools = MakeTools(registry);
        var result = tools.LoadNativeBinary(
            entries: [new BatchManifestEntry("/a/good.so", "Good")],
            mode: "eager");

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();
        ok.Data!.LoadedCount.Should().Be(1);
        ok.Data.Entries[0].Status.Should().Be("loaded");
        ok.Data.Entries[0].BinaryHandle.Should().NotBeNull();
        ok.Data.Entries[0].Error.Should().BeNull();
    }

    [Fact]
    public void LoadNativeBinary_EagerBatch_BuildIdMismatch_PerEntryFailure()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/binary.so", "wrong-id",
            NativeResult.Fail<NativeImage>(ErrorKinds.BinaryMismatch, "Build-id mismatch."));

        var tools = MakeTools(registry);
        var result = tools.LoadNativeBinary(
            entries: [new BatchManifestEntry("/a/binary.so", null, "wrong-id")],
            mode: "eager");

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();  // top-level is NOT an error
        ok.Data!.LoadedCount.Should().Be(0);
        ok.Data.Entries[0].Status.Should().Be("failed");
        ok.Data.Entries[0].Error!.Kind.Should().Be(ErrorKinds.BuildIdMismatch);
    }

    [Fact]
    public void LoadNativeBinary_EagerBatch_MixedValidAndInvalid_PerEntryOutcomes()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/good.so", null,
            NativeResult.Ok("ok", MakeImage("aabb", "good.so")));
        registry.AddLoadResult("/a/bad.so", null,
            NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, "Not found."));

        var tools = MakeTools(registry);
        var result = tools.LoadNativeBinary(
            entries:
            [
                new BatchManifestEntry("/a/good.so"),
                new BatchManifestEntry("/a/bad.so"),
            ],
            mode: "eager");

        var ok = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        ok.IsError.Should().BeFalse();
        ok.Data!.TotalCount.Should().Be(2);
        ok.Data.LoadedCount.Should().Be(1);
        ok.Summary.Should().Contain("Loaded 1 of 2");

        ok.Data.Entries[0].Status.Should().Be("loaded");
        ok.Data.Entries[1].Status.Should().Be("failed");
        ok.Data.Entries[1].Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Invalid mode
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadNativeBinary_InvalidMode_ReturnsInvalidArgument()
    {
        var tools = MakeTools();
        var result = tools.LoadNativeBinary(entries: [], mode: "invalid");

        var err = result.Should().BeOfType<NativeResult<BatchLoadData>>().Subject;
        err.IsError.Should().BeTrue();
        err.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NativeImage MakeImage(string buildId, string fileName) =>
        new NativeImage(
            ImageHandle.From(buildId, fileName),
            fileName,
            BinaryFormat.Elf,
            Architecture.X64,
            [new NativeSection(".text", 0x1000, 0x100, 0, 0x100)],
            [],
            new byte[0x100],
            0);

    /// <summary>
    /// Test registry that supports both <see cref="Load"/> (for eager-mode tests) and
    /// <see cref="RegisterHint"/> (for lazy-mode assertion).
    /// </summary>
    private sealed class BatchableTestRegistry : INativeBinaryRegistry
    {
        private readonly Dictionary<string, NativeResult<NativeImage>> _loadResults = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NativeImage> _byHandle = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> Hints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddLoadResult(string path, string? buildId, NativeResult<NativeImage> result)
        {
            var key = MakeKey(path, buildId);
            _loadResults[key] = result;
            if (!result.IsError)
                _byHandle[result.Data!.Handle.Value] = result.Data;
        }

        public NativeResult<NativeImage> Load(string path, string? expectedBuildId = null)
        {
            var key = MakeKey(path, expectedBuildId);
            if (_loadResults.TryGetValue(key, out var result))
                return result;
            // Fallback: try without buildId key
            key = MakeKey(path, null);
            if (_loadResults.TryGetValue(key, out result))
                return result;
            return NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, $"No staged result for '{path}'.");
        }

        public void RegisterHint(string path, string? buildId = null)
        {
            var absPath = Path.GetFullPath(path);
            Hints[absPath] = buildId;
        }

        public bool TryGet(string imageHandle, out NativeImage? image) =>
            _byHandle.TryGetValue(imageHandle, out image);

        public IReadOnlyList<NativeImage> List() => [.. _byHandle.Values];

        private static string MakeKey(string path, string? buildId) =>
            buildId is null ? path : $"{path}|{buildId}";
    }
}
