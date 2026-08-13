namespace DotnetInspector.Packages;

/// <summary>
/// Applies current archive limits to content returned by a package store.
/// </summary>
internal static class PackageContentAdmission
{
    internal static async Task<bool> IsAdmissibleAsync(
        IPackageContent content,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (!content.TryOpenArchive(out Stream? archiveStream))
            return false;

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
                is PackageArchiveValidation.Valid;
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
