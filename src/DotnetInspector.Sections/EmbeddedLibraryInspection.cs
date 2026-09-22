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

public sealed record EmbeddedLibraryInspectionResult(
    EmbeddedLibraryInspectionOutcome Outcome,
    InertString DeclaredName,
    string Digest,
    long ByteLength,
    AssemblyResolutionProvenance? Provenance,
    AssemblyReferenceIdentity? Assembly,
    ApiSurface? Surface,
    ImmutableArray<ApiAccessibilityBucket> Accessibility,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures,
    EmbeddedLibraryInspectionFailure? Failure,
    bool IsComplete)
{
    public bool IsAvailable =>
        Outcome == EmbeddedLibraryInspectionOutcome.Available
        && Assembly is not null
        && Surface is not null;
}

/// <summary>
/// Inspects one immutable embedded managed image as a closed-world Library.
/// </summary>
public static class EmbeddedLibraryInspection
{
    public const int DefaultMaximumImageBytes = 32 * 1024 * 1024;

    public static async Task<InspectionEnvelope<EmbeddedLibraryInspectionResult>>
        ExecuteAsync(
            string declaredName,
            ImmutableArray<byte> content,
            ApiSurfaceProjectionLimits limits,
            int maximumImageBytes = DefaultMaximumImageBytes,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumImageBytes, 1);

        if (string.IsNullOrWhiteSpace(declaredName))
        {
            return Rejected(
                InertString.Empty,
                content.IsDefault ? 0 : content.Length,
                digest: "",
                EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName,
                "The uploaded image has no declared file name.");
        }

        var safeName = new InertString(
            TextPolicy.Field,
            declaredName,
            maxLength: 260);
        if (safeName.IsTruncated)
        {
            return Rejected(
                safeName,
                content.IsDefault ? 0 : content.Length,
                digest: "",
                EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName,
                "The uploaded image file name exceeds the 260-character display limit.");
        }
        if (content.IsDefaultOrEmpty)
        {
            return Rejected(
                safeName,
                content.IsDefault ? 0 : content.Length,
                digest: "",
                EmbeddedLibraryInspectionFailureKind.EmptyImage,
                "The uploaded image is empty.");
        }
        if (content.Length > maximumImageBytes)
        {
            return Rejected(
                safeName,
                content.Length,
                digest: "",
                EmbeddedLibraryInspectionFailureKind.ResourceBudget,
                $"The uploaded image exceeds the {maximumImageBytes}-byte limit.");
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
            return Rejected(
                safeName,
                content.Length,
                digest,
                EmbeddedLibraryInspectionFailureKind.UnsupportedMetadataFormat,
                "The uploaded image uses unsupported Windows Metadata.");
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
            return Rejected(
                safeName,
                content.Length,
                digest,
                isModule
                    ? EmbeddedLibraryInspectionFailureKind.NotAssembly
                    : EmbeddedLibraryInspectionFailureKind.DescriptorUnavailable,
                isModule
                    ? "The uploaded managed image is a module, not an assembly."
                    : "The uploaded file is not a managed assembly.");
        }
        if (selection is AssemblyDescriptorSelectionResult.Rejected rejected)
        {
            return Rejected(
                safeName,
                content.Length,
                digest,
                FailureKind(rejected.Failure.Kind),
                rejected.Failure.Detail);
        }

        ResolvedAssemblyReference assembly =
            ((AssemblyDescriptorSelectionResult.Ready)selection).Reference;
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
            return Rejected(
                safeName,
                content.Length,
                digest,
                FailureKind(imageRejected.Failure.Kind),
                imageRejected.Failure.Detail,
                assembly.Identity);
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
            return Rejected(
                safeName,
                content.Length,
                digest,
                EmbeddedLibraryInspectionFailureKind.ProjectionTruncated,
                $"The uploaded Library exceeded the {truncation.Limit} "
                    + $"projection bound of {truncation.Bound}.",
                assembly.Identity);
        }

        AssemblyContextEntry<AssemblyApiSurface> entry =
            projection.Assemblies.Assemblies.Single();
        if (entry is AssemblyContextEntry<AssemblyApiSurface>.Rejected
            participantRejected)
        {
            return Rejected(
                safeName,
                content.Length,
                digest,
                FailureKind(participantRejected.Failure.Kind),
                participantRejected.Failure.Detail,
                assembly.Identity);
        }
        if (entry is AssemblyContextEntry<AssemblyApiSurface>.Failed failed)
        {
            return Rejected(
                safeName,
                content.Length,
                digest,
                EmbeddedLibraryInspectionFailureKind.InspectionFailed,
                failed.Error.Message,
                assembly.Identity);
        }

        AssemblyApiSurface available =
            ((AssemblyContextEntry<AssemblyApiSurface>.Available)entry).Value;
        var result = new EmbeddedLibraryInspectionResult(
            EmbeddedLibraryInspectionOutcome.Available,
            safeName,
            digest,
            content.Length,
            provenance,
            assembly.Identity,
            available.Surface,
            projection.Accessibility,
            available.InspectionFailures,
            Failure: null,
            IsComplete: available.InspectionFailures.IsEmpty);
        return new(
            result,
            NonProjectableShare(),
            available.InspectionFailures.Select(
                failure => new InspectionDiagnostic(
                    "embedded-library-inspection-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    $"{failure.Operation}: {failure.Kind}: {failure.Detail}",
                    assembly.Identity.ToString())));
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
                    : AssemblyResolutionProvenance.Embedded(
                        "browser-upload",
                        $"sha256:{digest}",
                        declaredName.ToString()),
                assembly,
                Surface: null,
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
