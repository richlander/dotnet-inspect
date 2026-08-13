using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full archive
/// limit set. Product-owned trees (app-cache slots carrying the commit marker)
/// must then match the archive entry paths, sizes, and CRC-32 values so a valid
/// nupkg cannot launder a mutated extract. Foreign trees such as NuGet's
/// global-packages folder are not 1:1 extracts (OPC entries omitted, sidecar
/// metadata files, nuspec casing) and cannot be rewritten by this product — they
/// receive reparse-point and expanded-byte gates only. Archive-less entries
/// require a top-level <c>.nuspec</c> and a full tree walk that counts every
/// filesystem node toward <see cref="PackagePayloadLimits.MaxEntryCount"/>.
/// Expanded-byte tally omits the retained archive path (when present) and the
/// internal commit marker.
/// </remarks>
internal static class PackageContentAdmission
{
    internal enum Outcome
    {
        Admissible,
        MissingArchive,
        LimitsExceeded,
    }

    internal static async Task<bool> IsAdmissibleAsync(
        IPackageContent content,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken) =>
        await EvaluateAsync(content, limits, cancellationToken)
            .ConfigureAwait(false)
        == Outcome.Admissible;

    internal static async Task<Outcome> EvaluateAsync(
        IPackageContent content,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (content.TryOpenArchive(out Stream? archiveStream))
        {
            byte[]? archive;
            await using (archiveStream!.ConfigureAwait(false))
            {
                archive = await ReadBoundedAsync(
                        archiveStream,
                        limits.MaxArchiveBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (archive is null
                || PackageArchiveValidator.Validate(
                    archive,
                    limits,
                    cancellationToken)
                    is not PackageArchiveValidation.Valid)
            {
                return Outcome.LimitsExceeded;
            }

            if (content.RootPath is not null
                && !AdmitExtractedTreeWithArchive(
                    content.RootPath,
                    archive,
                    content.NupkgPath,
                    limits,
                    cancellationToken))
            {
                return Outcome.LimitsExceeded;
            }

            return Outcome.Admissible;
        }

        if (content.RootPath is null
            || !HasTopLevelNuspec(content.RootPath))
        {
            return Outcome.MissingArchive;
        }

        return IsExtractedTreeWithinLimits(
                content.RootPath,
                retainedNupkgPath: null,
                limits)
            ? Outcome.Admissible
            : Outcome.LimitsExceeded;
    }

    /// <summary>
    /// Archive-less walk: every filesystem node counts toward
    /// <see cref="PackagePayloadLimits.MaxEntryCount"/>; file bytes toward
    /// <see cref="PackagePayloadLimits.MaxExpandedBytes"/>. Reparse points
    /// fail closed. The retained archive path (when set) and the commit marker
    /// are omitted from the expanded-byte tally.
    /// </summary>
    internal static bool IsExtractedTreeWithinLimits(
        string root,
        string? retainedNupkgPath,
        PackagePayloadLimits limits) =>
        WalkExtractedTree(
            root,
            retainedNupkgPath,
            limits,
            countEveryNodeTowardEntryLimit: true);

    /// <summary>
    /// Product-owned extracts (commit marker present) must match the archive.
    /// Foreign layouts (global-packages) get walk-only safety gates.
    /// </summary>
    internal static bool AdmitExtractedTreeWithArchive(
        string root,
        byte[] archive,
        string? retainedNupkgPath,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken) =>
        HasCommitMarker(root)
            ? ExtractedTreeMatchesArchive(
                root,
                archive,
                retainedNupkgPath,
                limits,
                cancellationToken)
            : WalkExtractedTree(
                root,
                retainedNupkgPath,
                limits,
                countEveryNodeTowardEntryLimit: false);

    static bool HasCommitMarker(string root)
    {
        try
        {
            return File.Exists(
                Path.Combine(root, NuGetCache.CommitMarkerFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// After a valid archive, require the extracted tree to match archive entry
    /// paths, sizes, and CRC-32 values and reject reparse points. Extra files
    /// allowed only for the retained nupkg and the commit marker; extra
    /// directories allowed only as parents of archive entries.
    /// </summary>
    internal static bool ExtractedTreeMatchesArchive(
        string root,
        byte[] archive,
        string? retainedNupkgPath,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || IsReparsePoint(root))
            return false;

        // First pass: no symlink escape and expanded bytes stay in bound.
        // Entry-count is not reapplied against FS nodes here — the archive
        // already paid MaxEntryCount; directory fan-out is constrained below
        // by matching the expected directory set.
        if (!WalkExtractedTree(
                root,
                retainedNupkgPath,
                limits,
                countEveryNodeTowardEntryLimit: false))
        {
            return false;
        }

        HashSet<string> allowedExtraFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            NuGetCache.CommitMarkerFileName,
        };
        if (retainedNupkgPath is not null)
        {
            allowedExtraFiles.Add(
                Path.GetRelativePath(root, retainedNupkgPath)
                    .Replace('\\', '/'));
        }

        HashSet<string> archiveFiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedDirs = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = new ZipArchive(
                new MemoryStream(archive, writable: false),
                ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(relative))
                    continue;

                if (relative.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relative))
                {
                    return false;
                }

                bool isDirectory = relative.EndsWith('/')
                    || (string.IsNullOrEmpty(entry.Name)
                        && entry.Length == 0);
                if (isDirectory)
                {
                    string dir = relative.TrimEnd('/');
                    if (dir.Length > 0)
                        RememberDirectory(expectedDirs, dir);
                    continue;
                }

                archiveFiles.Add(relative);
                RememberDirectoryParents(expectedDirs, relative);

                string onDisk = Path.Combine(
                    root,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(onDisk) || IsReparsePoint(onDisk))
                    return false;

                long length;
                uint crc;
                try
                {
                    length = new FileInfo(onDisk).Length;
                    if (length != entry.Length)
                        return false;
                    crc = ComputeFileCrc32(onDisk);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                if (crc != entry.Crc32)
                    return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return false;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path
                    .GetRelativePath(root, file)
                    .Replace('\\', '/');
                if (archiveFiles.Contains(relative)
                    || allowedExtraFiles.Contains(relative))
                {
                    continue;
                }

                return false;
            }

            foreach (string directory in Directory.EnumerateDirectories(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                if (IsReparsePoint(directory))
                    return false;

                string relative = Path
                    .GetRelativePath(root, directory)
                    .Replace('\\', '/');
                if (!expectedDirs.Contains(relative))
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
    }

    static void RememberDirectoryParents(
        HashSet<string> expectedDirs,
        string relativeFile)
    {
        int index = 0;
        while (index < relativeFile.Length)
        {
            int slash = relativeFile.IndexOf('/', index);
            if (slash < 0)
                break;

            if (slash > 0)
                expectedDirs.Add(relativeFile[..slash]);
            index = slash + 1;
        }
    }

    static void RememberDirectory(
        HashSet<string> expectedDirs,
        string relativeDirectory)
    {
        expectedDirs.Add(relativeDirectory);
        RememberDirectoryParents(expectedDirs, relativeDirectory + "/");
    }

    static uint ComputeFileCrc32(string path)
    {
        var crc = new ZipCrc32();
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            crc.Append(buffer.AsSpan(0, read));
        return crc.Value;
    }

    static bool WalkExtractedTree(
        string root,
        string? retainedNupkgPath,
        PackagePayloadLimits limits,
        bool countEveryNodeTowardEntryLimit)
    {
        if (!Directory.Exists(root))
            return false;

        if (IsReparsePoint(root))
            return false;

        StringComparison pathComparison = PathComparison;
        string? normalizedRetained = retainedNupkgPath is null
            ? null
            : Path.GetFullPath(retainedNupkgPath);
        // Only the product marker at the package root is internal metadata —
        // nested files that reuse the marker name still count toward budget.
        string normalizedMarker = Path.GetFullPath(
            Path.Combine(root, NuGetCache.CommitMarkerFileName));

        long expandedBytes = 0;
        int entryCount = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            List<string> children;
            try
            {
                children = Directory
                    .EnumerateFileSystemEntries(directory)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            foreach (string entry in children)
            {
                if (countEveryNodeTowardEntryLimit)
                {
                    entryCount++;
                    if (entryCount > limits.MaxEntryCount)
                        return false;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return false;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                // Skip retained archive (bounded by MaxArchiveBytes) and the
                // root commit marker only (not package content).
                if (IsExcludedFromExpandedBytes(
                        entry,
                        normalizedRetained,
                        normalizedMarker,
                        pathComparison))
                    continue;

                long length;
                try
                {
                    length = new FileInfo(entry).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                if (length > limits.MaxExpandedBytes - expandedBytes)
                    return false;

                expandedBytes += length;
            }
        }

        return true;
    }

    static bool IsExcludedFromExpandedBytes(
        string path,
        string? normalizedRetainedNupkg,
        string normalizedRootMarker,
        StringComparison pathComparison)
    {
        string fullPath = Path.GetFullPath(path);
        if (normalizedRetainedNupkg is not null
            && string.Equals(
                fullPath,
                normalizedRetainedNupkg,
                pathComparison))
        {
            return true;
        }

        return string.Equals(
            fullPath,
            normalizedRootMarker,
            pathComparison);
    }

    /// <summary>
    /// Match filesystem identity the way the host volume does: case-insensitive
    /// on Windows, case-sensitive elsewhere. Avoids excluding case-only nupkg
    /// siblings on Linux while still treating Windows paths as the same file.
    /// </summary>
    static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal static bool HasTopLevelNuspec(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root)
                .Any(path => path.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    internal static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source
                .ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            if (read > maxBytes - buffer.Length)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
