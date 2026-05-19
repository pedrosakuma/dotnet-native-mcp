using DotnetNativeMcp.Core;
using FluentAssertions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void Scaffold_notice_is_present()
    {
        NativeImageLoader.ScaffoldNotice.Should().Contain("scaffold phase");
    }

    [Fact]
    public void Get_size_breakdown_returns_deterministic_top_n_for_methods()
    {
        var fixture = CreateSampleFixture(withMstat: true);

        var loaded = NativeImageLoader.LoadNativeBinary(fixture.BinaryPath);
        loaded.Ok.Should().BeTrue();
        loaded.Value.Should().NotBeNull();

        var result = MstatSizeAnalyzer.GetSizeBreakdown(loaded.Value!, SizeBreakdownGroupBy.Method, topN: 2);
        result.Ok.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Items.Select(x => x.Name).Should().Equal("Sample.Widget::Grow", "Sample.Widget::Run");
        result.Value.Items.Select(x => x.Bytes).Should().Equal(10, 10);
    }

    [Fact]
    public void Get_size_breakdown_returns_mstat_not_found_without_sidecar()
    {
        var fixture = CreateSampleFixture(withMstat: false);
        var loaded = NativeImageLoader.LoadNativeBinary(fixture.BinaryPath);
        loaded.Ok.Should().BeTrue();
        loaded.Value.Should().NotBeNull();

        var result = MstatSizeAnalyzer.GetSizeBreakdown(loaded.Value!, SizeBreakdownGroupBy.Assembly, topN: 25);
        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Kind.Should().Be(NativeErrorKind.MstatNotFound);
    }

    private static SampleFixture CreateSampleFixture(bool withMstat)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotnet-native-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var binaryPath = Path.Combine(root, "SampleAot");
        File.WriteAllBytes(binaryPath, [0x7F, (byte)'E', (byte)'L', (byte)'F']);

        if (withMstat)
        {
            var mstatPath = $"{binaryPath}.mstat";
            WriteTestMstat(mstatPath);
        }

        return new SampleFixture(binaryPath);
    }

    private static void WriteTestMstat(string outputPath)
    {
        var metadata = new MetadataBuilder();
        var ilBuilder = new BlobBuilder();
        var methodBodies = new MethodBodyStreamEncoder(ilBuilder);
        var sig = BuildVoidMethodSignature(metadata);
        var firstParameterHandle = MetadataTokens.ParameterHandle(1);

        var methodsBodyOffset = methodBodies.AddMethodBody(CreateMethodsBody(), 8, default, MethodBodyAttributes.None);
        var typesBodyOffset = methodBodies.AddMethodBody(CreateTypesBody(), 8, default, MethodBodyAttributes.None);
        var growBodyOffset = methodBodies.AddMethodBody(CreateRetBody(), 8, default, MethodBodyAttributes.None);
        var runBodyOffset = methodBodies.AddMethodBody(CreateRetBody(), 8, default, MethodBodyAttributes.None);

        var methodsHandle = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Methods"),
            sig,
            methodsBodyOffset,
            firstParameterHandle);

        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Types"),
            sig,
            typesBodyOffset,
            firstParameterHandle);

        var growHandle = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Grow"),
            sig,
            growBodyOffset,
            firstParameterHandle);

        _ = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            sig,
            runBodyOffset,
            firstParameterHandle);

        metadata.AddModule(
            0,
            metadata.GetOrAddString("SampleAot.mstat"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        metadata.AddAssembly(
            metadata.GetOrAddString("SampleAot"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString(string.Empty),
            metadata.GetOrAddString("<Module>"),
            MetadataTokens.EntityHandle(0),
            MetadataTokens.FieldDefinitionHandle(1),
            methodsHandle);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Widget"),
            MetadataTokens.EntityHandle(0),
            MetadataTokens.FieldDefinitionHandle(1),
            growHandle);

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            ilBuilder,
            flags: CorFlags.ILOnly);

        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);
        File.WriteAllBytes(outputPath, peBlob.ToArray());
    }

    private static BlobHandle BuildVoidMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(0, returnType => returnType.Void(), _ => { });
        return metadata.GetOrAddBlob(signature);
    }

    private static InstructionEncoder CreateMethodsBody()
    {
        var body = new BlobBuilder();
        var il = new InstructionEncoder(body);

        il.OpCode(ILOpCode.Ldtoken);
        il.Token(MetadataTokens.MethodDefinitionHandle(3));
        il.LoadConstantI4(10);
        il.LoadConstantI4(0);
        il.LoadConstantI4(0);
        il.LoadConstantI4(0);

        il.OpCode(ILOpCode.Ldtoken);
        il.Token(MetadataTokens.MethodDefinitionHandle(4));
        il.LoadConstantI4(10);
        il.LoadConstantI4(0);
        il.LoadConstantI4(0);
        il.LoadConstantI4(0);

        il.OpCode(ILOpCode.Ret);
        return il;
    }

    private static InstructionEncoder CreateTypesBody()
    {
        var body = new BlobBuilder();
        var il = new InstructionEncoder(body);
        il.OpCode(ILOpCode.Ldtoken);
        il.Token(MetadataTokens.TypeDefinitionHandle(2));
        il.LoadConstantI4(20);
        il.LoadConstantI4(0);
        il.OpCode(ILOpCode.Ret);
        return il;
    }

    private static InstructionEncoder CreateRetBody()
    {
        var body = new BlobBuilder();
        var il = new InstructionEncoder(body);
        il.OpCode(ILOpCode.Ret);
        return il;
    }

    private sealed record SampleFixture(string BinaryPath);
}
