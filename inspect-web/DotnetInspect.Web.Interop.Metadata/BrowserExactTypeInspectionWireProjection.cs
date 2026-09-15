using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
internal static class BrowserExactTypeInspectionWireProjection
{
    internal static BrowserExactTypeInspectionEnvelope Project(
        InspectionEnvelope<ExactTypeInspectionResult> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new(
            Project(envelope.Content),
            Project(envelope.Share),
            [.. envelope.Diagnostics.Select(Project)]);
    }

    static BrowserExactTypeInspectionContent Project(
        ExactTypeInspectionResult result)
    {
        BrowserExactTypeOutcome kind;
        bool isComplete;
        BrowserExactTypeAvailable? available = null;
        BrowserExactTypeName[] suggestions = [];
        BrowserExactTypeCandidate[] candidates = [];

        switch (result)
        {
            case ExactTypeInspectionResult.Available value:
                kind = BrowserExactTypeOutcome.Available;
                isComplete = true;
                available = Project(value);
                break;
            case ExactTypeInspectionResult.NotFound notFound:
                kind = BrowserExactTypeOutcome.NotFound;
                isComplete = true;
                suggestions = [.. notFound.Suggestions.Select(Project)];
                break;
            case ExactTypeInspectionResult.Ambiguous ambiguous:
                kind = BrowserExactTypeOutcome.Ambiguous;
                isComplete = true;
                candidates = [.. ambiguous.Candidates.Select(Project)];
                break;
            case ExactTypeInspectionResult.Incomplete incomplete:
                kind = BrowserExactTypeOutcome.Incomplete;
                isComplete = false;
                candidates = [.. incomplete.Candidates.Select(Project)];
                break;
            case ExactTypeInspectionResult.Rejected:
                kind = BrowserExactTypeOutcome.Rejected;
                isComplete = false;
                break;
            default:
                throw new InvalidOperationException(
                    "Exact type inspection returned an unknown outcome.");
        }

        return new(
            kind,
            isComplete,
            Project(result.Request),
            available,
            suggestions,
            candidates,
            [.. result.Failures.Select(Project)]);
    }

    static BrowserExactTypeRequest Project(
        ExactTypeInspectionRequest request) =>
        new(
            request.ContextIndex,
            request.TypeSelector,
            request.Scope switch
            {
                ApiSurfaceScope.Public =>
                    BrowserExactTypeSurfaceScope.Public,
                ApiSurfaceScope.IncludeAll =>
                    BrowserExactTypeSurfaceScope.IncludeAll,
                ApiSurfaceScope.PublicWithNonPublicTypes =>
                    BrowserExactTypeSurfaceScope.PublicWithNonPublicTypes,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            },
            new(
                request.SurfaceLimits.MaxParticipants,
                request.SurfaceLimits.MaxTypes,
                request.SurfaceLimits.MaxMembers,
                request.SurfaceLimits.MaxInspectionFailures,
                request.SurfaceLimits.MaxTypeForwarders,
                request.SurfaceLimits.MaxMetadataRows,
                request.SurfaceLimits.MaxRetainedTextCharacters),
            request.AssemblyName,
            request.Library is null ? null : Project(request.Library),
            request.CompileAssetId);

    static BrowserExactTypeAvailable Project(
        ExactTypeInspectionResult.Available available)
    {
        ResearchViews.TypeProjectionResult facts =
            ResearchViews.ProjectType(available.Type);
        BrowserTypeSurface surface = BrowserMetadataWireProjection.Project(
            BrowserSurfaceProjection.Type(
                available.Type,
                available.Candidate.SupplierAssembly.Name,
                available.Candidate.SupplierAssetId,
                available.Candidate.SupplierAssembly.Name,
                qualifyId: true,
                selectedMembers: available.Members.Members));

        return new(
            Project(available.Candidate),
            available.IsContextUnique,
            new(
                surface,
                facts.BaseType,
                [.. facts.Interfaces],
                [.. facts.DerivedTypes],
                [
                    .. facts.TypeParameters.Select(parameter =>
                        new BrowserTypeParameter(
                            parameter.Name,
                            parameter.Variance,
                            [.. parameter.Constraints])),
                ],
                [.. facts.Attributes],
                facts.EnumUnderlyingType,
                facts.Composition is { } composition
                    ? new BrowserTypeComposition(
                        composition.Methods,
                        composition.Properties,
                        composition.Fields,
                        composition.Events,
                        composition.Constructors,
                        composition.Operators,
                        composition.ExplicitInterfaceImplementations,
                        composition.ExtensionMethods,
                        composition.Static,
                        composition.Unsafe,
                        composition.Async,
                        composition.Virtual,
                        composition.Abstract,
                        composition.Override,
                        composition.Extension,
                        composition.Obsolete,
                        composition.Total)
                    : null,
                available.Type.IsForwarded),
            [
                .. available.Members.Members.Select(member =>
                    new BrowserExactTypeMemberFacts(
                        member.MetadataToken,
                        member.DeclarationMetadataToken,
                        member.IsAsync,
                        member.HasMethodBody,
                        [.. member.Attributes])),
            ],
            [
                .. available.Members.KindFacets.Select(facet =>
                    new BrowserExactTypeFacet(
                        facet.Id,
                        facet.SingularLabel,
                        facet.PluralLabel,
                        facet.Weight,
                        facet.Count,
                        facet.IsDefault)),
            ],
            [.. available.InspectionFailures.Select(Project)]);
    }

