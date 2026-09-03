using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>
/// Applies current payload limits to content returned by a package store.
/// </summary>
/// <remarks>
/// Cache hits with a retained archive are revalidated against the full archive
/// limit set. Product-owned trees (app-cache slots carrying the commit marker)
/// must then match the archive entry paths, sizes, and CRC-32 values so a valid
/// nupkg cannot launder a mutated extract. Foreign trees such as NuGet's global-packages folder are not 1:1 extracts
/// (OPC entries omitted, sidecar metadata files, nuspec casing) and cannot be
/// rewritten by this product — they receive reparse-point, expanded-byte, and
/// full-node entry-count gates so zero-byte fan-out cannot unbounded-walk.
/// Archive-less entries require a top-level <c>.nuspec</c> and the same full
/// tree walk. Expanded-byte tally omits the retained archive path (when
/// present) and the internal commit marker.
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

    /// <summary>
    /// Applies the same admission contract synchronously to retained filesystem
    /// content. Path-based assembly binding is synchronous, so it uses this
    /// cache-only form rather than blocking on the asynchronous acquisition
    /// path.
    /// </summary>
    internal static Outcome EvaluateFileSystem(
        IPackageContent content,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        Stream? archiveStream = null;
        bool opened;
        try
        {
            opened = content.TryOpenArchive(out archiveStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return content.RequiresArchiveTreeMatch
                ? Outcome.MissingArchive
                : Outcome.LimitsExceeded;
        }

        if (opened && archiveStream is not null)
        {
            try
            {
                byte[]? archive;
                using (archiveStream)
                {
                    archive = ReadBounded(
                        archiveStream,
                        limits.MaxArchiveBytes,
                        cancellationToken);
                }

                return archive is null
                    ? Outcome.LimitsExceeded
                    : EvaluateArchive(
                        content,
                        archive,
                        limits,
                        cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Outcome.LimitsExceeded;
            }
        }

        if (content.RequiresArchiveTreeMatch)
            return Outcome.MissingArchive;

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

    internal static async Task<Outcome> EvaluateAsync(
        IPackageContent content,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (content is InMemoryPackageContent inMemory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EvaluateArchive(
                content,
                inMemory.RetainedArchive,
                limits,
                cancellationToken);
        }

        Stream? archiveStream = null;
        bool opened;
        try
        {
            opened = content.TryOpenArchive(out archiveStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Concurrent delete/permission change on a retained archive must not
            // escape admission as an unhandled fault. Product-owned content
            // without a readable archive is MissingArchive (same as TryOpen
            // returning false); do not mis-report it as a payload-limit failure.
            return content.RequiresArchiveTreeMatch
                ? Outcome.MissingArchive
                : Outcome.LimitsExceeded;
        }

        if (opened && archiveStream is not null)
        {
            try
            {
                byte[]? archive;
                await using (archiveStream.ConfigureAwait(false))
                {
                    archive = await ReadBoundedAsync(
                            archiveStream,
                            limits.MaxArchiveBytes,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return archive is null
                    ? Outcome.LimitsExceeded
                    : EvaluateArchive(
                        content,
                        archive,
                        limits,
                        cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Outcome.LimitsExceeded;
            }
        }

        // Product-owned commits always retain an archive. A missing nupkg must
        // not fall through to foreign walk-only gates (mutated extract + deleted
        // archive would otherwise admit without CRC/path match).
        if (content.RequiresArchiveTreeMatch)
            return Outcome.MissingArchive;

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

    static Outcome EvaluateArchive(
        IPackageContent content,
        byte[] archive,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (PackageArchiveValidator.Validate(
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
                content.RequiresArchiveTreeMatch,
                cancellationToken))
        {
            return Outcome.LimitsExceeded;
        }

        return Outcome.Admissible;
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
    /// Product-owned extracts must match the archive. Ownership is the
    /// immutable <paramref name="requiresArchiveTreeMatch"/> flag from the
    /// store (or a still-present commit marker as a belt-and-suspenders check
    /// for hand-constructed test content). Foreign layouts (global-packages)
    /// get walk-only safety gates (reparse, expanded bytes, every FS node
    /// toward <see cref="PackagePayloadLimits.MaxEntryCount"/>) plus the same
    /// top-level <c>.nuspec</c> usability gate as archive-less slots so a
    /// retained nupkg beside an empty folder is not published as content.
    /// </summary>
    internal static bool AdmitExtractedTreeWithArchive(
        string root,
        byte[] archive,
        string? retainedNupkgPath,
        PackagePayloadLimits limits,
        bool requiresArchiveTreeMatch,
        CancellationToken cancellationToken) =>
        requiresArchiveTreeMatch || HasCommitMarker(root)
            ? ExtractedTreeMatchesArchive(
                root,
                archive,
                retainedNupkgPath,
                limits,
                cancellationToken)
            : HasTopLevelNuspec(root)
                && WalkExtractedTree(
                    root,
                    retainedNupkgPath,
                    limits,
                    countEveryNodeTowardEntryLimit: true);

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

        // First pass: symlink/expanded-byte gates, plus a node budget so a
        // mutated product-owned tree cannot force an unbounded walk before the
        // archive match rejects it. Budget = archive entries + unique dirs +
        // allowed extras (retained nupkg, commit marker, headroom). Saturate
        // rather than checked-add so hostile MaxUniqueDirectories cannot throw
        // OverflowException out of admission.
        long nodeBudget =
            (long)limits.MaxEntryCount + limits.MaxUniqueDirectories + 8;
        int productOwnedNodeBudget = nodeBudget > int.MaxValue
            ? int.MaxValue
            : (int)nodeBudget;
        if (!WalkExtractedTree(
                root,
                retainedNupkgPath,
                limits with { MaxEntryCount = productOwnedNodeBudget },
                countEveryNodeTowardEntryLimit: true))
        {
            return false;
        }

        // Path identity follows the host volume so case-only siblings on
        // case-sensitive filesystems remain distinct extras/dirs/files.
        StringComparer pathComparer = PathComparer;
        HashSet<string> allowedExtraFiles = new(pathComparer)
        {
            NuGetCache.CommitMarkerFileName,
        };
        if (retainedNupkgPath is not null)
        {
            allowedExtraFiles.Add(
                Path.GetRelativePath(root, retainedNupkgPath)
                    .Replace('\\', '/'));
        }

        HashSet<string> archiveFiles = new(pathComparer);
        HashSet<string> expectedDirs = new(pathComparer);
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

                // Segment rules match PackageArchiveValidator / StorePath: interior
                // dots (Foo..dll) are legal; only exact "." / ".." segments
                // and rooted paths are rejected.
                if (HasUnsafeArchiveRelativePath(relative))
                    return false;

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
            // Incremental enum: do not materialize a whole directory before the
            // entry-count gate can reject a hostile fan-out. Guard the whole
            // foreach — GetEnumerator/MoveNext throw outside the old
            // EnumerateFileSystemEntries-only try.
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             directory))
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
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

    static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
    /// True when an archive-relative path is unsafe for tree matching.
    /// Segments use <see cref="StorePath.IsSafeSegment"/> so names like
    /// <c>Foo..dll</c> are allowed while exact <c>..</c> segments are not.
    /// </summary>
    static bool HasUnsafeArchiveRelativePath(string relative)
    {
        if (relative.Length > PackageArchiveValidator.MaxEntryPathLength
            || Path.IsPathRooted(relative))
        {
            return true;
        }

        // Match PackageArchiveValidator: do not TrimEntries — a segment that is
        // only whitespace or padded " .. " must keep its original spelling so
        // IsSafeSegment agrees with archive admission. Empty segments from
        // leading/trailing '/' (directory markers) are skipped.
        foreach (string segment in relative.Split(
                     '/',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Length > PackageArchiveValidator.MaxEntrySegmentLength
                || !StorePath.IsSafeSegment(segment))
            {
                return true;
            }
        }

        return false;
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
    /// Reads exactly <paramref name="length"/> bytes from <paramref name="source"/>,
    /// returning null when the stream ends early or carries even one byte more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the reader for a payload whose length the transport already
    /// declared. <see cref="ReadBoundedAsync"/> cannot use that declaration as a
    /// stop condition — it exists precisely for sources that advertise nothing
    /// or under-report — so it grows a buffer as bytes arrive, transiently
    /// holding the old and new arrays at once. A host that reserved capacity
    /// from the advertised length would then be wrong about its own peak while
    /// the payload lands. Here the declared length is allocated once and filled,
    /// so the reservation and the allocation are the same number.
    /// </para>
    /// <para>
    /// The declaration is not trusted as a description of the body: a short body
    /// is a truncated transfer and a long one contradicts its own header, and
    /// both are rejected rather than accepted at the declared prefix. The
    /// caller's archive bound still applies — it is what admits the declared
    /// length in the first place.
    /// </para>
    /// <para>
    /// Gated by <c>PackagePayloadAcquisitionTests.ReadExactAsync_*</c> and
    /// <c>AdvertisedLengthPayload_IsReadIntoExactlyOneBufferOfThatLength</c>.
    /// </para>
    /// </remarks>
    internal static async Task<byte[]?> ReadExactAsync(
        Stream source,
        int length,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        byte[] buffer = length == 0 ? [] : new byte[length];
        int total = 0;
        while (total < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source
                .ReadAsync(buffer.AsMemory(total, length - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return null;

            total += read;
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] probe = new byte[1];
        int extra = await source
            .ReadAsync(probe, cancellationToken)
            .ConfigureAwait(false);
        return extra == 0 ? buffer : null;
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from <paramref name="source"/>.
    /// Returns null when the stream exceeds the bound. Uses a single growable
    /// buffer (no <see cref="MemoryStream.ToArray"/> double-copy) so a payload
    /// at the configured archive ceiling does not transiently allocate ~2×.
    /// This is the reader for a payload of unknown length; one whose length the
    /// transport declared uses <see cref="ReadExactAsync"/> instead.
    /// </summary>
    internal static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (maxBytes > int.MaxValue)
            maxBytes = int.MaxValue;

        int max = (int)maxBytes;

        // Seekable Length is only a fast-reject / sizing hint — never the stop
        // condition. A Length under-report or a file that grows after Length is
        // observed must still drain to EOF under the bound (with a one-byte
        // over-limit probe), matching the non-seekable path.
        int initialCapacity = max == 0 ? 0 : Math.Min(81920, max);
        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining < 0)
                remaining = 0;
            if (remaining > max)
                return null;
            if (remaining > initialCapacity)
                initialCapacity = (int)remaining;
        }

        byte[] buffer = initialCapacity == 0 ? [] : new byte[initialCapacity];
        int total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (total == buffer.Length)
            {
                // Probe before growing so an exact-length fill (common for
                // seekable files sized from Length) does not transiently
                // allocate ~2× only to observe EOF and shrink back.
                byte[] probe = new byte[1];
                int extra = await source
                    .ReadAsync(probe, cancellationToken)
                    .ConfigureAwait(false);
                if (extra == 0)
                    return total == 0 ? [] : buffer;
                if (total == max)
                    return null;

                int growTo = (int)Math.Min(max, Math.Max((long)buffer.Length * 2, 81920));
                if (growTo <= buffer.Length)
                    growTo = max;
                Array.Resize(ref buffer, growTo);
                buffer[total++] = probe[0];
                continue;
            }

            int read = await source
                .ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                if (total == 0)
                    return [];
                if (total != buffer.Length)
                    Array.Resize(ref buffer, total);
                return buffer;
            }

            total += read;
        }
    }

    static byte[]? ReadBounded(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (maxBytes > int.MaxValue)
            maxBytes = int.MaxValue;

        int max = (int)maxBytes;
        int initialCapacity = max == 0 ? 0 : Math.Min(81920, max);
        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining < 0)
                remaining = 0;
            if (remaining > max)
                return null;
            if (remaining > initialCapacity)
                initialCapacity = (int)remaining;
        }

        byte[] buffer = initialCapacity == 0 ? [] : new byte[initialCapacity];
        int total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (total == buffer.Length)
            {
                int extra = source.ReadByte();
                if (extra < 0)
                    return total == 0 ? [] : buffer;
                if (total == max)
                    return null;

                int growTo = (int)Math.Min(
                    max,
                    Math.Max((long)buffer.Length * 2, 81920));
                if (growTo <= buffer.Length)
                    growTo = max;
                Array.Resize(ref buffer, growTo);
                buffer[total++] = (byte)extra;
                continue;
            }

            int read = source.Read(
                buffer,
                total,
                buffer.Length - total);
            if (read == 0)
            {
                if (total == 0)
                    return [];
                if (total != buffer.Length)
                    Array.Resize(ref buffer, total);
                return buffer;
            }

            total += read;
        }
    }
}
