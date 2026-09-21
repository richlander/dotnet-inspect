using System.Security;

namespace DotnetInspect.Cli.Output;

internal static class EvidenceEnvelopeOutput
{
    internal static bool TryResolvePath(
        string path,
        out string fullPath,
        out string? error)
    {
        fullPath = "";
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "--evidence-envelope requires a non-empty path.";
            return false;
        }
        if (path == "-")
        {
            error =
                "--evidence-envelope requires a file path; '-' is not a stdout shorthand.";
            return false;
        }
        if (path[0] == '-')
        {
            error =
                "--evidence-envelope requires a file path, not an option-shaped value.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            error = "--evidence-envelope requires a valid file path.";
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            error =
                "--evidence-envelope requires a file destination, not a directory.";
            return false;
        }

        string? parent = Path.GetDirectoryName(fullPath);
        if (parent is null || !Directory.Exists(parent))
        {
            error =
                "--evidence-envelope requires an existing parent directory.";
            return false;
        }

        return true;
    }

    internal static bool TryPublish(
        string path,
        ReadOnlySpan<byte> payload,
        out Exception? exception)
    {
        string parent = Path.GetDirectoryName(path)!;
        string temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        bool published = false;
        exception = null;
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(payload);
            }
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
            published = true;
        }
        catch (Exception caught)
            when (caught is IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            exception = caught;
        }
        finally
        {
            try
            {
                if (!published && File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException)
                when (cleanupException is IOException
                    or UnauthorizedAccessException
                    or SecurityException)
            {
                exception = exception is null
                    ? cleanupException
                    : new AggregateException(exception, cleanupException);
                published = false;
            }
        }

        return published;
    }

    internal static bool PathsMayIdentifySameFile(
        string first,
        string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);
        if (!OperatingSystem.IsWindows())
        {
            return string.Equals(
                first,
                second,
                StringComparison.OrdinalIgnoreCase);
        }

        string? normalizedFirst = NormalizeWindowsPath(first);
        string? normalizedSecond = NormalizeWindowsPath(second);
        return normalizedFirst is null
            || normalizedSecond is null
            || string.Equals(
                normalizedFirst,
                normalizedSecond,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string? NormalizeWindowsPath(string path)
    {
        if (path.StartsWith(
                @"\\?\UNC\",
                StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        if (path.StartsWith(
                @"\\?\",
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                @"\\.\",
                StringComparison.OrdinalIgnoreCase))
        {
            return path.Length >= 7
                && char.IsAsciiLetter(path[4])
                && path[5] == ':'
                && path[6] is '\\' or '/'
                    ? path[4..]
                    : null;
        }

        return path;
    }
}
