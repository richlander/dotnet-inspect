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
        : this(code, summary, fullPath, fullPath)
    {
    }

    public LocalArtifactDiagnostic(
        string code,
        string summary,
        string requestedPath,
        string? canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        Code = code;
        Summary = summary;
        RequestedPath = requestedPath;
        CanonicalPath = canonicalPath;
    }

    public string Code { get; }
    public string Summary { get; }
    public string RequestedPath { get; }
    public string? CanonicalPath { get; }
    public string FullPath => CanonicalPath ?? RequestedPath;
}

/// <summary>
/// Acquires one explicit local file into adapter-private immutable bytes before
/// registering it with an artifact generation.
/// </summary>
/// <remarks>
/// Source replacement and deletion resistance are gated by
/// <c>LocalArtifactSnapshot_MutationCannotChangeInspectionBytes</c> and
/// <c>LocalPathAdmission_ConsumerReceivesTheVerifiedOpenGeneration</c>.
/// Pre-open rejection of stable non-regular entries is gated by
/// <c>LocalPathAdmission_StableNonRegularEntriesRejectBeforeOpen</c>.
/// Bounded directory acquisition is gated by the three
/// <c>LocalDirectoryAcquisition_*</c> tests.
/// </remarks>
public static partial class LocalArtifactSource
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

        await using LocalFileAdmission admission =
            LocalPathAdmission.AdmitRegularFile(path, cancellationToken);
        LocalPathClassification classification = admission.Classification;
        if (classification.Outcome != LocalPathOutcome.Classified)
            return ProjectAdmissionOutcome(classification);

        FileStream stream = admission.Stream
            ?? throw new InvalidOperationException(
                "Successful local-file admission did not return a stream.");
        string fullPath = classification.CanonicalPath!;
        try
        {
            if (stream.Length > options.MaxFileBytes)
                return RejectedForSize(classification);

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
            return RejectedForSize(classification);
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
                    classification.RequestedPath,
                    classification.CanonicalPath));
        }
    }

    internal static ArtifactAcquisitionOutcome ProjectAdmissionOutcome(
        LocalPathClassification classification)
    {
        LocalArtifactDiagnostic Diagnostic(string code, string summary) =>
            new(
                code,
                summary,
                classification.RequestedPath,
                classification.CanonicalPath);

        return classification.Outcome switch
        {
            LocalPathOutcome.Unavailable =>
                new ArtifactAcquisitionOutcome.Unavailable(
                    Diagnostic(
                        "local.file.missing",
                        "The local artifact does not exist.")),
            LocalPathOutcome.Rejected
                when classification.Reason == LocalPathReason.InvalidPath =>
                new ArtifactAcquisitionOutcome.Rejected(
                    Diagnostic(
                        "local.file.invalid-path",
                        "The requested local file path is invalid.")),
            LocalPathOutcome.Rejected =>
                new ArtifactAcquisitionOutcome.Rejected(
                    Diagnostic(
                        "local.file.unsupported-entry",
                        "The requested local path is not a supported regular file.")),
            LocalPathOutcome.Failed
                when classification.Reason
                    == LocalPathReason.ClassificationUnsupported =>
                new ArtifactAcquisitionOutcome.Failed(
                    Diagnostic(
                        "local.file.classification-unsupported",
                        "Local path classification is unavailable on this host.")),
            LocalPathOutcome.Failed =>
                new ArtifactAcquisitionOutcome.Failed(
                    Diagnostic(
                        "local.file.read-failed",
                        "The local artifact could not be read.")),
            _ => throw new InvalidOperationException(
                "A classified local path must be consumed by admission."),
        };
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
        LocalPathClassification classification) =>
        new ArtifactAcquisitionOutcome.Rejected(
            new LocalArtifactDiagnostic(
                "local.file.size-limit",
                "The local artifact exceeds the configured byte limit.",
                classification.RequestedPath,
                classification.CanonicalPath));

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
