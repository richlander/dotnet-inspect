using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum EmbeddedLibraryInspectionOutcome
{
    Available,
    Rejected,
}

public enum EmbeddedLibraryInspectionFailureKind
{
    InvalidDeclaredName,
    EmptyImage,
    ResourceBudget,
    DescriptorUnavailable,
    NotAssembly,
    InvalidImage,
    UnsupportedMetadataFormat,
    InspectionFailed,
    ProjectionTruncated,
}

public sealed record EmbeddedLibraryInspectionFailure(
    EmbeddedLibraryInspectionFailureKind Kind,
    InertString Detail);

public readonly record struct EmbeddedLibraryProvenance(
    string ContentRef,
    string Digest,
    InertString DeclaredName);

public readonly record struct EmbeddedLibraryAssemblyIdentity(
    string Name,
    Version? Version,
    string? Culture,
    string? PublicKeyToken);

public enum EmbeddedLibraryApiSurfaceFailureMechanism
{
    Metadata,
    Relationship,
    Signature,
    TypeSpecification,
}

public readonly record struct EmbeddedLibraryApiSurfaceInspectionFailure(
    string Operation,
    int SubjectToken,
    EmbeddedLibraryApiSurfaceFailureMechanism Mechanism,
    string Kind,
    string Detail,
    EmbeddedLibraryAssemblyIdentity? SubjectAssembly,
    EmbeddedLibraryAssemblyIdentity? DependencyAssembly);

public sealed record EmbeddedLibraryInspectionResult(
    EmbeddedLibraryInspectionOutcome Outcome,
    InertString DeclaredName,
    string Digest,
    long ByteLength,
    EmbeddedLibraryProvenance? Provenance,
    EmbeddedLibraryAssemblyIdentity? Assembly,
    ImmutableArray<ApiAccessibilityBucket> Accessibility,
    ImmutableArray<EmbeddedLibraryApiSurfaceInspectionFailure>
        InspectionFailures,
    EmbeddedLibraryInspectionFailure? Failure,
    bool IsComplete)
{
    public bool IsAvailable =>
        Outcome == EmbeddedLibraryInspectionOutcome.Available
        && Assembly is not null;
}

public readonly record struct EmbeddedLibraryInspectionExecution(
    InspectionEnvelope<EmbeddedLibraryInspectionResult> Inspection,
    ApiSurface? Surface);

/// <summary>
/// Inspects one immutable embedded managed image as a closed-world Library.
/// </summary>
public static class EmbeddedLibraryInspection
{
    public const int DefaultMaximumImageBytes = 32 * 1024 * 1024;

    public static async Task<EmbeddedLibraryInspectionExecution>
        ExecuteAsync(
            string declaredName,
            ImmutableArray<byte> content,
            ApiSurfaceProjectionLimits limits,
            int maximumImageBytes = DefaultMaximumImageBytes,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumImageBytes, 1);

        EmbeddedLibraryPreparation preparation =
            Prepare(
                declaredName,
                content,
                maximumImageBytes,
                cancellationToken);
        if (preparation is EmbeddedLibraryPreparation.Rejected rejected)
            return new(rejected.Inspection, Surface: null);

