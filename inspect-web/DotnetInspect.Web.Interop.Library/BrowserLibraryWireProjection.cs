using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;
using ILInspector.Metadata;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Library;

[SupportedOSPlatform("browser")]
internal static class BrowserLibraryWireProjection
{
    internal static BrowserUploadedLibraryInspection Project(
        InspectionEnvelope<EmbeddedLibraryInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        EmbeddedLibraryInspectionResult content = inspection.Content;
        BrowserUploadedLibraryInspectionOutcome outcome =
            content.Outcome switch
            {
                EmbeddedLibraryInspectionOutcome.Available =>
                    BrowserUploadedLibraryInspectionOutcome.Available,
                EmbeddedLibraryInspectionOutcome.Rejected =>
                    BrowserUploadedLibraryInspectionOutcome.Rejected,
                _ => throw new InvalidOperationException(
                    "Unknown embedded Library inspection outcome."),
            };
        BrowserUploadedLibraryFailure? failure =
            content.Failure is null ? null : Project(content.Failure);
        BrowserLibraryInspectionDiagnostic[] diagnostics =
            [.. inspection.Diagnostics.Select(Project)];
        bool isComplete = content.IsComplete;
        BrowserUploadedLibrarySurface? surface = null;
        if (content.IsAvailable)
        {
            BrowserSurfaceProjection.Surface projected =
                BrowserSurfaceProjection.Project(
                    content.Surface!,
                    content.Accessibility,
                    content.Assembly!,
                    content.DeclaredName.ToString(),
                    $"sha256:{content.Digest}",
                    content.DeclaredName.ToString(),
                    content.InspectionFailures);
            if (projected.IsTruncated)
            {
                string detail = projected.InspectionError
                    ?? "The Browser API-surface projection exceeded its transport bound.";
                outcome = BrowserUploadedLibraryInspectionOutcome.Rejected;
                failure = new(
                    BrowserUploadedLibraryFailureKind.ProjectionTruncated,
                    detail);
                diagnostics =
                [
                    .. diagnostics,
                    new(
                        "embedded-library.browser-projection-truncated",
                        "Error",
                        detail,
                        content.DeclaredName.ToString()),
                ];
                isComplete = false;
            }
            else
            {
                surface = Project(projected);
            }
        }

        return AdmitTransport(new(
            new BrowserUploadedLibraryResult(
                outcome,
                content.DeclaredName.ToString(),
                content.Digest,
                content.ByteLength,
                Project(content.Provenance),
                Project(content.Assembly),
                surface,
                [.. content.InspectionFailures.Select(Project)],
                failure,
                isComplete),
            Project(inspection.Share),
            diagnostics));
    }

    static BrowserUploadedLibraryInspection AdmitTransport(
        BrowserUploadedLibraryInspection inspection)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(
            inspection,
            BrowserLibraryJsonContext.Default.BrowserUploadedLibraryInspection);
        long collectionEntries =
            BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead
            + BrowserOrdinaryWorkerJsonBudget.CollectionEntries(document.RootElement);
        if (collectionEntries
            > BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerCollectionEntries)
        {
            return TransportRejected(
                inspection,
                "collection-entry",
                BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerCollectionEntries,
                collectionEntries);
        }

        long transportedCharacters =
            BrowserOrdinaryWorkerJsonBudget.JsonStringifyCharacters(document.RootElement)
            + BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead;
        if (transportedCharacters
            > BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerJsonCharacters)
        {
            return TransportRejected(
                inspection,
                "serialized-character",
                BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerJsonCharacters,
                transportedCharacters);
        }