    static BrowserExactTypeCandidate Project(ExactTypeCandidate candidate) =>
        new(
            Project(candidate.Definition),
            new(
                candidate.Address.ModuleVersionId.ToString("D"),
                candidate.Address.Definition.Value),
            Project(candidate.Declaration),
            Project(candidate.Supplier),
            Project(candidate.SupplierSource),
            Project(candidate.SupplierAssembly),
            [.. candidate.ForwardingHops.Select(Project)],
            candidate.DeclarationAssetId,
            candidate.SupplierAssetId);

    static BrowserExactTypeLibrarySource Project(
        ExactLibrarySourceCoordinate source)
    {
        BrowserExactTypeAssemblyIdentity library =
            Project(source.LibraryIdentity.Identity);
        return source switch
        {
            ExactLibrarySourceCoordinate.Package package =>
                new(
                    BrowserExactTypeLibrarySourceKind.Package,
                    library,
                    package.PackageCoordinate.PackageId,
                    package.PackageCoordinate.Version),
            ExactLibrarySourceCoordinate.Platform platform =>
                new(
                    BrowserExactTypeLibrarySourceKind.Platform,
                    library,
                    PlatformFamily:
                        platform.Population.Family.ToString()),
            ExactLibrarySourceCoordinate.Project =>
                new(BrowserExactTypeLibrarySourceKind.Project, library),
            ExactLibrarySourceCoordinate.Local =>
                new(BrowserExactTypeLibrarySourceKind.Local, library),
            _ => throw new InvalidOperationException(
                "Exact type inspection returned an unknown Library source."),
        };
    }

    static BrowserExactTypeRealizedSource Project(
        RealizedMemberCoordinate source) =>
        source switch
        {
            RealizedMemberCoordinate.Package package =>
                new(
                    BrowserExactTypeRealizedSourceKind.Package,
                    package.PackageId,
                    package.Version,
                    package.Producer,
                    package.Framework,
                    package.RuntimeIdentifier),
            RealizedMemberCoordinate.Platform platform =>
                new(
                    BrowserExactTypeRealizedSourceKind.Platform,
                    Version: platform.Version,
                    Producer: platform.Producer,
                    Framework: platform.Framework,
                    PlatformFamily: platform.Family,
                    Assembly: platform.Assembly),
            _ => throw new InvalidOperationException(
                "Browser exact type inspection supports only package or platform realized sources."),
        };

    static BrowserExactTypeForwardingHop Project(
        ExactTypeForwardingHop hop) =>
        new(
            Project(hop.SourceAssembly),
            Project(hop.TargetAssembly),
            hop.Scope switch
            {
                AssemblyResolutionScope.Any =>
                    BrowserExactTypeResolutionScope.Any,
                AssemblyResolutionScope.Platform =>
                    BrowserExactTypeResolutionScope.Platform,
                _ => throw new ArgumentOutOfRangeException(nameof(hop)),
            });

