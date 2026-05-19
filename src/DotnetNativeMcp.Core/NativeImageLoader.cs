using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DotnetNativeMcp.Core;

public static class NativeImageLoader
{
    private const int BuildIdPrefixLength = 16;
    private const int BinaryNameHashPrefixLength = 8;

    private static readonly ConcurrentDictionary<string, NativeImageInfo> LoadedImagesByPath = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, NativeImageInfo> LoadedImagesByHandle = new(StringComparer.Ordinal);

    public const string ScaffoldNotice =
        "dotnet-native-mcp is in scaffold phase. See docs/handoff-contract.md and the V0 issue.";

    public static NativeResult<NativeImageInfo> LoadNativeBinary(string binaryPath)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            return NativeResult.Fail<NativeImageInfo>(
                NativeErrorKind.InvalidArgument,
                "binaryPath is required.");
        }

        var fullPath = Path.GetFullPath(binaryPath);
        if (!File.Exists(fullPath))
        {
            return NativeResult.Fail<NativeImageInfo>(
                NativeErrorKind.ImageNotFound,
                $"Native binary was not found at '{fullPath}'.");
        }

        var image = LoadedImagesByPath.GetOrAdd(fullPath, path =>
        {
            var handle = BuildHandle(path);
            var mstatPath = FindPairedMstatPath(path);
            return new NativeImageInfo(handle, path, mstatPath);
        });
        LoadedImagesByHandle[image.ImageHandle] = image;

        return NativeResult.Success(image);
    }

    public static bool TryGetLoadedImage(string imageHandle, out NativeImageInfo imageInfo) =>
        LoadedImagesByHandle.TryGetValue(imageHandle, out imageInfo!);

    public static string? FindPairedMstatPath(string binaryPath)
    {
        var fullPath = Path.GetFullPath(binaryPath);
        var candidates = new[]
        {
            $"{fullPath}.mstat",
            Path.ChangeExtension(fullPath, ".mstat"),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildHandle(string binaryPath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(binaryPath);
        var buildHash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        var binaryHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(binaryPath)))).ToLowerInvariant();

        return $"i:{buildHash[..BuildIdPrefixLength]}:{binaryHash[..BinaryNameHashPrefixLength]}";
    }
}

public sealed record NativeImageInfo(string ImageHandle, string BinaryPath, string? MstatPath);

public sealed record NativeError(string Kind, string Detail);

public sealed record NativeResult<T>(bool Ok, T? Value, NativeError? Error);

public static class NativeResult
{
    public static NativeResult<T> Success<T>(T value) => new(true, value, null);

    public static NativeResult<T> Fail<T>(string kind, string detail) =>
        new(false, default, new NativeError(kind, detail));
}

public static class NativeErrorKind
{
    public const string InvalidArgument = "invalid_argument";
    public const string ImageNotFound = "image_not_found";
    public const string UnknownImageHandle = "unknown_image_handle";
    public const string MstatNotFound = "mstat_not_found";
    public const string InvalidMstat = "invalid_mstat";
}
