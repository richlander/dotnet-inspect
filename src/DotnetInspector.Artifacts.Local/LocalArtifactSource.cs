namespace DotnetInspector.Artifacts.Local;

public sealed record LocalArtifactAcquisitionOptions
{
    public const long DefaultMaxFileBytes = 512L * 1024 * 1024;

    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxFileBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxFileBytes,
            int.MaxValue);
    }
}

/// <summary>
/// Typed evidence for one explicitly selected local file.
/// </summary>
public sealed record LocalArtifactProvenance :
    IArtifactProvenance
{
    public LocalArtifactProvenance(
        string fullPath,
        long contentLength,
        DateTime observedLastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        FullPath = fullPath;
        ContentLength = contentLength;
        ObservedLastWriteTimeUtc = observedLastWriteTimeUtc;
    }

    public string FullPath { get; }
    public long ContentLength { get; }
    public DateTime ObservedLastWriteTimeUtc { get; }
}

public sealed record LocalArtifactDiagnostic :
    IArtifactAcquisitionDiagnostic
{
    public LocalArtifactDiagnostic(
        string code,
        string summary,
        string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        Code = code;
        Summary = summary;
        FullPath = fullPath;
    }

    public string Code { get; }
    public string Summary { get; }
    public string FullPath { get; }
}

/// <summary>
/// Acquires one explicit local file into adapter-private immutable bytes before
/// registering it with an artifact generation.
/// </summary>
/// <remarks>
/// Source replacement and deletion resistance are gated by
/// <c>LocalArtifactSnapshot_MutationCannotChangeInspectionBytes</c>.
/// Directory acquisition remains unverified.
/// </remarks>
public static class LocalArtifactSource
{
    public static async ValueTask<ArtifactAcquisitionOutcome>
        AcquireFileAsync(
            ArtifactContributionScope scope,
            string path,
            LocalArtifactAcquisitionOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new LocalArtifactAcquisitionOptions();
        options.Validate();

        string fullPath = Path.GetFullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 81920,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
            if (stream.Length > options.MaxFileBytes)
                return RejectedForSize(fullPath);

            byte[] snapshot = await ReadBoundedAsync(
                    stream,
                    options.MaxFileBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            DateTime observedLastWriteTimeUtc =
                File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            cancellationToken.ThrowIfCancellationRequested();
            var provenance = new LocalArtifactProvenance(
                fullPath,
                snapshot.LongLength,
                observedLastWriteTimeUtc);
            ArtifactContribution contribution = scope.Register(
                provenance,
                () => OpenSnapshot(snapshot),
                kind: "local-file");
            return new ArtifactAcquisitionOutcome.Acquired(
                [contribution],
                ArtifactAcquisitionLeases.None);
        }
        catch (LocalArtifactSizeLimitException)
        {
            return RejectedForSize(fullPath);
        }
        catch (FileNotFoundException)
        {
            return Unavailable(fullPath);
        }
        catch (DirectoryNotFoundException)
        {
            return Unavailable(fullPath);
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
            return new ArtifactAcquisitionOutcome.Failed(
                new LocalArtifactDiagnostic(
                    "local.file.read-failed",
                    "The local artifact could not be read.",
                    fullPath));
        }
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        Stream source,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        using var destination = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            if (destination.Length + read > maxFileBytes)
                throw new LocalArtifactSizeLimitException();
            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static ArtifactAcquisitionOutcome RejectedForSize(
        string fullPath) =>
        new ArtifactAcquisitionOutcome.Rejected(
            new LocalArtifactDiagnostic(
                "local.file.size-limit",
                "The local artifact exceeds the configured byte limit.",
                fullPath));

    private static ArtifactAcquisitionOutcome Unavailable(
        string fullPath) =>
        new ArtifactAcquisitionOutcome.Unavailable(
            new LocalArtifactDiagnostic(
                "local.file.missing",
                "The local artifact does not exist.",
                fullPath));

    private static MemoryStream OpenSnapshot(byte[] snapshot) =>
        new(
            snapshot,
            index: 0,
            count: snapshot.Length,
            writable: false,
            publiclyVisible: false);

    private sealed class LocalArtifactSizeLimitException :
        IOException;
}
