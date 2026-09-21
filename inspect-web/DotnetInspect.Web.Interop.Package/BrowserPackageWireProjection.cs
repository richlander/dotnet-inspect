using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.Package;

/// <summary>
/// Maps <c>DotnetInspect.Web.Core</c>'s DTO-neutral projections onto this facade's own wire
/// records. Core owns the projection semantics; this file owns nothing but the transport shape.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageWireProjection
{
    internal static BrowserExactLibraryApiInspection Project(
        InspectionEnvelope<ExactLibraryApiInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return new(
            BrowserInspectionWireProjection.Project(inspection.ContentKind),
            Project(inspection.Content),
            Project(inspection.PortableProjection),
            [
                .. inspection.Diagnostics.Select(diagnostic =>
                    new BrowserInspectionDiagnostic(
                        diagnostic.Code,
                        diagnostic.Severity.ToString(),
                        diagnostic.Summary.ToString(),
                        diagnostic.Correspondence?.ToString())),
            ]);
    }

    private static BrowserExactLibraryApiInspectionResult Project(
        ExactLibraryApiInspectionResult result) =>
        new(
            result.Outcome switch
            {
                ExactLibraryApiInspectionOutcome.Available =>
                    BrowserExactLibraryApiInspectionOutcome.Available,
                ExactLibraryApiInspectionOutcome.NotFound =>
                    BrowserExactLibraryApiInspectionOutcome.NotFound,
                ExactLibraryApiInspectionOutcome.Ambiguous =>
                    BrowserExactLibraryApiInspectionOutcome.Ambiguous,
                ExactLibraryApiInspectionOutcome.Unavailable =>
                    BrowserExactLibraryApiInspectionOutcome.Unavailable,
                _ => throw new InvalidOperationException(
                    "Unknown exact-Library API inspection outcome."),
            },
            result.PackageId,
            result.PackageVersion,
            result.RequestedTargetFramework,
            result.RequestedLibrary,
            result.Source is null
                ? null
                : new BrowserExactLibraryApiSourceCoordinate(
                    result.Source.PackageId,
                    result.Source.PackageVersion,
                    result.Source.Producer,
                    result.Source.Framework),
            result.Asset is null
                ? null
                : new BrowserExactLibraryApiAsset(
                    result.Asset.Id,
                    result.Asset.Path,
                    result.Asset.AssemblyName,
                    result.Asset.TargetFramework,
                    result.Asset.Kind switch
                    {
                        PackageCompileAssetKind.Reference =>
                            BrowserExactLibraryApiAssetKind.Reference,
                        PackageCompileAssetKind.Library =>
                            BrowserExactLibraryApiAssetKind.Library,
                        _ => throw new InvalidOperationException(
                            "Unknown package compile-asset kind."),
                    }),
            result.Assembly is null
                ? null
                : new BrowserExactLibraryApiAssemblyIdentity(
                    Project(result.Assembly.Identity),
                    result.Assembly.ModuleVersionId),
            result.Inventory is null
                ? null
                : new BrowserExactLibraryApiInventory(
                    result.Inventory.PublicTypeCount,
                    result.Inventory.PublicMemberCount,
                    result.Inventory.PublicMethodCount,
                    result.Inventory.PublicPropertyCount,
                    [
                        .. result.Inventory.TypeKinds.Select(facet =>
                            new BrowserExactLibraryApiFacet(
                                facet.Id,
                                facet.SingularLabel,
                                facet.PluralLabel,
                                facet.Weight,
                                facet.Count,
                                facet.IsDefault)),
                    ],
                    [
                        .. result.Inventory.Namespaces.Select(@namespace =>
                            new BrowserExactLibraryApiNamespace(
                                @namespace.Name,
                                @namespace.Count)),
                    ]),
            result.Truncation is null
                ? null
                : new BrowserExactLibraryApiProjectionTruncation(
                    result.Truncation.Limit switch
                    {
                        ApiSurfaceProjectionLimit.Participants =>
                            BrowserExactLibraryApiProjectionLimit.Participants,
                        ApiSurfaceProjectionLimit.Types =>
                            BrowserExactLibraryApiProjectionLimit.Types,
                        ApiSurfaceProjectionLimit.Members =>
                            BrowserExactLibraryApiProjectionLimit.Members,
                        ApiSurfaceProjectionLimit.InspectionFailures =>
                            BrowserExactLibraryApiProjectionLimit.InspectionFailures,
                        ApiSurfaceProjectionLimit.TypeForwarders =>
                            BrowserExactLibraryApiProjectionLimit.TypeForwarders,
                        ApiSurfaceProjectionLimit.MetadataRows =>
                            BrowserExactLibraryApiProjectionLimit.MetadataRows,
                        ApiSurfaceProjectionLimit.RetainedTextCharacters =>
                            BrowserExactLibraryApiProjectionLimit.RetainedTextCharacters,
                        _ => throw new InvalidOperationException(
                            "Unknown API projection limit."),
                    },
                    result.Truncation.Bound,
                    result.Truncation.ProjectedParticipants,
                    result.Truncation.OmittedParticipants,
                    result.Truncation.ProjectedTypes,
                    result.Truncation.ProjectedMembers,
                    result.Truncation.ProjectedInspectionFailures,
                    result.Truncation.ProjectedTypeForwarders,
                    result.Truncation.InspectedMetadataRows,
                    result.Truncation.ProjectedRetainedTextCharacters),
            [
                .. result.Failures.Select(failure =>
                    new BrowserExactLibraryApiInspectionFailure(
                        failure.Kind switch
                        {
                            ExactLibraryApiInspectionFailureKind.ContextLoad =>
                                BrowserExactLibraryApiInspectionFailureKind.ContextLoad,
                            ExactLibraryApiInspectionFailureKind.PackageMismatch =>
                                BrowserExactLibraryApiInspectionFailureKind.PackageMismatch,
                            ExactLibraryApiInspectionFailureKind
                                .CompileSelectionUnavailable =>
                                    BrowserExactLibraryApiInspectionFailureKind
                                        .CompileSelectionUnavailable,
                            ExactLibraryApiInspectionFailureKind.LibraryNotFound =>
                                BrowserExactLibraryApiInspectionFailureKind.LibraryNotFound,
                            ExactLibraryApiInspectionFailureKind.LibraryAmbiguous =>
                                BrowserExactLibraryApiInspectionFailureKind.LibraryAmbiguous,
                            ExactLibraryApiInspectionFailureKind.ParticipantUnavailable =>
                                BrowserExactLibraryApiInspectionFailureKind.ParticipantUnavailable,
                            ExactLibraryApiInspectionFailureKind.InspectionIncomplete =>
                                BrowserExactLibraryApiInspectionFailureKind.InspectionIncomplete,
                            ExactLibraryApiInspectionFailureKind.ProjectionTruncated =>
                                BrowserExactLibraryApiInspectionFailureKind.ProjectionTruncated,
                            _ => throw new InvalidOperationException(
                                "Unknown exact-Library API inspection failure."),
                        },
                        failure.Detail,
                        failure.SubjectAssembly is null
                            ? null
                            : Project(failure.SubjectAssembly))),
            ],
            result.IsComplete,
            result.IsAvailable);

    private static BrowserExactLibraryApiAssemblyReferenceIdentity Project(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken);

    internal static BrowserCompileLibraryAvailability Project(
        BrowserCompileLibraryInfo compileLibrary)
    {
        ArgumentNullException.ThrowIfNull(compileLibrary);
        return new(
            compileLibrary.State switch
            {
                BrowserCompileLibraryState.Selected =>
                    BrowserCompileLibraryStatus.Selected,
                BrowserCompileLibraryState.NoCompileAssets =>
                    BrowserCompileLibraryStatus.NoCompileAssets,
                BrowserCompileLibraryState.NoMatchingTargetFramework =>
                    BrowserCompileLibraryStatus.NoMatchingTargetFramework,
                BrowserCompileLibraryState.EmptyCompileGroup =>
                    BrowserCompileLibraryStatus.EmptyCompileGroup,
                BrowserCompileLibraryState.InvalidImplementationAssets =>
                    BrowserCompileLibraryStatus.InvalidImplementationAssets,
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            },
            compileLibrary.TargetFramework,
            compileLibrary.Message);
    }

    internal static BrowserPackageSurface Project(BrowserPackageSurfaceInfo surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return new(
            surface.Package,
            surface.Version,
            surface.Frameworks,
            surface.ActiveFramework,
            Project(surface.Icon),
            surface.DefaultAssemblyId,
            Project(surface.CompileLibrary),
            [.. surface.Assemblies.Select(Project)],
            [.. surface.Types.Select(Project)],
            [.. surface.Accessibility.Select(Project)],
            surface.TotalMembers,
            Project(surface.Documents),
            surface.InspectionErrors,
            surface.InspectionError);
    }

    internal static BrowserPackageVersionSettlementInspection Project(
        InspectionEnvelope<PackageVersionSettlementOutcome> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return new(
            BrowserInspectionWireProjection.Project(inspection.ContentKind),
            Project(inspection.Content),
            Project(inspection.PortableProjection),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    internal static BrowserPackageInfoMeasurementInspection Project(
        InspectionEnvelope<PackageInfoMeasurements> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        PackageInfoMeasurements content = inspection.Content;
        return new(
            BrowserInspectionWireProjection.Project(inspection.ContentKind),
            new BrowserPackageInfoMeasurements(
                content.Status.ToString(),
                content.PackageId,
                content.PackageVersion,
                content.CompressedPackageBytes,
                content.SelectedTargetFramework?.ToString(),
                content.AvailableTargetFrameworks is null
                    ? null
                    : [
                        .. content.AvailableTargetFrameworks.Select(
                            static framework => framework.ToString()),
                    ],
                content.SelectedTargetFrameworkFolders is null
                    ? null
                    : [
                        .. content.SelectedTargetFrameworkFolders.Select(
                            static folder => folder.ToString()),
                    ],
                content.SelectedLibraryPayloadBytes,
                content.SelectedLibraryCount,
                content.Detail?.ToString(),
                content.UnavailableReason?.ToString(),
                content.HasSelectedSlice),
            Project(inspection.PortableProjection),
            [.. inspection.Diagnostics.Select(Project)]);
    }

    static BrowserPackageVersionSettlementOutcome Project(
        PackageVersionSettlementOutcome outcome) =>
        outcome switch
        {
            PackageVersionSettlementOutcome.Settled settled =>
                new(
                    BrowserPackageVersionSettlementOutcomeKind.Settled,
                    new(
                        Project(settled.Result.Request),
                        new(
                            settled.Result.Coordinate.PackageId,
                            settled.Result.Coordinate.Version),
                        settled.Result.IncludePrerelease,
                        settled.Result.Freshness?.ToString(),
                        [
                            .. settled.Result.Listings.Select(listing =>
                                new BrowserPackageVersionSettlementListing(
                                    listing.Version,
                                    listing.Listed)),
                        ],
                        [
                            .. settled.Result.SourceListings.Select(listing =>
                                new BrowserPackageVersionSettlementSourceListing(
                                    listing.Version,
                                    listing.Feed,
                                    listing.Listed)),
                        ]),
                    Failure: null),
            PackageVersionSettlementOutcome.NotSettled notSettled =>
                new(
                    BrowserPackageVersionSettlementOutcomeKind.NotSettled,
                    Result: null,
                    new(
                        Project(notSettled.Failure.Request),
                        notSettled.Failure.Kind.ToString(),
                        notSettled.Failure.Reason.ToString(),
                        notSettled.Failure.OperationTimedOut,
                        [
                            .. notSettled.Failure.AuthorityFailures.Select(failure =>
                                new BrowserPackageVersionSettlementAuthorityFailure(
                                    failure.Authority.ToString(),
                                    failure.Kind.ToString(),
                                    failure.Message.ToString(),
                                    failure.TimeoutKind?.ToString())),
                        ])),
            _ => throw new InvalidOperationException(
                "Unknown package version settlement outcome."),
        };

    static BrowserPackageVersionSettlementRequest Project(
        PackageCoordinate request) =>
        new(request.PackageId, request.Version);

    internal static BrowserInspectionPortableProjection Project(
        InspectionPortableProjection portableProjection) =>
        portableProjection switch
        {
            InspectionPortableProjection.Available available =>
                new(
                    BrowserInspectionPortableProjectionKind.Available,
                    available.FullUrl,
                    available.Packet,
                    Path: null,
                    Reason: null,
                    Explanation: null),
            InspectionPortableProjection.NonProjectable nonProjectable =>
                new(
                    BrowserInspectionPortableProjectionKind.NonProjectable,
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Path,
                    BrowserInspectionWireProjection.Project(
                        nonProjectable.Reason),
                    nonProjectable.Explanation),
            _ => throw new InvalidOperationException(
                "Unknown inspection portable projection."),
        };

    internal static BrowserInspectionDiagnostic Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    internal static BrowserAssemblySurface Project(BrowserAssemblySurfaceInfo assembly) =>
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

    internal static BrowserAccessibilityDescriptor Project(
        BrowserAccessibilityInfo accessibility) =>
        new(
            accessibility.Id,
            accessibility.Label,
            accessibility.Order,
            accessibility.IsDefault,
            accessibility.Count);

    internal static BrowserTypeSurface Project(BrowserTypeSurfaceInfo type) =>
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

    internal static BrowserMemberSurface Project(BrowserMemberSurfaceInfo member) =>
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
                .. member.Parameters.Select(parameter => new BrowserParameterSurface(
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
                .. member.Exceptions.Select(exception => new BrowserExceptionSurface(
                    exception.Type,
                    exception.Description)),
            ],
            member.StableSelector,
            member.AnchorDigest,
            member.CanonicalSignature,
            member.AnchorTypeFullName,
            member.GraphSelectorKey,
            [
                .. member.BodySelectors.Select(selector => new BrowserMemberBodySelector(
                    selector.Token,
                    selector.MemberName,
                    selector.SelectorKey)),
            ]);

    internal static BrowserPackageCacheStats Project(BrowserPackageCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Packages,
            snapshot.Resident,
            snapshot.MaxPackageEntries,
            snapshot.Workspaces,
            snapshot.MaxWorkspaces,
            snapshot.MaxWorkspaceAssembliesPerRole,
            snapshot.ResidentBytes,
            snapshot.MaxResidentBytes,
            snapshot.MaxWorkspaceRetainedImageBytes);
    }

    internal static BrowserPackageDocument Project(BrowserPackageDocumentEntry document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Size);
    }

    internal static BrowserPackageDocument[] Project(
        IReadOnlyList<BrowserPackageDocumentEntry> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return [.. documents.Select(Project)];
    }

    internal static BrowserPackageDocumentContent Project(
        BrowserPackageDocumentPayload document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Text);
    }

    internal static BrowserPackageIcon? Project(BrowserPackageIconPayload? icon) =>
        icon is null
            ? null
            : new BrowserPackageIcon(icon.MediaType, icon.Base64);
}
