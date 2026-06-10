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
}
