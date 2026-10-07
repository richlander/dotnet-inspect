using ILInspector.SourceLink;

namespace DotnetInspector.Services;

/// <summary>
/// Reads a compiler source file named by a portable PDB only when the file's
/// bytes authenticate against that PDB document's checksum.
/// </summary>
public static class VerifiedLocalSourceRead
{
    public static byte[]? TryRead(
        string? localPath,
        string? checksumAlgorithm,
        byte[]? checksum)
    {
        if (string.IsNullOrEmpty(localPath)
            || checksumAlgorithm is not { Length: > 0 }
            || checksum is not { Length: > 0 }
            || !IsCompilerLanguageSourcePath(localPath)
            || !IsLocalFileSystemPath(localPath))
        {
            return null;
        }

        byte[] content;
        try
        {
            var info = new FileInfo(localPath);
            if (!info.Exists
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.Length > MaxLocalSourceBytes)
            {
                return null;
            }
            content = File.ReadAllBytes(localPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return null;
        }

        return SourceLinkService.VerifyChecksum(
                checksumAlgorithm,
                checksum,
                content)
            is SourceChecksumVerification.Exact
                or SourceChecksumVerification.LineEndingNormalized
            ? content
            : null;
    }

    public static byte[]? TryRead(SourceDocumentObservation document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Checksum is not { Length: > 0 })
            return null;

        byte[] checksum;
        try
        {
            checksum = Convert.FromHexString(document.Checksum);
        }
        catch (FormatException)
        {
            return null;
        }

        return TryRead(
            document.OriginalPath,
            document.ChecksumAlgorithm,
            checksum);
    }

    static bool IsCompilerLanguageSourcePath(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase);

    const long MaxLocalSourceBytes = 64L * 1024 * 1024;

    internal static bool IsLocalFileSystemPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return false;
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal)
            || full.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string? root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root)
                && new DriveInfo(root).DriveType == DriveType.Network)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
        }

        return true;
    }
}
