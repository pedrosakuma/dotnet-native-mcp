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

/// <summary>Tests for <c>import_native_manifest</c>.</summary>
public class NativeToolsImportManifestTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NativeTools MakeTools(BatchableTestRegistry? registry = null) =>
        new NativeTools(registry ?? new BatchableTestRegistry(), new NativeCallGraphCache(), new SourceResolver());

    // ---------------------------------------------------------------------------
    // Empty batch → 0/0 summary
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImportNativeManifest_EmptyEntries_ReturnsResultWithZeroCount()
    {
        var tools = MakeTools();
        var result = tools.ImportNativeManifest([]);

        result.IsError.Should().BeFalse();
        result.Data!.Entries.Should().BeEmpty();
        result.Data.LoadedCount.Should().Be(0);
        result.Data.TotalCount.Should().Be(0);
        result.Summary.Should().Contain("0 of 0");
    }

    [Fact]
    public void ImportNativeManifest_TooManyEntries_ReturnsInvalidArgument()
    {
        var tools = MakeTools();
        var entries = Enumerable.Range(0, ResourceLimits.MaxManifestEntries + 1)
            .Select(index => new BatchManifestEntry($"/a/binary-{index}.so"))
            .ToArray();

        var result = tools.ImportNativeManifest(entries);

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
        result.Error.Message.Should().Contain(ResourceLimits.MaxManifestEntries.ToString());
    }

    // ---------------------------------------------------------------------------
    // Lazy mode (default) — all entries registered, no handle returned
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImportNativeManifest_LazyBatch_AllEntriesRegisteredWithNullHandle()
    {
        var registry = new BatchableTestRegistry();
        var tools = MakeTools(registry);

        var result = tools.ImportNativeManifest(
            [
                new BatchManifestEntry("/a/binary1.so", "Binary1"),
                new BatchManifestEntry("/a/binary2.so", null, "deadbeef"),
            ],
            mode: "lazy");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.LoadedCount.Should().Be(2);
        result.Summary.Should().Contain("Registered 2 of 2");

        result.Data.Entries.Should().AllSatisfy(e =>
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
    public void ImportNativeManifest_LazyBatch_EmptyPathEntryFails()
    {
        var tools = MakeTools();
        var result = tools.ImportNativeManifest(
            [new BatchManifestEntry("  ")],
            mode: "lazy");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.LoadedCount.Should().Be(0);
        result.Data.Entries[0].Status.Should().Be("failed");
        result.Data.Entries[0].Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
    }

    // ---------------------------------------------------------------------------
    // Eager mode — entries loaded; build-id mismatch → per-entry failure
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImportNativeManifest_EagerBatch_SuccessfulEntry_ReturnsHandle()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/good.so", null,
            NativeResult.Ok("ok", MakeImage("aabb", "good.so")));

        var tools = MakeTools(registry);
        var result = tools.ImportNativeManifest(
            [new BatchManifestEntry("/a/good.so", "Good")],
            mode: "eager");

        result.IsError.Should().BeFalse();
        result.Data!.LoadedCount.Should().Be(1);
        result.Data.Entries[0].Status.Should().Be("loaded");
        result.Data.Entries[0].BinaryHandle.Should().NotBeNull();
        result.Data.Entries[0].Error.Should().BeNull();
    }

    [Fact]
    public void ImportNativeManifest_EagerBatch_BuildIdMismatch_PerEntryFailure()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/binary.so", "wrong-id",
            NativeResult.Fail<NativeImage>(ErrorKinds.BinaryMismatch, "Build-id mismatch."));

        var tools = MakeTools(registry);
        var result = tools.ImportNativeManifest(
            [new BatchManifestEntry("/a/binary.so", null, "wrong-id")],
            mode: "eager");

        result.IsError.Should().BeFalse();  // top-level is NOT an error
        result.Data!.LoadedCount.Should().Be(0);
        result.Data.Entries[0].Status.Should().Be("failed");
        result.Data.Entries[0].Error!.Kind.Should().Be(ErrorKinds.BuildIdMismatch);
    }

    [Fact]
    public void ImportNativeManifest_EagerBatch_MixedValidAndInvalid_PerEntryOutcomes()
    {
        var registry = new BatchableTestRegistry();
        registry.AddLoadResult("/a/good.so", null,
            NativeResult.Ok("ok", MakeImage("aabb", "good.so")));
        registry.AddLoadResult("/a/bad.so", null,
            NativeResult.Fail<NativeImage>(ErrorKinds.BinaryNotFound, "Not found."));

        var tools = MakeTools(registry);
        var result = tools.ImportNativeManifest(
            [
                new BatchManifestEntry("/a/good.so"),
                new BatchManifestEntry("/a/bad.so"),
            ],
            mode: "eager");

        result.IsError.Should().BeFalse();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.LoadedCount.Should().Be(1);
        result.Summary.Should().Contain("Loaded 1 of 2");

        result.Data.Entries[0].Status.Should().Be("loaded");
        result.Data.Entries[1].Status.Should().Be("failed");
        result.Data.Entries[1].Error!.Kind.Should().Be(ErrorKinds.BinaryNotFound);
    }

    // ---------------------------------------------------------------------------
    // Invalid mode
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImportNativeManifest_InvalidMode_ReturnsInvalidArgument()
    {
        var tools = MakeTools();
        var result = tools.ImportNativeManifest([], mode: "invalid");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.InvalidArgument);
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

        public NativeResult<string> RegisterHint(string path, string? buildId = null)
        {
            var absPath = Path.GetFullPath(path);
            Hints[absPath] = buildId;
            return NativeResult.Ok("registered", absPath);
        }

        public bool TryGet(string imageHandle, out NativeImage? image) =>
            _byHandle.TryGetValue(imageHandle, out image);

        public IReadOnlyList<NativeImage> List() => [.. _byHandle.Values];

        private static string MakeKey(string path, string? buildId) =>
            buildId is null ? path : $"{path}|{buildId}";
    }
}
