using System.IO.Enumeration;

namespace DotnetInspector.Artifacts.Local;

public sealed record LocalDirectoryArtifactAcquisitionOptions
{
    public const int DefaultMaxObservedEntries = 1024;
    public const int DefaultMaxSelectedFiles = 1024;
    public const long DefaultMaxFileBytes = 512L * 1024 * 1024;
    public const long DefaultMaxTotalBytes = 512L * 1024 * 1024;

    public IReadOnlyCollection<string> ExcludedFileNames { get; init; } =
        Array.Empty<string>();

    public IReadOnlyCollection<string> IncludedFileExtensions { get; init; } =
        new[] { ".dll" };

    public int MaxObservedEntries { get; init; } =
        DefaultMaxObservedEntries;

    public int MaxSelectedFiles { get; init; } =
        DefaultMaxSelectedFiles;

    public long MaxFileBytes { get; init; } =
        DefaultMaxFileBytes;

    public long MaxTotalBytes { get; init; } =
        DefaultMaxTotalBytes;
}

/// <summary>
/// Typed evidence for one top-level file copied from a local directory.
/// </summary>
public sealed record LocalDirectoryArtifactProvenance :
    IArtifactProvenance
{
    public LocalDirectoryArtifactProvenance(
        string canonicalRoot,
        string relativeName,
        string fullPath,
        long contentLength,
        DateTime observedLastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        CanonicalRoot = canonicalRoot;
        RelativeName = relativeName;
        FullPath = fullPath;
        ContentLength = contentLength;
        ObservedLastWriteTimeUtc = observedLastWriteTimeUtc;
    }

    public string CanonicalRoot { get; }
    public string RelativeName { get; }
    public string FullPath { get; }
    public long ContentLength { get; }
    public DateTime ObservedLastWriteTimeUtc { get; }
}

