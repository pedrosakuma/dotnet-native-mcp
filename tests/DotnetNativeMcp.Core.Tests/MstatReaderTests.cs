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
    public void Read_MissingPath_ReturnsMstatNotFound()
    {
        var result = MstatReader.Read("/no/such/file.mstat");

        result.IsError.Should().BeTrue();
        result.Error!.Kind.Should().Be(ErrorKinds.MstatNotFound);
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
