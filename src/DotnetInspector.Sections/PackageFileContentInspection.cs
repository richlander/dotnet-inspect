using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<PackageFileContentStatus>))]
public enum PackageFileContentStatus
{
    Completed,
    Unavailable,
    Failed,
}

public sealed record PackageFileContentDocument
{
    private PackageFileContentDocument(
        PackageFileContentStatus status,
        string? packageId,
        string? version,
        string? path,
        long size,
        ImmutableArray<byte> content,
        InertString? detail)
    {
        Status = status;
        PackageId = packageId;
        Version = version;
        Path = path;
        Size = size;
        Content = content.IsDefault ? [] : content;
        Detail = detail;
    }

    public PackageFileContentStatus Status { get; }

    public string? PackageId { get; }

    public string? Version { get; }

    public string? Path { get; }

    public long Size { get; }

    public ImmutableArray<byte> Content { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public static PackageFileContentDocument Completed(
        string packageId,
        string version,
        string path,
        ImmutableArray<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (content.IsDefault)
            throw new ArgumentException(
                "Completed package file content must be initialized.",
                nameof(content));

        return new(
            PackageFileContentStatus.Completed,
            packageId,
            version,
            path,
            content.Length,
            content,
            detail: null);
    }

    public static PackageFileContentDocument Failed(
        PackageFileContentStatus status,
        string detail)
    {
        if (status == PackageFileContentStatus.Completed)
        {
            throw new ArgumentException(
                "A failed package file cannot have completed status.",
                nameof(status));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new(
            status,
            packageId: null,
            version: null,
            path: null,
            size: 0,
            content: [],
            new InertString(TextPolicy.Field, detail));
    }
}

public sealed record PackageFileContentInspectionRequest
{
    public PackageFileContentInspectionRequest(
        InspectionWorkspace workspace,
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageFileAcquisitionResult.Acquired file,
        long maxBytes)
    {
        Workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
        Occurrence = occurrence
            ?? throw new ArgumentNullException(nameof(occurrence));
        File = file
            ?? throw new ArgumentNullException(nameof(file));
        if (!occurrence.Occurrence.Package.HasContentGeneration(
                file.Settlement.Payload.Content.GenerationIdentity))
        {
            throw new ArgumentException(
                "The package file must belong to the admitted Root's content generation.",
                nameof(file));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxBytes,
            int.MaxValue);
        MaxBytes = maxBytes;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }

    public PackageFileAcquisitionResult.Acquired File { get; }

    public long MaxBytes { get; }
}

public enum PackageFileContentQueryAdmission
{
    NotReady,
    Admitted,
    Rejected,
}

public sealed record PackageFileContentEvidence(
    PackageAcquisitionEvidenceEntry Acquisition,
    PackageFileContentQueryAdmission QueryAdmission,
    ArtifactRootFailure? QueryFailure,
    PackageCompileAssetSelectionStatus? LibrarySelection,
    int? SurfaceLibraryCount,
    int? ImplementationLibraryCount);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageFileContentDocument))]
[JsonSerializable(typeof(PackageFileContentEvidence))]
public partial class PackageFileContentInspectionJsonContext
    : JsonSerializerContext;

public static class PackageFileContentInspection
{
    private const string SharePath = "package-file-content/share";