        var ready = (EmbeddedLibraryPreparation.Ready)preparation;
        InertString safeName = ready.DeclaredName;
        string digest = ready.Digest;
        AssemblyResolutionProvenance provenance = ready.Provenance;
        ResolvedAssemblyReference assembly = ready.Assembly;
        await using var workspace = new InspectionWorkspace();
        RetainedAssemblyContextGroup retained =
            RetainedAssemblyContextGroup.Create(
                workspace,
                [assembly],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = maximumImageBytes,
                },
                cancellationToken);
        if (retained is RetainedAssemblyContextGroup.Rejected imageRejected)
        {
            return new(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    FailureKind(imageRejected.Failure.Kind),
                    imageRejected.Failure.Detail,
                    assembly.Identity),
                Surface: null);
        }

        AssemblyContextGroup group =
            ((RetainedAssemblyContextGroup.Ready)retained).Group;
        AssemblyContextParticipant participant = group.Participants[0];
        AssemblyContextApiSurfaceResult projection =
            AssemblyContextApiSurfaceQuery.ExecuteBoundedResolved(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                limits,
                [participant]);
        if (projection.Truncation is { } truncation)
        {
            return new(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    EmbeddedLibraryInspectionFailureKind.ProjectionTruncated,
                    $"The uploaded Library exceeded the {truncation.Limit} "
                        + $"projection bound of {truncation.Bound}.",
                    assembly.Identity),
                Surface: null);
        }

        AssemblyContextEntry<AssemblyApiSurface> entry =
            projection.Assemblies.Assemblies.Single();
        if (entry is AssemblyContextEntry<AssemblyApiSurface>.Rejected
            participantRejected)
        {
            return new(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    FailureKind(participantRejected.Failure.Kind),
                    participantRejected.Failure.Detail,
                    assembly.Identity),
                Surface: null);
        }
        if (entry is AssemblyContextEntry<AssemblyApiSurface>.Failed failed)
        {
            return new(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    EmbeddedLibraryInspectionFailureKind.InspectionFailed,
                    failed.Error.Message,
                    assembly.Identity),
                Surface: null);
        }

        AssemblyApiSurface available =
            ((AssemblyContextEntry<AssemblyApiSurface>.Available)entry).Value;
        ImmutableArray<EmbeddedLibraryApiSurfaceInspectionFailure>
            inspectionFailures =
                Detach(available.InspectionFailures);
        var result = new EmbeddedLibraryInspectionResult(
            EmbeddedLibraryInspectionOutcome.Available,
            safeName,
            digest,
            content.Length,
            Detach(provenance),
            Detach(assembly.Identity),
            projection.Accessibility,
            inspectionFailures,
            Failure: null,
            IsComplete: available.InspectionFailures.IsEmpty);
        return new(
            new(
                result,
                NonProjectableShare(),
                inspectionFailures.Select(
                    failure => new InspectionDiagnostic(
                        "embedded-library-inspection-incomplete",
                        InspectionDiagnosticSeverity.Warning,
                        $"{failure.Operation}: {failure.Kind}: {failure.Detail}",
                        assembly.Identity.ToString()))),
            available.Surface);
    }

    /// <summary>
    /// Materializes one immutable embedded image as an owned direct Library.
    /// The caller must settle the returned adapter result.
    /// </summary>
    public static async ValueTask<AssemblyContextLibraryAdapterResult>
        MaterializeAsync(
            string declaredName,
            ImmutableArray<byte> content,
            AssemblyContextLibraryRole role,
            AssemblyContextLibraryMaterializationLimits limits,
            int maximumImageBytes = DefaultMaximumImageBytes,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumImageBytes, 1);

        EmbeddedLibraryPreparation preparation =
            Prepare(
                declaredName,
                content,
                maximumImageBytes,
                cancellationToken);
        if (preparation is EmbeddedLibraryPreparation.Rejected rejected)
        {
            EmbeddedLibraryInspectionFailure failure =
                rejected.Inspection.Content.Failure
                ?? throw new InvalidOperationException(
                    "A rejected embedded Library has no failure.");
            throw new InvalidOperationException(
                $"The uploaded Library could not be materialized "
                    + $"({failure.Kind}): {failure.Detail}");
        }

        ResolvedAssemblyReference assembly =
            ((EmbeddedLibraryPreparation.Ready)preparation).Assembly;
        await using var workspace = new InspectionWorkspace();
        RetainedAssemblyContextGroup retained =
            RetainedAssemblyContextGroup.Create(
                workspace,
                [assembly],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = maximumImageBytes,
                },
                cancellationToken);
        if (retained is RetainedAssemblyContextGroup.Rejected imageRejected)
        {
            throw new InvalidOperationException(
                "The uploaded Library image could not be retained "
                    + $"({FailureKind(imageRejected.Failure.Kind)}): "
                    + imageRejected.Failure.Detail);
        }

        AssemblyContextGroup group =
            ((RetainedAssemblyContextGroup.Ready)retained).Group;
        return await AssemblyContextLibraryAdapter.MaterializeAsync(
                group,
                group.Participants[0],
                role,
                limits,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static EmbeddedLibraryPreparation Prepare(
        string declaredName,
        ImmutableArray<byte> content,
        int maximumImageBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(declaredName))
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    InertString.Empty,
                    content.IsDefault ? 0 : content.Length,
                    digest: "",
                    EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName,
                    "The uploaded image has no declared file name."));
        }

        var safeName = new InertString(
            TextPolicy.Field,
            declaredName,
            maxLength: 260);
        if (safeName.IsTruncated)
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.IsDefault ? 0 : content.Length,
                    digest: "",
                    EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName,
                    "The uploaded image file name exceeds the 260-character display limit."));
        }
        if (content.IsDefaultOrEmpty)
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.IsDefault ? 0 : content.Length,
                    digest: "",
                    EmbeddedLibraryInspectionFailureKind.EmptyImage,
                    "The uploaded image is empty."));
        }
        if (content.Length > maximumImageBytes)
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.Length,
                    digest: "",
                    EmbeddedLibraryInspectionFailureKind.ResourceBudget,
                    $"The uploaded image exceeds the {maximumImageBytes}-byte limit."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = ImmutableCollectionsMarshal.AsArray(content)!;
        string digest = Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();
        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Embedded(
                "browser-upload",
                $"sha256:{digest}",
                safeName.ToString());
        if (UsesUnsupportedMetadataFormat(bytes))
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    EmbeddedLibraryInspectionFailureKind.UnsupportedMetadataFormat,
                    "The uploaded image uses unsupported Windows Metadata."));
        }

        AssemblyDescriptorSelectionResult selection =
            ResolvedAssemblyReference.SelectFromStream(
                () => new MemoryStream(bytes, writable: false),
                provenance,
                lastWriteTimeUtc: null,
                assetFileName: safeName.ToString());
        if (selection is AssemblyDescriptorSelectionResult.Descriptorless)
        {
            bool isModule = IsMetadataModule(bytes);
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    isModule
                        ? EmbeddedLibraryInspectionFailureKind.NotAssembly
                        : EmbeddedLibraryInspectionFailureKind.DescriptorUnavailable,
                    isModule
                        ? "The uploaded managed image is a module, not an assembly."
                        : "The uploaded file is not a managed assembly."));
        }
        if (selection is AssemblyDescriptorSelectionResult.Rejected rejected)
        {
            return new EmbeddedLibraryPreparation.Rejected(
                Rejected(
                    safeName,
                    content.Length,
                    digest,
                    FailureKind(rejected.Failure.Kind),
                    rejected.Failure.Detail));
        }

        return new EmbeddedLibraryPreparation.Ready(
            safeName,
            digest,
            provenance,
            ((AssemblyDescriptorSelectionResult.Ready)selection).Reference);
    }

    private abstract record EmbeddedLibraryPreparation
    {
        private EmbeddedLibraryPreparation()
        {
        }

        public sealed record Ready(
            InertString DeclaredName,
            string Digest,
            AssemblyResolutionProvenance Provenance,
            ResolvedAssemblyReference Assembly)
            : EmbeddedLibraryPreparation;

        public sealed record Rejected(
            InspectionEnvelope<EmbeddedLibraryInspectionResult> Inspection)
            : EmbeddedLibraryPreparation;
    }

    private static InspectionEnvelope<EmbeddedLibraryInspectionResult>
        Rejected(
            InertString declaredName,
            long byteLength,
            string digest,
            EmbeddedLibraryInspectionFailureKind kind,
            string detail,
            AssemblyReferenceIdentity? assembly = null)
    {
        var failure = new EmbeddedLibraryInspectionFailure(
            kind,
            new InertString(TextPolicy.Field, detail));
        return new(
            new EmbeddedLibraryInspectionResult(
                EmbeddedLibraryInspectionOutcome.Rejected,
                declaredName,
                digest,
                byteLength,
                Provenance: string.IsNullOrEmpty(digest)
                    ? null
                    : new EmbeddedLibraryProvenance(
                        "browser-upload",
                        $"sha256:{digest}",
                        declaredName),
                assembly is null ? null : Detach(assembly),
                Accessibility: [],
                InspectionFailures: [],
                failure,
                IsComplete: false),
            NonProjectableShare(),
            [
                new InspectionDiagnostic(
                    "embedded-library-rejected",
                    InspectionDiagnosticSeverity.Error,
                    detail,
                    assembly?.ToString()),
            ]);
    }

    static EmbeddedLibraryProvenance Detach(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(
                    embedded.ContentRef,
                    embedded.Digest,
                    new InertString(TextPolicy.Field, embedded.DeclaredName)),
            _ => throw new InvalidOperationException(
                "Embedded Library provenance must remain Embedded."),
        };

    static EmbeddedLibraryAssemblyIdentity Detach(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version,
            identity.Culture,
            identity.PublicKeyToken);

    static ImmutableArray<EmbeddedLibraryApiSurfaceInspectionFailure>
        Detach(ImmutableArray<ApiSurfaceInspectionFailure> failures) =>
        failures.IsEmpty
            ? []
            : [.. failures.Select(Detach)];

    static EmbeddedLibraryApiSurfaceInspectionFailure Detach(
        ApiSurfaceInspectionFailure failure) =>
        new(
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism switch
            {
                MetadataTypeNameFailureMechanism.Metadata =>
                    EmbeddedLibraryApiSurfaceFailureMechanism.Metadata,
                MetadataTypeNameFailureMechanism.Relationship =>
                    EmbeddedLibraryApiSurfaceFailureMechanism.Relationship,
                MetadataTypeNameFailureMechanism.Signature =>
                    EmbeddedLibraryApiSurfaceFailureMechanism.Signature,
                MetadataTypeNameFailureMechanism.TypeSpecification =>
                    EmbeddedLibraryApiSurfaceFailureMechanism.TypeSpecification,
                _ => throw new InvalidOperationException(
                    "Unknown embedded Library API-surface failure mechanism."),
            },
            failure.Kind,
            failure.Detail,
            failure.SubjectAssembly is null
                ? null
                : Detach(failure.SubjectAssembly),
            failure.DependencyAssembly is null
                ? null
                : Detach(failure.DependencyAssembly));

    private static bool IsMetadataModule(byte[] bytes)
    {
        try
        {
            using var peReader = new PEReader(
                new MemoryStream(bytes, writable: false));
            return peReader.HasMetadata
                && !peReader.GetMetadataReader().IsAssembly;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static bool UsesUnsupportedMetadataFormat(byte[] bytes)
    {
        try
        {
            using var peReader = new PEReader(
                new MemoryStream(bytes, writable: false));
            _ = MetadataFormatAdmission.AdmitImage(peReader);
            return false;
        }
        catch (UnsupportedMetadataFormatException)
        {
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static EmbeddedLibraryInspectionFailureKind FailureKind(
        CandidateOpenFailureKind kind) =>
        kind switch
        {
            CandidateOpenFailureKind.ResourceBudget =>
                EmbeddedLibraryInspectionFailureKind.ResourceBudget,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                EmbeddedLibraryInspectionFailureKind.UnsupportedMetadataFormat,
            CandidateOpenFailureKind.InvalidImage =>
                EmbeddedLibraryInspectionFailureKind.InvalidImage,
            CandidateOpenFailureKind.Unreadable =>
                EmbeddedLibraryInspectionFailureKind.InspectionFailed,
            _ => throw new InvalidOperationException(
                $"Unknown candidate-open failure kind '{kind}'."),
        };

    private static InspectionShare NonProjectableShare() =>
        new InspectionShare.NonProjectable(
            "embedded-library/share",
            "Uploaded Library bytes are session-local and cannot be restored.");
}
