namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full limit
/// set (archive bytes, expanded bytes, entry count, structural ZIP rules).
/// Entries that keep only an extracted tree — common when a host or image
/// strips <c>.nupkg</c> files from global-packages — cannot prove archive-byte
/// or ZIP structure limits; they are admitted only when the tree has a
/// top-level <c>.nuspec</c> and the full filesystem walk (files and
/// directories) stays within the expanded-size and entry-count limits.
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
            await using (archiveStream.ConfigureAwait(false))
            {
                byte[]? archive = await ReadBoundedAsync(
                        archiveStream,
                        limits.MaxArchiveBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                return archive is not null
                    && PackageArchiveValidator.Validate(
                        archive,
                        limits,
                        cancellationToken)
                    is PackageArchiveValidation.Valid
                    ? Outcome.Admissible
                    : Outcome.LimitsExceeded;
            }
        }

        // No retained archive. In-memory content always exposes one; a
        // filesystem entry without a .nupkg is the NuGet global-packages case.
        // Require a top-level .nuspec — empty lib/tools directories alone are not
        // a usable package and must not mask a downloadable copy.
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
    /// Walks files and directories under <paramref name="root"/>, counting every
    /// filesystem entry toward <see cref="PackagePayloadLimits.MaxEntryCount"/>
    /// and file bytes toward <see cref="PackagePayloadLimits.MaxExpandedBytes"/>.
    /// Stops as soon as either limit is crossed.
    /// </summary>
    internal static bool IsExtractedTreeWithinLimits(
        string root,
        PackagePayloadLimits limits)
    {
        if (!Directory.Exists(root))
            return false;

        long expandedBytes = 0;
        int entryCount = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(directory);
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
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

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
