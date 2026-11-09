using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Sections;

public sealed record LibraryTypeProjectionInspectionRequest(
    LibraryReference Library,
    MetadataTypeDefinitionName Type,
    ApiSurfaceExtractionBounds Bounds,
    bool IncludeRelationships = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(typeof(LibraryTypeProjectionInspectionOutcome.Available), "available")]
[JsonDerivedType(typeof(LibraryTypeProjectionInspectionOutcome.NotFound), "not-found")]
[JsonDerivedType(typeof(LibraryTypeProjectionInspectionOutcome.Unavailable), "unavailable")]
public abstract record LibraryTypeProjectionInspectionOutcome
{
    private protected LibraryTypeProjectionInspectionOutcome() { }

    public sealed record Available(ResearchViews.TypeProjectionResult Projection)
        : LibraryTypeProjectionInspectionOutcome;

    public sealed record NotFound : LibraryTypeProjectionInspectionOutcome;

    public sealed record Unavailable(string Reason)
        : LibraryTypeProjectionInspectionOutcome;
}

public static class TypeProjectionInspectionOperation
{
    public static InspectionEnvelope<ResearchViews.TypeProjectionResult> Execute(
        ApiType type,
        ApiSurface? surface = null,
        ResearchViews.TypeProjectionOptions? options = null)
    {
        ResearchViews.TypeProjectionResult projection =
            ResearchViews.ProjectType(type, surface, options);
        return new(projection, Share(), Diagnostics(projection));
    }

    public static InspectionEnvelope<LibraryTypeProjectionInspectionOutcome> Execute(
        LibraryTypeProjectionInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        using (lease)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Library);
            ArgumentNullException.ThrowIfNull(request.Type);
            ArgumentNullException.ThrowIfNull(request.Bounds);
            string fullName = request.Type.ToMetadataFullName();
            LibraryApiSurfaceInspectionOutcome selected =
                LibraryApiSurfaceInspection.Execute(
                    new(request.Library,
                        ApiSurfaceExtractionScope.IncludeAll,
                        request.Bounds,
                        typesOnly: true,
                        typeSelection: request.IncludeRelationships
                            ? null
                            : new ApiTypeSelection(name =>
                                StringComparer.Ordinal.Equals(name, fullName))),
                    lease, cancellationToken);
            if (selected is not LibraryApiSurfaceInspectionOutcome.Completed completed)
                return Unavailable($"Selected Type metadata was not completed: {selected}.");
            ApiSurface surface = completed.Correspondence.Surface;
            ApiType? type = surface.Types.SingleOrDefault(candidate =>
                request.Type.Equals(candidate.DefinitionName));
            if (type is null)
            {
                if (surface.InspectionFailures.Count > 0)
                    return Unavailable("Type lookup has incomplete metadata evidence.");
                return new(new LibraryTypeProjectionInspectionOutcome.NotFound(),
                    Share(), []);
            }

            InspectionEnvelope<ResearchViews.TypeProjectionResult> projection =
                Execute(type, surface,
                    new(Composition: false,
                        RelationshipGraph: request.IncludeRelationships));
            return new(new LibraryTypeProjectionInspectionOutcome.Available(
                projection.Content), projection.Share, projection.Diagnostics);
        }
    }

    private static InspectionEnvelope<LibraryTypeProjectionInspectionOutcome>
        Unavailable(string reason) =>
        new(new LibraryTypeProjectionInspectionOutcome.Unavailable(reason),
            Share(),
            [new("type-projection.unavailable",
                InspectionDiagnosticSeverity.Warning, reason)]);

    private static InspectionShare Share() =>
        new InspectionShare.NonProjectable("type-projection/share",
            "A complete portable Workspace scenario was not supplied.");

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        ResearchViews.TypeProjectionResult projection) =>
        projection.InspectionFailures.Select(static failure =>
            new InspectionDiagnostic("type-projection.metadata-incomplete",
                InspectionDiagnosticSeverity.Warning,
                $"{failure.Operation}: {failure.Detail}"));
}
