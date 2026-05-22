using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core;

/// <summary>Centralized resource caps for file- and result-bounded operations.</summary>
public static class ResourceLimits
{
    public const long MaxImageBytes = 512L * 1024 * 1024;
    public const long MaxDgmlBytes = 64L * 1024 * 1024;
    public const long MaxIlMapBytes = 16L * 1024 * 1024;
    public const int MaxManifestEntries = 1024;
    public const int MaxStringMatches = 500_000;
    public const int MaxExtractedStringChars = 16_384;
    public const int MaxCallerSites = 100_000;
    public const int MaxDgmlNodes = 1_000_000;
    public const int MaxDgmlEdges = 2_000_000;
    public const int MaxIlMapEntries = 1_048_576;
    public const long MaxPdbBytes = 64L * 1024 * 1024;
    public const long MaxMapFileBytes = 256L * 1024 * 1024;
    public const int MaxMapFileEntries = 5_000_000;
    public const long MaxMstatBytes = 256L * 1024 * 1024;
    public const int MaxMstatAttributions = 5_000_000;
    public const long MaxXrefCacheBytes = 256L * 1024 * 1024;
    public const long MaxEmbeddedPdbCacheBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Reads an entire file only when it fits under <paramref name="maxBytes"/>.
    /// Returns <see cref="ErrorKinds.FileTooLarge"/> when the file exceeds the cap.
    /// </summary>
    public static NativeResult<byte[]> SafeReadAllBytes(string path, long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<byte[]>(ErrorKinds.InvalidArgument, "path must not be empty.");

        if (!File.Exists(path))
            return NativeResult.Fail<byte[]>(ErrorKinds.BinaryNotFound, $"File not found: '{path}'.");

        try
        {
            var info = new FileInfo(path);
            if (info.Length > maxBytes)
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.FileTooLarge,
                    $"'{path}' is {info.Length} bytes, which exceeds the limit of {maxBytes} bytes.");

            using var stream = File.OpenRead(path);
            if (stream.CanSeek && stream.Length > maxBytes)
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.FileTooLarge,
                    $"'{path}' is {stream.Length} bytes, which exceeds the limit of {maxBytes} bytes.");

            var length = stream.CanSeek ? stream.Length : info.Length;
            if (length > int.MaxValue)
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.FileTooLarge,
                    $"'{path}' is {length} bytes, which exceeds the maximum readable size of {int.MaxValue} bytes.");

            var bytes = new byte[(int)length];
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
                if (read == 0)
                {
                    return NativeResult.Fail<byte[]>(
                        ErrorKinds.InternalError,
                        $"Failed to read '{path}': unexpected end of file.");
                }

                totalRead += read;
            }

            if (stream.ReadByte() != -1)
            {
                return NativeResult.Fail<byte[]>(
                    ErrorKinds.FileTooLarge,
                    $"'{path}' grew while it was being read and now exceeds the limit of {maxBytes} bytes.");
            }

            return NativeResult.Ok($"Read {bytes.Length} bytes from '{Path.GetFileName(path)}'.", bytes);
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<byte[]>(
                ErrorKinds.InternalError,
                $"Failed to read '{path}': {ex.Message}",
                ex.ToString());
        }
    }
}
