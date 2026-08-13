namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full limit
/// set (archive bytes, expanded bytes, entry count, structural ZIP rules).
/// Entries that keep only an extracted tree — common when a host or image
/// strips <c>.nupkg</c> files from global-packages — cannot prove archive-byte
/// or ZIP structure limits; they are admitted only when the extracted tree is
/// a valid NuGet layout and stays within the expanded-size and entry-count
/// limits that can still be measured from the tree.
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
        if (content.RootPath is null
            || !NuGetCache.IsCachedPackageValid(content.RootPath))
        {
            return Outcome.MissingArchive;
        }

        return IsExtractedTreeWithinLimits(content, limits)
            ? Outcome.Admissible
            : Outcome.LimitsExceeded;
    }

    /// <summary>
    /// Measures entry count and expanded bytes from the already-extracted tree.
    /// Archive-byte and ZIP-structure limits are not applicable without the
    /// retained nupkg.
    /// </summary>
    internal static bool IsExtractedTreeWithinLimits(
        IPackageContent content,
        PackagePayloadLimits limits)
    {
        long expandedBytes = 0;
        int entryCount = 0;
        foreach (string relativePath in content.EnumerateEntries())
        {
            entryCount++;
            if (entryCount > limits.MaxEntryCount)
                return false;

            if (!content.TryOpenEntry(relativePath, out Stream? entry))
                return false;

            using (entry)
            {
                long length = entry.CanSeek
                    ? entry.Length
                    : CountBytes(entry, limits.MaxExpandedBytes - expandedBytes);
                if (length < 0
                    || length > limits.MaxExpandedBytes - expandedBytes)
                {
                    return false;
                }

                expandedBytes += length;
            }
        }

        return true;
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

    /// <returns>
    /// Byte count, or <c>-1</c> when the stream exceeds
    /// <paramref name="maxRemaining"/>.
    /// </returns>
    static long CountBytes(Stream source, long maxRemaining)
    {
        long total = 0;
        byte[] chunk = new byte[81920];
        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return total;

            if (read > maxRemaining - total)
                return -1;

            total += read;
        }
    }
}
