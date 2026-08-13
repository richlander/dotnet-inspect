namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full limit
/// set (archive bytes, expanded bytes, entry count, structural ZIP rules).
/// When a filesystem <c>RootPath</c> is also present, the extracted tree is
/// measured as well: consumers open files under that root, so a valid archive
/// must not launder a damaged or symlink-escaping tree. Entries that keep only
/// an extracted tree (stripped <c>.nupkg</c>) require a top-level
/// <c>.nuspec</c> and the same tree walk.
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

            // Archive alone is not enough when consumers will open RootPath.
            if (content.RootPath is not null)
            {
                return IsExtractedTreeWithinLimits(content.RootPath, limits)
                    ? Outcome.Admissible
                    : Outcome.LimitsExceeded;
            }

            return Outcome.Admissible;
        }

        // No retained archive. Require a top-level .nuspec — empty lib/tools
        // directories alone are not a usable package and must not mask a
        // downloadable copy.
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
    /// Walks files and directories under <paramref name="root"/> without
    /// following reparse points. Every filesystem entry counts toward
    /// <see cref="PackagePayloadLimits.MaxEntryCount"/>; file bytes count
    /// toward <see cref="PackagePayloadLimits.MaxExpandedBytes"/>. Stops as
    /// soon as either limit is crossed or a symlink/junction is observed.
    /// </summary>
    internal static bool IsExtractedTreeWithinLimits(
        string root,
        PackagePayloadLimits limits)
    {
        if (!Directory.Exists(root))
            return false;

        // Reject a RootPath that is itself a reparse point (symlink/junction).
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
                // Materialize inside the guard: EnumerateFileSystemEntries is
                // lazy and MoveNext can throw after the constructor returns.
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
                entryCount++;
                if (entryCount > limits.MaxEntryCount)
                    return false;

                FileAttributes attributes;
                try
                {
                    // Do not follow the final component when reading attributes.
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

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from <paramref name="source"/>,
    /// or returns null when the stream carries more.
    /// </summary>
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
