using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Mstat;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class MstatReaderTests
{
    [Fact]
    public void Read_SampleAotMstat_ReturnsDecodedAttributions()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        result.Data.Should().NotBeNull();
        result.Data!.Attributions.Should().NotBeEmpty();
        result.Data.MethodCount.Should().BeGreaterThan(0);
        result.Data.TypeCount.Should().BeGreaterThan(0);
        result.Data.Attributions.Should().Contain(a => a.MethodName == "Main" || a.MethodName == "<Main>$");
    }

    [Fact]
    public void Read_SampleAotMstat_ReportsFormatVersion()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        result.Data!.FormatVersion.Should().MatchRegex(@"^\d+\.\d+$");
    }

    [Fact]
    public void Read_SampleAotMstat_DecodesBlobCatchAllTable()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        var data = result.Data!;

        // The Blobs catch-all table holds every non-method/non-type native node and is the bulk of
        // what the old methods+types-only reader missed.
        data.Attributions.Should().Contain(a => a.Source == MstatCategory.Blob);

        var methodAndTypeOnly = data.Attributions
            .Where(a => a.Source is MstatCategory.Method or MstatCategory.Type)
            .Sum(a => (long)a.TotalSize);

        // Full accounting must exceed a code+EEType-only total — otherwise the breakdown undercounts.
        data.TotalSize.Should().BeGreaterThan(methodAndTypeOnly);
    }

    [Fact]
    public void Read_SampleAotMstat_ResolvesMangledSymbolNames()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        result.Data!.Attributions
            .Should().Contain(a => a.Source == MstatCategory.Method && !string.IsNullOrEmpty(a.SymbolName));
    }

    [Fact]
    public void Read_SampleAotMstat_TotalEqualsMethodsTypesBlobs_NoDoubleCount()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        var data = result.Data!;

        // In MSTAT 2.x the RvaFields/FrozenObjects/ManifestResources tables duplicate bytes already
        // present in Blobs; the authoritative non-overlapping partition is Methods + Types + Blobs.
        var partition = data.Attributions
            .Where(a => a.Source is MstatCategory.Method or MstatCategory.Type or MstatCategory.Blob)
            .Sum(a => (long)a.TotalSize);

        data.TotalSize.Should().Be(partition);
        data.Attributions.Should().OnlyContain(a =>
            a.Source == MstatCategory.Method || a.Source == MstatCategory.Type || a.Source == MstatCategory.Blob);
    }

    [Fact]
    public void Read_SampleAotMstat_ReportsCategoryTotals()
    {
        var mstatPath = FixturePaths.SampleAotMstat;
        if (mstatPath is null || !File.Exists(mstatPath))
            return;

        var result = MstatReader.Read(mstatPath);

        result.IsError.Should().BeFalse();
        var data = result.Data!;

        data.CategoryTotals.Should().NotBeEmpty();
        data.CategoryTotals.Should().Contain(c => c.Category == MstatCategory.Method);
        data.CategoryTotals.Sum(c => c.TotalSize).Should().Be(data.TotalSize);

        // Category totals are ordered largest-first.
        data.CategoryTotals.Should().BeInDescendingOrder(c => c.TotalSize);
    }

    [Fact]
    public void Aggregate_GroupByCategory_SumsAcrossSources()
    {
        var rows = new[]
        {
            new MstatAttribution("App", "MyApp", "MyApp.Program", "Main", 10, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Program", "Run", 4, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Program", null, 5, MstatCategory.Type),
            new MstatAttribution("(native)", "", "DehydratedData", null, 100, MstatCategory.Blob),
        };

        var result = MstatReader.Aggregate(rows, MstatGroupBy.Category, 25);

        result.Should().HaveCount(3);
        result[0].Key.Should().Be(MstatCategory.Blob);
        result[0].TotalSize.Should().Be(100);
        result.Should().Contain(r => r.Key == MstatCategory.Method && r.TotalSize == 14 && r.AttributionCount == 2);
        result.Should().Contain(r => r.Key == MstatCategory.Type && r.TotalSize == 5);
    }

    [Fact]
    public void Read_MissingPath_ReturnsMstatNotFound()
    {
        var result = MstatReader.Read("/no/such/file.mstat");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MstatNotFound);
    }

    [Fact]
    public void Read_CorruptFilePresent_ReturnsMstatInvalid()
    {
        var scratchDir = Path.Combine(
            Path.GetDirectoryName(typeof(MstatReaderTests).Assembly.Location)!,
            "scratch");
        Directory.CreateDirectory(scratchDir);
        var corruptPath = Path.Combine(scratchDir, $"{Guid.NewGuid():N}-corrupt.mstat");
        File.WriteAllBytes(corruptPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03,
                                         0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);
        try
        {
            var result = MstatReader.Read(corruptPath);

            result.IsError.Should().BeTrue();
            result.Error!.Kind.Should().Be(ErrorKinds.MstatInvalid);
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    [Fact]
    public void Aggregate_GroupByAssembly_CombinesMethodAndTypeRows()
    {
        var rows = new[]
        {
            new MstatAttribution("App", "MyApp", "MyApp.Program", "Main", 10, "method"),
            new MstatAttribution("App", "MyApp", "MyApp.Program", null, 5, "type"),
            new MstatAttribution("Lib", "MyLib", "MyLib.Helper", "Run", 9, "method"),
        };

        var result = MstatReader.Aggregate(rows, MstatGroupBy.Assembly, 25);

        result.Should().HaveCount(2);
        result[0].AssemblyName.Should().Be("App");
        result[0].TotalSize.Should().Be(15);
        result[1].AssemblyName.Should().Be("Lib");
        result[1].TotalSize.Should().Be(9);
    }

    [Fact]
    public void Aggregate_GroupByMethod_ExcludesTypeOnlyRows_AndCapsTopN()
    {
        var rows = Enumerable.Range(0, 600)
            .Select(i => new MstatAttribution("App", "MyApp", $"MyApp.Type{i}", $"Method{i}", 600 - i, "method"))
            .Append(new MstatAttribution("App", "MyApp", "MyApp.Type0", null, 999, "type"))
            .ToArray();

        var result = MstatReader.Aggregate(rows, MstatGroupBy.Method, 9999);

        result.Should().HaveCount(MstatReader.MaxTopN);
        result.Should().OnlyContain(row => row.MethodName != null);
        result[0].MethodName.Should().Be("Method0");
        result[0].TotalSize.Should().Be(600);
    }

    private static MstatDocument Doc(params MstatAttribution[] attributions) => new(
        "/tmp/synthetic.mstat",
        attributions,
        attributions.Count(a => a.Source == MstatCategory.Method),
        attributions.Count(a => a.Source == MstatCategory.Type),
        attributions.Sum(a => (long)a.TotalSize),
        "2.2",
        Array.Empty<MstatCategoryTotal>(),
        0);

    [Fact]
    public void Diff_GroupByType_ClassifiesGrewShrankAddedRemoved()
    {
        var baseline = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Grows", "M", 100, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Shrinks", "M", 200, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Gone", "M", 50, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Stable", "M", 30, MstatCategory.Method));
        var current = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.Grows", "M", 175, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Shrinks", "M", 120, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.New", "M", 90, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.Stable", "M", 30, MstatCategory.Method));

        var diff = MstatReader.Diff(baseline, current, MstatGroupBy.Type, 25);

        diff.GroupBy.Should().Be("type");
        diff.BaselineTotalSize.Should().Be(380);
        diff.CurrentTotalSize.Should().Be(415);
        diff.TotalSizeDelta.Should().Be(35);

        diff.AddedBucketCount.Should().Be(1);    // MyApp.New
        diff.RemovedBucketCount.Should().Be(1);  // MyApp.Gone
        diff.ChangedBucketCount.Should().Be(2);  // Grows + Shrinks (Stable unchanged)

        // Largest growth first; added bucket counts as growth from zero.
        diff.TopGrew.Should().HaveCount(2);
        diff.TopGrew[0].TypeName.Should().Be("MyApp.New");
        diff.TopGrew[0].SizeDelta.Should().Be(90);
        diff.TopGrew[0].BaselineSize.Should().Be(0);
        diff.TopGrew[1].TypeName.Should().Be("MyApp.Grows");
        diff.TopGrew[1].SizeDelta.Should().Be(75);

        // Most negative first; removed bucket counts as a shrink to zero.
        diff.TopShrank.Should().HaveCount(2);
        diff.TopShrank[0].TypeName.Should().Be("MyApp.Shrinks");
        diff.TopShrank[0].SizeDelta.Should().Be(-80);
        diff.TopShrank[1].TypeName.Should().Be("MyApp.Gone");
        diff.TopShrank[1].SizeDelta.Should().Be(-50);
        diff.TopShrank[1].CurrentSize.Should().Be(0);
    }

    [Fact]
    public void Diff_SameDocument_ReportsNoDeltas()
    {
        var doc = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.A", "M", 100, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.A", null, 40, MstatCategory.Type),
            new MstatAttribution("(native)", "", "Metadata", null, 500, MstatCategory.Blob));

        var diff = MstatReader.Diff(doc, doc, MstatGroupBy.Category, 25);

        diff.TotalSizeDelta.Should().Be(0);
        diff.AddedBucketCount.Should().Be(0);
        diff.RemovedBucketCount.Should().Be(0);
        diff.ChangedBucketCount.Should().Be(0);
        diff.TopGrew.Should().BeEmpty();
        diff.TopShrank.Should().BeEmpty();
    }

    [Fact]
    public void Diff_GroupByCategory_AggregatesAcrossSources()
    {
        var baseline = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.A", "M", 100, MstatCategory.Method),
            new MstatAttribution("(native)", "", "Metadata", null, 500, MstatCategory.Blob));
        var current = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.A", "M", 140, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.B", "M", 60, MstatCategory.Method),
            new MstatAttribution("(native)", "", "Metadata", null, 450, MstatCategory.Blob));

        var diff = MstatReader.Diff(baseline, current, MstatGroupBy.Category, 25);

        diff.TopGrew.Should().ContainSingle(r => r.Key == MstatCategory.Method && r.SizeDelta == 100);
        diff.TopShrank.Should().ContainSingle(r => r.Key == MstatCategory.Blob && r.SizeDelta == -50);
    }

    [Fact]
    public void Diff_GroupByMethod_ExcludesTypeOnlyBuckets()
    {
        var baseline = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.A", "M", 100, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.A", null, 999, MstatCategory.Type));
        var current = Doc(
            new MstatAttribution("App", "MyApp", "MyApp.A", "M", 130, MstatCategory.Method),
            new MstatAttribution("App", "MyApp", "MyApp.A", null, 1, MstatCategory.Type));

        var diff = MstatReader.Diff(baseline, current, MstatGroupBy.Method, 25);

        // Type-only rows must not appear as method buckets even though their size changed.
        diff.TopGrew.Should().ContainSingle();
        diff.TopGrew[0].MethodName.Should().Be("M");
        diff.TopGrew[0].SizeDelta.Should().Be(30);
        diff.TopShrank.Should().BeEmpty();
    }
}