    public static async ValueTask<
        InspectionEnvelope<PackageFileContentDocument>>
        ExecuteAsync(
            PackageFileContentInspectionRequest request,
            CancellationToken cancellationToken = default)
    {
        var result = await ExecuteCoreAsync(
                request,
                captureEvidence: false,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Inspection;
    }

    public static async ValueTask<
        EvidenceInspectionEnvelope<
            PackageFileContentDocument,
            PackageFileContentEvidence>>
        ExecuteWithEvidenceAsync(
            PackageFileContentInspectionRequest request,
            CancellationToken cancellationToken = default)
    {
        var result = await ExecuteCoreAsync(
                request,
                captureEvidence: true,
                cancellationToken)
            .ConfigureAwait(false);
        return new(result.Inspection, result.Evidence!);
    }

    private static async ValueTask<(
        InspectionEnvelope<PackageFileContentDocument> Inspection,
        PackageFileContentEvidence? Evidence)>
        ExecuteCoreAsync(
            PackageFileContentInspectionRequest request,
            bool captureEvidence,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PackageFileContentEvidence? evidence = null;
        if (request.Occurrence.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || request.Occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            return (
                Failed(
                    PackageFileContentStatus.Unavailable,
                    "package-file-content.root-unavailable",
                    "The package Root is not ready for content inspection."),
                captureEvidence
                    ? Evidence(
                        PackageFileContentQueryAdmission.NotReady)
                    : null);
        }

        ArtifactRootResult<InspectionEnvelope<PackageFileContentDocument>>
            query = await request.Workspace.ExecutePackageRootQueryAsync(
                correspondence,
                ready.Generation,
                (realization, token) =>
                {
                    if (captureEvidence)
                    {
                        evidence = Evidence(
                            PackageFileContentQueryAdmission.Admitted,
                            surfaceLibraries:
                                realization.SurfaceParticipants.Length,
                            implementationLibraries:
                                realization.ImplementationParticipants.Length);
                    }
                    return ReadAsync(request, token);
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (query is ArtifactRootResult<
                InspectionEnvelope<PackageFileContentDocument>>
                    .Available available)
        {
            return (available.Value, evidence);
        }

        var rejected = (ArtifactRootResult<
            InspectionEnvelope<PackageFileContentDocument>>.Rejected)query;
        return (
            Failed(
                PackageFileContentStatus.Unavailable,
                "package-file-content.query-rejected",
                $"The package Root query was rejected: {rejected.Failure}."),
            captureEvidence
                ? Evidence(
                    PackageFileContentQueryAdmission.Rejected,
                    rejected.Failure)
                : null);

        PackageFileContentEvidence Evidence(
            PackageFileContentQueryAdmission admission,
            ArtifactRootFailure? failure = null,
            int? surfaceLibraries = null,
            int? implementationLibraries = null) =>
            new(
                PackageAcquisitionEvidenceEntry.From(
                    request.File.FileList.Narrowing.Acquisition),
                admission,
                failure,
                request.Occurrence.Occurrence.Package.SelectionStatus,
                surfaceLibraries,
                implementationLibraries);
    }

    private static async ValueTask<
        InspectionEnvelope<PackageFileContentDocument>> ReadAsync(
            PackageFileContentInspectionRequest request,
            CancellationToken cancellationToken)
    {
        PackageContentEntry entry = request.File.Entry;
        if (entry.Length < 0
            || entry.Length > request.MaxBytes)
        {
            return Failed(
                PackageFileContentStatus.Unavailable,
                "package-file-content.length-unavailable",
                $"Package entry '{entry.Path}' exceeds the detached content "
                    + $"limit of {request.MaxBytes} bytes.");
        }

        try
        {
            await using PackageHousePayloadRead input =
                request.File.OpenRead();
            byte[] content = GC.AllocateUninitializedArray<byte>(
                checked((int)entry.Length));
            int offset = 0;
            while (offset < content.Length)
            {
                int read = await input.ReadAsync(
                        content.AsMemory(offset),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Package entry '{entry.Path}' ended before its declared length.");
                offset += read;
            }

            byte[] extra = new byte[1];
            if (await input.ReadAsync(extra, cancellationToken)
                    .ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException(
                    $"Package entry '{entry.Path}' exceeded its declared length.");
            }

            PackageSourceCoordinate coordinate =
                request.File.Settlement.Payload.Coordinate;
            var file = PackageFileContentDocument.Completed(
                coordinate.PackageId,
                coordinate.Version,
                entry.Path,
                ImmutableCollectionsMarshal.AsImmutableArray(content));
            return new(
                file,
                new InspectionShare.NonProjectable(
                    SharePath,
                    "Package file content sharing is not yet available."));
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or NotSupportedException
                or PackageEntryNotMaterializedException)
        {
            return Failed(
                PackageFileContentStatus.Failed,
                "package-file-content.read-failed",
                exception.Message);
        }
    }

    private static InspectionEnvelope<PackageFileContentDocument> Failed(
        PackageFileContentStatus status,
        string code,
        string detail) =>
        new(
            PackageFileContentDocument.Failed(status, detail),
            new InspectionShare.NonProjectable(
                SharePath,
                "Package file content sharing is not yet available."),
            [
                new InspectionDiagnostic(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);
}
