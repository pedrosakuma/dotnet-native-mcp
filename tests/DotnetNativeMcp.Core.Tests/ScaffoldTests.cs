using DotnetNativeMcp.Core;
using FluentAssertions;
using Xunit;

namespace DotnetNativeMcp.Core.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void Scaffold_notice_is_present()
    {
        // Anchor test so 'dotnet test' has something to run during the scaffold
        // phase. Delete me when the first real loader test lands.
        NativeImageLoader.ScaffoldNotice.Should().Contain("scaffold phase");
    }
}
