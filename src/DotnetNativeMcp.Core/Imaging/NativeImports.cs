namespace DotnetNativeMcp.Core.Imaging;

/// <summary>One imported function entry discovered in a native image.</summary>
public sealed record ImportedFunction(string? Library, string Name, ushort? Ordinal);

/// <summary>One imported library dependency discovered in a native image.</summary>
public sealed record ImportedLibrary(string Name);
