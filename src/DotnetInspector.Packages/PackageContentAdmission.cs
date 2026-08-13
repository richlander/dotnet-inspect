namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full archive
/// limit set. When a filesystem <c>RootPath</c> is also present, the extracted
/// tree is checked for reparse-point escape and expanded-byte limits — not for
/// archive entry count, which is a ZIP central-directory concept and is always
/// lower than the post-extract filesystem node count (directories, retained
/// nupkg, commit marker). Archive-less entries (stripped <c>.nupkg</c>) require
/// a top-level <c>.nuspec</c> and a full tree walk that counts every filesystem
/// node toward <see cref="PackagePayloadLimits.MaxEntryCount"/>.
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
            await using (archiveStream!.ConfigureAwait(false))
            {
                byte[]? archive = await ReadBoundedAsync(
                        archiveStream,
                        limits.MaxArchiveBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (archive is null
                    || PackageArchiveValidator.Validate(
                        archive,
                        limits,
                        cancellationToken)
                        is not PackageArchiveValidation.Valid)
                {
                    return Outcome.LimitsExceeded;
                }
            }

            // Archive limits already bound ZIP structure. The tree check only
            // rejects symlink escape and expanded-byte growth under RootPath.
            if (content.RootPath is not null
                && !IsExtractedTreeSafeAfterArchive(
                    content.RootPath,
                    limits))
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

        return IsExtractedTreeWithinLimits(content.RootPath, limits)
            ? Outcome.Admissible
            : Outcome.LimitsExceeded;
    }

    /// <summary>
    /// Archive-less walk: every filesystem node counts toward
    /// <see cref="PackagePayloadLimits.MaxEntryCount"/>; file bytes toward
    /// <see cref="PackagePayloadLimits.MaxExpandedBytes"/>. Reparse points
    /// fail closed.
    /// </summary>
    internal static bool IsExtractedTreeWithinLimits(
        string root,
        PackagePayloadLimits limits) =>
        WalkExtractedTree(
            root,
            limits,
            countEveryNodeTowardEntryLimit: true);

    /// <summary>
    /// Post-archive walk: do not re-apply ZIP entry count to filesystem nodes;
    /// still reject reparse points and enforce expanded bytes.
    /// </summary>
    internal static bool IsExtractedTreeSafeAfterArchive(
        string root,
        PackagePayloadLimits limits) =>
        WalkExtractedTree(
            root,
            limits,
            countEveryNodeTowardEntryLimit: false);

    static bool WalkExtractedTree(
        string root,
        PackagePayloadLimits limits,
        bool countEveryNodeTowardEntryLimit)
    {
        if (!Directory.Exists(root))
            return false;

        if (IsReparsePoint(root))
            return false;

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

                long length;
                try
                {
                    length = new FileInfo(entry).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                // Retained nupkg bytes were already bounded by MaxArchiveBytes.
                if (entry.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    continue;

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