        return inspection;
    }

    static BrowserUploadedLibraryInspection TransportRejected(
        BrowserUploadedLibraryInspection inspection,
        string limit,
        long bound,
        long observed)
    {
        string detail =
            $"The Browser Library result transport truncated at the ordinary Worker "
            + $"{limit} limit ({bound}): observed {observed}.";
        BrowserUploadedLibraryResult content = inspection.Content;
        var rejection = new BrowserUploadedLibraryInspection(
            content with
            {
                Outcome = BrowserUploadedLibraryInspectionOutcome.Rejected,
                Surface = null,
                InspectionFailures = [],
                Failure = new(
                    BrowserUploadedLibraryFailureKind.ProjectionTruncated,
                    detail),
                IsComplete = false,
            },
            inspection.Share,
            [
                new(
                    "embedded-library.browser-projection-truncated",
                    "Error",
                    detail,
                    content.DeclaredName),
            ]);

        using JsonDocument document = JsonSerializer.SerializeToDocument(
            rejection,
            BrowserLibraryJsonContext.Default.BrowserUploadedLibraryInspection);
        if (BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead
                + BrowserOrdinaryWorkerJsonBudget.CollectionEntries(document.RootElement)
                > BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerCollectionEntries
            || BrowserOrdinaryWorkerJsonBudget.JsonStringifyCharacters(
                document.RootElement)
                + BrowserOrdinaryWorkerJsonBudget.OrdinaryWorkerResultTupleOverhead
                > BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerJsonCharacters)
        {
            throw new InvalidOperationException(
                "The bounded uploaded-Library transport rejection exceeds "
                    + "the ordinary Worker admission limits.");
        }

        return rejection;
    }

    static BrowserUploadedLibraryFailure Project(
        EmbeddedLibraryInspectionFailure failure) =>
        new(
            failure.Kind switch
            {
                EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName =>
                    BrowserUploadedLibraryFailureKind.InvalidDeclaredName,
                EmbeddedLibraryInspectionFailureKind.EmptyImage =>
                    BrowserUploadedLibraryFailureKind.EmptyImage,
                EmbeddedLibraryInspectionFailureKind.ResourceBudget =>
                    BrowserUploadedLibraryFailureKind.ResourceBudget,
                EmbeddedLibraryInspectionFailureKind.DescriptorUnavailable =>
                    BrowserUploadedLibraryFailureKind.DescriptorUnavailable,
                EmbeddedLibraryInspectionFailureKind.NotAssembly =>
                    BrowserUploadedLibraryFailureKind.NotAssembly,
                EmbeddedLibraryInspectionFailureKind.InvalidImage =>
                    BrowserUploadedLibraryFailureKind.InvalidImage,
                EmbeddedLibraryInspectionFailureKind.UnsupportedMetadataFormat =>
                    BrowserUploadedLibraryFailureKind.UnsupportedMetadataFormat,
                EmbeddedLibraryInspectionFailureKind.InspectionFailed =>
                    BrowserUploadedLibraryFailureKind.InspectionFailed,
                EmbeddedLibraryInspectionFailureKind.ProjectionTruncated =>
                    BrowserUploadedLibraryFailureKind.ProjectionTruncated,
                _ => throw new InvalidOperationException(
                    "Unknown embedded Library failure kind."),
            },
            failure.Detail.ToString());

    static BrowserEmbeddedLibraryProvenance? Project(
        AssemblyResolutionProvenance? provenance) =>
        provenance switch
        {
            null => null,
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName),
            _ => throw new InvalidOperationException(
                "Uploaded Library provenance must be Embedded."),
        };

    static BrowserLibraryAssemblyReference? Project(
        AssemblyReferenceIdentity? assembly) =>
        assembly is null
            ? null
            : new(
                assembly.Name,
                assembly.Version?.ToString() ?? "",
                assembly.Culture,
                assembly.PublicKeyToken);

    static BrowserLibraryInspectionFailure Project(
        ApiSurfaceInspectionFailure failure) =>
        new(
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism.ToString(),
            failure.Kind,
            failure.Detail,
            Project(failure.SubjectAssembly),
            Project(failure.DependencyAssembly));

    static BrowserLibraryInspectionShare Project(InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                new(
                    BrowserLibraryInspectionShareKind.Available,
                    available.FullUrl,
                    available.Packet,
                    Path: null,
                    Reason: null),
            InspectionShare.NonProjectable nonProjectable =>
                new(
                    BrowserLibraryInspectionShareKind.NonProjectable,
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown inspection Share outcome."),
        };

    static BrowserLibraryInspectionDiagnostic Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    static BrowserUploadedLibrarySurface Project(
        BrowserSurfaceProjection.Surface surface) =>
        new(
            [.. surface.Assemblies.Select(Project)],
            [.. surface.Types.Select(Project)],
            [.. surface.Accessibility.Select(Project)],
            surface.TotalMembers,
            surface.InspectionErrors,
            surface.InspectionError,
            surface.IsTruncated);

    static BrowserLibraryAssemblySurface Project(
        BrowserAssemblySurfaceInfo assembly) =>
        new(
            assembly.Id,
            assembly.Name,
            assembly.Version,
            assembly.Culture,
            assembly.PublicKeyToken,
            assembly.Asset,
            assembly.PublicTypes,
            assembly.PublicMembers,
            assembly.PlatformPack);

    static BrowserLibraryAccessibilityDescriptor Project(
        BrowserAccessibilityInfo accessibility) =>
        new(
            accessibility.Id,
            accessibility.Label,
            accessibility.Order,
            accessibility.IsDefault,
            accessibility.Count);

    static BrowserLibraryTypeSurface Project(BrowserTypeSurfaceInfo type) =>
        new(
            type.Id,
            type.DefinitionId,
            type.QueryId,
            type.MetadataId,
            type.Name,
            type.DisplayName,
            type.Namespace,
            type.Kind,
            type.Accessibility,
            type.AccessibilityId,
            type.Assembly,
            type.AssemblyId,
            type.AssemblyName,
            type.Members,
            type.Signature,
            [.. type.Api.Select(Project)],
            type.PlatformPack);

    static BrowserLibraryMemberSurface Project(BrowserMemberSurfaceInfo member) =>
        new(
            member.Name,
            member.Kind,
            member.Signature,
            member.Accessibility,
            member.IsStatic,
            member.IsUnsafe,
            member.IsVirtual,
            member.IsAbstract,
            member.IsOverride,
            member.IsExtension,
            member.IsObsolete,
            member.GenericArity,
            member.MetadataToken,
            member.DeclarationMetadataToken,
            member.ReturnType,
            [
                .. member.Parameters.Select(parameter =>
                    new BrowserLibraryParameterSurface(
                        parameter.Name,
                        parameter.Type,
                        parameter.Modifier,
                        parameter.HasDefault,
                        parameter.DefaultValue,
                        parameter.Description)),
            ],
            member.DocumentationId,
            member.Summary,
            member.Returns,
            [
                .. member.Exceptions.Select(exception =>
                    new BrowserLibraryExceptionSurface(
                        exception.Type,
                        exception.Description)),
            ],
            member.StableSelector,
            member.AnchorDigest,
            member.CanonicalSignature,
            member.AnchorTypeFullName,
            member.GraphSelectorKey,
            [
                .. member.BodySelectors.Select(selector =>
                    new BrowserLibraryMemberBodySelector(
                        selector.Token,
                        selector.MemberName,
                        selector.SelectorKey)),
            ]);
}
