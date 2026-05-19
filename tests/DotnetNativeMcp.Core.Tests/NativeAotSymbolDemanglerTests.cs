using DotnetNativeMcp.Core.Symbols;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public class NativeAotSymbolDemanglerTests
{
    [Fact]
    public void Demangle_NullOrEmpty_ReturnsInput()
    {
        NativeAotSymbolDemangler.Demangle(null).Should().BeEmpty();
        NativeAotSymbolDemangler.Demangle("").Should().BeEmpty();
    }

    [Fact]
    public void Demangle_NativeSymbol_PassThrough()
    {
        NativeAotSymbolDemangler.Demangle("__libc_start_main").Should().Be("__libc_start_main");
        NativeAotSymbolDemangler.Demangle("CryptoNative_BioRead").Should().Be("CryptoNative_BioRead");
        NativeAotSymbolDemangler.Demangle("realloc").Should().Be("realloc");
        NativeAotSymbolDemangler.Demangle("[unknown]").Should().Be("[unknown]");
    }

    [Fact]
    public void Demangle_SystemPrivateCoreLib_PrefixIsExpanded()
    {
        var result = NativeAotSymbolDemangler.Demangle("S_P_CoreLib_System_String__Equals");
        result.Should().StartWith("System.Private.CoreLib.");
        result.Should().Contain("System.String");
        result.Should().EndWith(".Equals");
    }

    [Fact]
    public void Demangle_AssemblyNamespaceTypeMethod_BoundaryRestored()
    {
        var result = NativeAotSymbolDemangler.Demangle(
            "Microsoft_AspNetCore_Http_Microsoft_AspNetCore_Http_HeaderDictionary__get_ContentLength");
        result.Should().EndWith(".get_ContentLength");
        result.Should().Contain("HeaderDictionary");
        result.Should().NotContain("__");
    }

    [Fact]
    public void Demangle_GenericArity_RenderedAsBacktick()
    {
        var result = NativeAotSymbolDemangler.Demangle(
            "System_Collections_Generic_Dictionary_2_Enumerator__MoveNext");
        result.Should().Contain("Dictionary`2");
        result.Should().EndWith(".MoveNext");
    }

    [Fact]
    public void Demangle_GenericArguments_AreCommaSeparated()
    {
        var result = NativeAotSymbolDemangler.Demangle("Foo<Bar__Baz>__Method");
        result.Should().Contain("<");
        result.Should().Contain(", ");
        result.Should().EndWith(".Method");
    }

    [Fact]
    public void Demangle_BoxedUnboxStub_TaggedAndCollapsed()
    {
        const string mangled = "<Boxed>Some_Type__<unbox>Some_Type__Method";
        var result = NativeAotSymbolDemangler.Demangle(mangled);
        result.Should().EndWith(" (boxed)");
        result.Should().Contain("Method");
        result.Should().NotContain("<Boxed>");
        result.Should().NotContain("<unbox>");
    }

    [Fact]
    public void Demangle_UnboxStub_Tagged()
    {
        var result = NativeAotSymbolDemangler.Demangle("unbox_UIntPtr__TryFormat");
        result.Should().EndWith(" (unbox)");
        result.Should().Contain("UIntPtr");
        result.Should().Contain("TryFormat");
    }

    [Fact]
    public void Demangle_RealNativeAotSymbol_BecomesReadable()
    {
        const string mangled =
            "Microsoft_AspNetCore_Http_Microsoft_AspNetCore_Internal_AdaptiveCapacityDictionary_2_Enumerator__MoveNext";
        var result = NativeAotSymbolDemangler.Demangle(mangled);
        result.Length.Should().BeLessThan(mangled.Length);
        result.Should().EndWith("MoveNext");
        result.Should().Contain("Microsoft.AspNetCore");
    }

    [Theory]
    [InlineData(null, NativeAotSymbolDemangler.SymbolSource.Stripped)]
    [InlineData("", NativeAotSymbolDemangler.SymbolSource.Stripped)]
    [InlineData("[unknown]", NativeAotSymbolDemangler.SymbolSource.Stripped)]
    [InlineData("0x7fa1234abcde", NativeAotSymbolDemangler.SymbolSource.Stripped)]
    [InlineData("__libc_start_main", NativeAotSymbolDemangler.SymbolSource.Native)]
    [InlineData("CryptoNative_BioRead", NativeAotSymbolDemangler.SymbolSource.Native)]
    [InlineData("pthread_mutex_lock", NativeAotSymbolDemangler.SymbolSource.Native)]
    [InlineData("S_P_CoreLib_System_String__Equals", NativeAotSymbolDemangler.SymbolSource.ElfMangled)]
    [InlineData("Microsoft_AspNetCore_Http_T__M", NativeAotSymbolDemangler.SymbolSource.ElfMangled)]
    [InlineData("MyType.MyMethod", NativeAotSymbolDemangler.SymbolSource.ElfDemangled)]
    public void Classify_ReturnsExpectedSource(string? symbol, NativeAotSymbolDemangler.SymbolSource expected)
    {
        NativeAotSymbolDemangler.Classify(symbol).Should().Be(expected);
    }

    [Theory]
    [InlineData("pthread_mutex_lock")]
    [InlineData("clock_gettime")]
    [InlineData("epoll_wait")]
    [InlineData("malloc_trim")]
    public void Demangle_GenericNativeFunctionsWithUnderscores_PassThrough(string nativeSymbol)
    {
        NativeAotSymbolDemangler.Demangle(nativeSymbol).Should().Be(nativeSymbol);
    }

    [Theory]
    [InlineData("MyNamespace.Type.get_Value")]
    [InlineData("HeaderDictionary.get_ContentLength")]
    [InlineData("System.String.Equals")]
    public void Demangle_AlreadyDemangledNames_AreReturnedUnchanged(string display)
    {
        NativeAotSymbolDemangler.Demangle(display).Should().Be(display);
        NativeAotSymbolDemangler.Demangle(NativeAotSymbolDemangler.Demangle(display)).Should().Be(display);
    }

    [Fact]
    public void Demangle_IsIdempotent_OnMangledInputAfterFirstPass()
    {
        const string mangled = "S_P_CoreLib_System_String__Equals";
        var first = NativeAotSymbolDemangler.Demangle(mangled);
        var second = NativeAotSymbolDemangler.Demangle(first);
        second.Should().Be(first);
    }

    [Fact]
    public void Demangle_BoxedUnboxStub_MismatchedHalves_PreservesBoth()
    {
        var result = NativeAotSymbolDemangler.Demangle("<Boxed>IFoo__<unbox>ConcreteFoo__M");
        result.Should().EndWith(" (boxed)");
        result.Should().Contain("IFoo");
        result.Should().Contain("ConcreteFoo");
        result.Should().Contain("M");
    }

    [Fact]
    public void Combine_DistinctConcreteSources_ReturnsMixed()
    {
        NativeAotSymbolDemangler.Combine(
            NativeAotSymbolDemangler.SymbolSource.ElfDemangled,
            NativeAotSymbolDemangler.SymbolSource.Stripped)
            .Should().Be(NativeAotSymbolDemangler.SymbolSource.Mixed);
    }

    [Fact]
    public void Combine_MangledAndDemangled_CollapsesToDemangled()
    {
        NativeAotSymbolDemangler.Combine(
            NativeAotSymbolDemangler.SymbolSource.ElfMangled,
            NativeAotSymbolDemangler.SymbolSource.ElfDemangled)
            .Should().Be(NativeAotSymbolDemangler.SymbolSource.ElfDemangled);
    }
}
