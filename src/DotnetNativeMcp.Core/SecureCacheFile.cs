using System.Runtime.InteropServices;

namespace DotnetNativeMcp.Core;

/// <summary>
/// Helpers for hardening on-disk cache I/O:
/// - Bounded reads (size cap before allocation).
/// - Atomic writes via random-named temp file + <see cref="File.Move(string, string, bool)"/>.
/// - POSIX mode 0700 on cache directories and 0600 on cache files.
///
/// <para>All operations are best-effort: callers must continue to swallow exceptions
/// raised by their cache writers so that cache unavailability never surfaces to clients.</para>
/// </summary>
public static class SecureCacheFile
{
    /// <summary>
    /// Reads the file at <paramref name="path"/> only when its on-disk length is at most
    /// <paramref name="maxBytes"/>. Returns <see langword="null"/> on missing file, oversize, or
    /// any I/O error — over-cap files are deleted so the next call rebuilds the cache.
    /// </summary>
    public static byte[]? TryReadCapped(string path, long maxBytes)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var info = new FileInfo(path);
            if (info.Length > maxBytes)
            {
                TryDelete(path);
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (stream.CanSeek && stream.Length > maxBytes)
            {
                TryDelete(path);
                return null;
            }

            // Read directly from the opened stream to avoid the TOCTOU window
            // between size validation and a second File.Open.
            using var bounded = new MemoryStream();
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    TryDelete(path);
                    return null;
                }

                bounded.Write(buffer, 0, read);
            }

            return bounded.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="bytes"/> to <paramref name="path"/>:
    /// creates the parent directory with mode 0700 (POSIX), writes into a random-named
    /// temp file opened with <see cref="FileMode.CreateNew"/> + <see cref="FileShare.None"/>,
    /// sets mode 0600 on the temp file (POSIX), and renames it over <paramref name="path"/>.
    /// All failures are swallowed.
    /// </summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
            return;

        string? tmp = null;
        try
        {
            CreateSecureDirectory(dir);

            tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Path.GetRandomFileName() + ".tmp");

            using (var fs = new FileStream(
                tmp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            TrySetFileMode0600(tmp);
            File.Move(tmp, path, overwrite: true);
            tmp = null;
        }
        catch
        {
        }
        finally
        {
            if (tmp is not null)
                TryDelete(tmp);
        }
    }

    /// <summary>
    /// Ensures <paramref name="dir"/> exists and is mode 0700 (POSIX). No-op on Windows.
    /// </summary>
    public static void CreateSecureDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            File.SetUnixFileMode(
                dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
        }
    }

    private static void TrySetFileMode0600(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