public sealed record LocalDirectoryArtifactDiagnostic :
    IArtifactAcquisitionDiagnostic
{
    public LocalDirectoryArtifactDiagnostic(
        string code,
        string summary,
        string requestedRoot,
        string? canonicalRoot,
        string? relativeName = null,
        string? fullPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedRoot);
        if (relativeName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(relativeName);
        if (fullPath is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        Code = code;
        Summary = summary;
        RequestedRoot = requestedRoot;
        CanonicalRoot = canonicalRoot;
        RelativeName = relativeName;
        FullPath = fullPath;
    }

    public string Code { get; }
    public string Summary { get; }
    public string RequestedRoot { get; }
    public string? CanonicalRoot { get; }
    public string? RelativeName { get; }
    public string? FullPath { get; }
}

public static partial class LocalArtifactSource
{
    /// <summary>
    /// Acquires a bounded, deterministic batch of top-level files from one
    /// explicit local directory.
    /// </summary>
    public static async ValueTask<ArtifactAcquisitionOutcome>
        AcquireDirectoryAsync(
            ArtifactContributionScope scope,
            string path,
            LocalDirectoryArtifactAcquisitionOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DirectoryOptionsSnapshot configured = ValidateAndCopy(
            options ?? new LocalDirectoryArtifactAcquisitionOptions());

        LocalPathClassification root =
            LocalPathAdmission.AdmitDirectory(path, cancellationToken);
        if (root.Outcome != LocalPathOutcome.Classified)
            return ProjectDirectoryRootOutcome(root);

        string canonicalRoot = root.CanonicalPath!;
        List<ObservedDirectoryEntry> observed = [];
        try
        {
            var entries = new FileSystemEnumerable<ObservedDirectoryEntry>(
                canonicalRoot,
                static (ref FileSystemEntry entry) =>
                    new ObservedDirectoryEntry(
                        entry.FileName.ToString(),
                        entry.ToFullPath(),
                        entry.Attributes),
                new EnumerationOptions
                {
                    AttributesToSkip = 0,
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                });
            foreach (ObservedDirectoryEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (observed.Count == configured.MaxObservedEntries)
                {
                    return DirectoryOutcome.Rejected(
                        "local.directory.entry-limit",
                        "The local directory exceeds the configured observed-entry limit.",
                        root);
                }

                observed.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return DirectoryOutcome.Failed(
                "local.directory.enumeration-failed",
                "The local directory could not be enumerated.",
                root);
        }

        observed.Sort(
            static (left, right) =>
                StringComparer.Ordinal.Compare(
                    left.RelativeName,
                    right.RelativeName));

        List<DirectoryArtifactSnapshot> snapshots = [];
        long copiedBytes = 0;
        int selectedFiles = 0;
        foreach (ObservedDirectoryEntry entry in observed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.Directory) != 0
                && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            if (!configured.Includes(entry.RelativeName))
                continue;

            LocalPathClassification classification =
                LocalPathAdmission.Classify(
                    entry.FullPath,
                    cancellationToken);
            if (classification.Outcome == LocalPathOutcome.Classified
                && classification.Kind == LocalPathKind.Directory)
            {
                continue;
            }

            if (classification.Outcome != LocalPathOutcome.Classified)
            {
                return ProjectDirectoryEntryOutcome(
                    root,
                    entry,
                    classification);
            }

            selectedFiles++;
            if (selectedFiles > configured.MaxSelectedFiles)
            {
                return DirectoryOutcome.Rejected(
                    "local.directory.selected-file-limit",
                    "The local directory exceeds the configured selected-file limit.",
                    root,
                    entry);
            }

            await using LocalFileAdmission admission =
                LocalPathAdmission.AdmitRegularFile(
                    classification,
                    cancellationToken);
            if (admission.Classification.Outcome
                != LocalPathOutcome.Classified)
            {
                return ProjectDirectoryEntryOutcome(
                    root,
                    entry,
                    admission.Classification);
            }

            FileStream stream = admission.Stream
                ?? throw new InvalidOperationException(
                    "Successful local-file admission did not return a stream.");
            try
            {
                long remainingTotalBytes =
                    configured.MaxTotalBytes - copiedBytes;
                long observedLength = stream.Length;
                ThrowIfKnownLengthExceedsLimit(
                    observedLength,
                    configured.MaxFileBytes,
                    remainingTotalBytes);
                byte[] snapshot = await ReadDirectorySnapshotAsync(
                        stream,
                        configured.MaxFileBytes,
                        remainingTotalBytes,
                        checked((int)observedLength),
                        cancellationToken)
                    .ConfigureAwait(false);
                DateTime observedLastWriteTimeUtc =
                    File.GetLastWriteTimeUtc(stream.SafeFileHandle);
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(
                    new DirectoryArtifactSnapshot(
                        entry,
                        snapshot,
                        observedLastWriteTimeUtc));
                copiedBytes += snapshot.LongLength;
            }
            catch (LocalDirectoryFileSizeLimitException)
            {
                return DirectoryOutcome.Rejected(
                    "local.directory.file-size-limit",
                    "A selected local-directory file exceeds the configured per-file byte limit.",
                    root,
                    entry);
            }
            catch (LocalDirectoryTotalSizeLimitException)
            {
                return DirectoryOutcome.Rejected(
                    "local.directory.total-size-limit",
                    "The selected local-directory files exceed the configured aggregate byte limit.",
                    root,
                    entry);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                return DirectoryOutcome.Failed(
                    "local.directory.read-failed",
                    "A selected local-directory file could not be read.",
                    root,
                    entry);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ArtifactContribution[] contributions =
            new ArtifactContribution[snapshots.Count];
        for (int index = 0; index < snapshots.Count; index++)
        {
            DirectoryArtifactSnapshot snapshot = snapshots[index];
            var provenance = new LocalDirectoryArtifactProvenance(
                canonicalRoot,
                snapshot.Entry.RelativeName,
                snapshot.Entry.FullPath,
                snapshot.Bytes.LongLength,
                snapshot.ObservedLastWriteTimeUtc);
            contributions[index] = scope.Register(
                provenance,
                () => OpenSnapshot(snapshot.Bytes),
                kind: "local-directory-entry");
        }

        return new ArtifactAcquisitionOutcome.Acquired(
            contributions,
            ArtifactAcquisitionLeases.None);
    }

    internal static ArtifactAcquisitionOutcome ProjectDirectoryRootOutcome(
        LocalPathClassification classification) =>
        classification.Outcome switch
        {
            LocalPathOutcome.Unavailable =>
                DirectoryOutcome.Unavailable(
                    "local.directory.root-missing",
                    "The requested local directory does not exist.",
                    classification),
            LocalPathOutcome.Rejected
                when classification.Reason == LocalPathReason.InvalidPath =>
                DirectoryOutcome.Rejected(
                    "local.directory.root-invalid-path",
                    "The requested local directory path is invalid.",
                    classification),
            LocalPathOutcome.Rejected =>
                DirectoryOutcome.Rejected(
                    "local.directory.root-unsupported",
                    "The requested local path is not a supported directory.",
                    classification),
            LocalPathOutcome.Failed =>
                DirectoryOutcome.Failed(
                    "local.directory.root-admission-failed",
                    "The requested local directory could not be classified.",
                    classification),
            _ => throw new InvalidOperationException(
                "A classified directory must be enumerated."),
        };

    private static ArtifactAcquisitionOutcome ProjectDirectoryEntryOutcome(
        LocalPathClassification root,
        ObservedDirectoryEntry entry,
        LocalPathClassification classification) =>
        classification.Outcome switch
        {
            LocalPathOutcome.Unavailable =>
                DirectoryOutcome.Unavailable(
                    "local.directory.entry-missing",
                    "A selected local-directory entry does not exist.",
                    root,
                    entry),
            LocalPathOutcome.Rejected =>
                DirectoryOutcome.Rejected(
                    "local.directory.entry-unsupported",
                    "A selected local-directory entry is not a supported regular file.",
                    root,
                    entry),
            LocalPathOutcome.Failed =>
                DirectoryOutcome.Failed(
                    "local.directory.entry-admission-failed",
                    "A selected local-directory entry could not be admitted.",
                    root,
                    entry),
            _ => throw new InvalidOperationException(
                "A classified regular file must be copied."),
        };

    private static DirectoryOptionsSnapshot ValidateAndCopy(
        LocalDirectoryArtifactAcquisitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.ExcludedFileNames);
        ArgumentNullException.ThrowIfNull(options.IncludedFileExtensions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxObservedEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxSelectedFiles);
        ValidateByteLimit(options.MaxFileBytes);
        ValidateByteLimit(options.MaxTotalBytes);

        string[] excludedFileNames = [.. options.ExcludedFileNames];
        foreach (string fileName in excludedFileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || Path.IsPathRooted(fileName)
                || fileName is "." or ".."
                || fileName.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new ArgumentException(
                    "Each excluded file name must be one non-rooted name without separators or parent traversal.",
                    nameof(options));
            }
        }

        string[] includedFileExtensions =
            [.. options.IncludedFileExtensions];
        foreach (string extension in includedFileExtensions)
        {
            if (string.IsNullOrWhiteSpace(extension)
                || extension.Length < 2
                || extension[0] != '.'
                || extension.IndexOfAny(
                    [
                        '/',
                        '\\',
                        '*',
                        '?',
                    ]) >= 0
                || !string.Equals(
                    Path.GetExtension($"entry{extension}"),
                    extension,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Each included file extension must be one non-empty extension, not a glob or path.",
                    nameof(options));
            }
        }

        return new DirectoryOptionsSnapshot(
            new HashSet<string>(
                excludedFileNames,
                StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(
                includedFileExtensions,
                StringComparer.OrdinalIgnoreCase),
            options.MaxObservedEntries,
            options.MaxSelectedFiles,
            options.MaxFileBytes,
            options.MaxTotalBytes);
    }

    private static void ValidateByteLimit(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, int.MaxValue);
    }

    private static void ThrowIfKnownLengthExceedsLimit(
        long length,
        long maxFileBytes,
        long remainingTotalBytes)
    {
        if (length <= maxFileBytes && length <= remainingTotalBytes)
            return;
        if (maxFileBytes <= remainingTotalBytes
            && length > maxFileBytes)
        {
            throw new LocalDirectoryFileSizeLimitException();
        }

        throw new LocalDirectoryTotalSizeLimitException();
    }

    private static async ValueTask<byte[]> ReadDirectorySnapshotAsync(
        Stream source,
        long maxFileBytes,
        long remainingTotalBytes,
        int initialCapacity,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream(initialCapacity);
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return destination.ToArray();

            long remainingFileBytes =
                maxFileBytes - destination.Length;
            long remainingBatchBytes =
                remainingTotalBytes - destination.Length;
            if (read > remainingFileBytes
                || read > remainingBatchBytes)
            {
                if (remainingFileBytes <= remainingBatchBytes
                    && read > remainingFileBytes)
                {
                    throw new LocalDirectoryFileSizeLimitException();
                }

                throw new LocalDirectoryTotalSizeLimitException();
            }

            destination.Write(buffer, 0, read);
        }
    }

    private readonly record struct DirectoryOptionsSnapshot(
        HashSet<string> ExcludedFileNames,
        HashSet<string> IncludedFileExtensions,
        int MaxObservedEntries,
        int MaxSelectedFiles,
        long MaxFileBytes,
        long MaxTotalBytes)
    {
        internal bool Includes(string fileName) =>
            !ExcludedFileNames.Contains(fileName)
            && IncludedFileExtensions.Contains(
                Path.GetExtension(fileName));
    }

    private readonly record struct ObservedDirectoryEntry(
        string RelativeName,
        string FullPath,
        FileAttributes Attributes);

    private readonly record struct DirectoryArtifactSnapshot(
        ObservedDirectoryEntry Entry,
        byte[] Bytes,
        DateTime ObservedLastWriteTimeUtc);

    private static class DirectoryOutcome
    {
        internal static ArtifactAcquisitionOutcome Unavailable(
            string code,
            string summary,
            LocalPathClassification root,
            ObservedDirectoryEntry? entry = null) =>
            new ArtifactAcquisitionOutcome.Unavailable(
                Diagnostic(code, summary, root, entry));

        internal static ArtifactAcquisitionOutcome Rejected(
            string code,
            string summary,
            LocalPathClassification root,
            ObservedDirectoryEntry? entry = null) =>
            new ArtifactAcquisitionOutcome.Rejected(
                Diagnostic(code, summary, root, entry));

        internal static ArtifactAcquisitionOutcome Failed(
            string code,
            string summary,
            LocalPathClassification root,
            ObservedDirectoryEntry? entry = null) =>
            new ArtifactAcquisitionOutcome.Failed(
                Diagnostic(code, summary, root, entry));

        private static LocalDirectoryArtifactDiagnostic Diagnostic(
            string code,
            string summary,
            LocalPathClassification root,
            ObservedDirectoryEntry? entry) =>
            new(
                code,
                summary,
                root.RequestedPath,
                root.CanonicalPath,
                entry?.RelativeName,
                entry?.FullPath);
    }

    private sealed class LocalDirectoryFileSizeLimitException :
        IOException;

    private sealed class LocalDirectoryTotalSizeLimitException :
        IOException;
}