    static BrowserExactTypeFailure Project(
        ExactTypeInspectionFailure failure) =>
        new(
            failure.Kind switch
            {
                ExactTypeInspectionFailureKind.InvalidRequest =>
                    BrowserExactTypeFailureKind.InvalidRequest,
                ExactTypeInspectionFailureKind.DefinitionMismatch =>
                    BrowserExactTypeFailureKind.DefinitionMismatch,
                ExactTypeInspectionFailureKind.ContextUnavailable =>
                    BrowserExactTypeFailureKind.ContextUnavailable,
                ExactTypeInspectionFailureKind.ContextLoadFailed =>
                    BrowserExactTypeFailureKind.ContextLoadFailed,
                ExactTypeInspectionFailureKind.PopulationUnavailable =>
                    BrowserExactTypeFailureKind.PopulationUnavailable,
                ExactTypeInspectionFailureKind.DeclarationInventoryIncomplete =>
                    BrowserExactTypeFailureKind.DeclarationInventoryIncomplete,
                ExactTypeInspectionFailureKind.TypeResolutionRejected =>
                    BrowserExactTypeFailureKind.TypeResolutionRejected,
                ExactTypeInspectionFailureKind.TypeResolutionUnavailable =>
                    BrowserExactTypeFailureKind.TypeResolutionUnavailable,
                ExactTypeInspectionFailureKind.TypeResolutionAmbiguous =>
                    BrowserExactTypeFailureKind.TypeResolutionAmbiguous,
                ExactTypeInspectionFailureKind.ApiSurfaceRejected =>
                    BrowserExactTypeFailureKind.ApiSurfaceRejected,
                ExactTypeInspectionFailureKind.ApiSurfaceFailed =>
                    BrowserExactTypeFailureKind.ApiSurfaceFailed,
                ExactTypeInspectionFailureKind.ApiSurfaceIncomplete =>
                    BrowserExactTypeFailureKind.ApiSurfaceIncomplete,
                ExactTypeInspectionFailureKind.AsyncClassificationUnavailable =>
                    BrowserExactTypeFailureKind.AsyncClassificationUnavailable,
                ExactTypeInspectionFailureKind.ResolvedTypeMissing =>
                    BrowserExactTypeFailureKind.ResolvedTypeMissing,
                _ => throw new ArgumentOutOfRangeException(nameof(failure)),
            },
            failure.Assembly is null ? null : Project(failure.Assembly),
            failure.ContextLoadFailure?.ToString(),
            failure.CandidateOpenFailure?.ToString(),
            failure.PopulationFailure?.ToString(),
            failure.SurfaceLimit?.ToString());

    static BrowserExactTypeInspectionFailure Project(
        ApiSurfaceInspectionFailure failure) =>
        new(
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism switch
            {
                MetadataTypeNameFailureMechanism.Metadata =>
                    BrowserExactTypeInspectionFailureMechanism.Metadata,
                MetadataTypeNameFailureMechanism.Relationship =>
                    BrowserExactTypeInspectionFailureMechanism.Relationship,
                MetadataTypeNameFailureMechanism.Signature =>
                    BrowserExactTypeInspectionFailureMechanism.Signature,
                MetadataTypeNameFailureMechanism.TypeSpecification =>
                    BrowserExactTypeInspectionFailureMechanism.TypeSpecification,
                _ => throw new ArgumentOutOfRangeException(nameof(failure)),
            },
            failure.Kind,
            failure.Detail,
            failure.SubjectAssembly is null
                ? null
                : Project(failure.SubjectAssembly),
            failure.DependencyAssembly is null
                ? null
                : Project(failure.DependencyAssembly),
            failure.OwningTypeToken,
            failure.OwningTypeDefinition is null
                ? null
                : Project(failure.OwningTypeDefinition),
            [.. failure.AffectedTypeDefinitions.Select(Project)]);

    static BrowserExactTypeShare Project(InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                new(
                    BrowserExactTypeShareKind.Available,
                    available.FullUrl,
                    available.Packet,
                    Path: null,
                    Reason: null),
            InspectionShare.NonProjectable nonProjectable =>
                new(
                    BrowserExactTypeShareKind.NonProjectable,
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Exact type inspection returned an unknown Share outcome."),
        };

    static BrowserExactTypeDiagnostic Project(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity switch
            {
                InspectionDiagnosticSeverity.Information =>
                    BrowserExactTypeDiagnosticSeverity.Information,
                InspectionDiagnosticSeverity.Warning =>
                    BrowserExactTypeDiagnosticSeverity.Warning,
                InspectionDiagnosticSeverity.Error =>
                    BrowserExactTypeDiagnosticSeverity.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(diagnostic)),
            },
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    static BrowserExactTypeAssemblyIdentity Project(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken);

    static BrowserExactTypeName Project(
        MetadataTypeDefinitionName name) =>
        new(name.Namespace, [.. name.Segments]);
}
