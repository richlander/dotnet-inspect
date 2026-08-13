using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full archive
/// limit set, then the extracted <c>RootPath</c> is checked for reparse-point
/// escape and for agreement with the archive's entry paths and sizes (so a
/// valid nupkg cannot launder a mutated tree). Archive-less entries require a
/// top-level <c>.nuspec</c> and a full tree walk that counts every filesystem
/// node toward <see cref="PackagePayloadLimits.MaxEntryCount"/>; only the
/// retained archive path (when present) is excluded from expanded-byte tally.
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
                && !ExtractedTreeMatchesArchive(
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
    /// fail closed. Only <paramref name="retainedNupkgPath"/> (when set) is
    /// omitted from the expanded-byte tally.
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
    /// After a valid archive, require the extracted tree to match archive entry
    /// paths/sizes and reject reparse points. Extra files allowed only for the
    /// retained nupkg and the commit marker.
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
        if (!WalkExtractedTree(
                root,
                retainedNupkgPath,
                limits,
                countEveryNodeTowardEntryLimit: false))
        {
            return false;
        }

        HashSet<string> allowedExtras = new(StringComparer.OrdinalIgnoreCase)
        {
            NuGetCache.CommitMarkerFileName,
        };
        if (retainedNupkgPath is not null)
        {
            allowedExtras.Add(
                Path.GetRelativePath(root, retainedNupkgPath)
                    .Replace('\\', '/'));
        }

        HashSet<string> archivePaths = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = new ZipArchive(
                new MemoryStream(archive, writable: false),
                ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(relative)
                    || relative.EndsWith('/'))
                {
                    continue;
                }

                if (relative.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relative))
                {
                    return false;
                }

                archivePaths.Add(relative);
                string onDisk = Path.Combine(
                    root,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(onDisk))
                    return false;

                long length;
                try
                {
                    length = new FileInfo(onDisk).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                if (length != entry.Length)
                    return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return false;
        }

        // No unexpected files under root (commit marker + retained nupkg OK).
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
                if (archivePaths.Contains(relative)
                    || allowedExtras.Contains(relative))
                {
                    continue;
                }

                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
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

        string? normalizedRetained = retainedNupkgPath is null
            ? null
            : Path.GetFullPath(retainedNupkgPath);

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

                // Skip only the retained archive path from expanded tally —
                // those bytes were already bounded by MaxArchiveBytes.
                if (normalizedRetained is not null
                    && string.Equals(
                        Path.GetFullPath(entry),
                        normalizedRetained,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
