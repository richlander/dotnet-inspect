using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Consumes an already issued Library definition identity. The document
    /// producer validates that identity ordinally within the same lease;
    /// no query inventory or second definition probe is required. Admission
    /// remains that of the issued metadata identity, independently of the
    /// requested Member accessibility and hidden-status projection.
    /// </summary>
    public static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> ExecuteOverviewDefinition(
        TypeOverviewDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default) =>
        TypeOverviewDocumentInspectionOperation.Execute(request, lease, cancellationToken);

    /// <summary>
    /// Selects within one borrowed Library without projecting Members. The
    /// callback consumes the issued definition and the same Library lease;
    /// the lease closes on every completion, cancellation, and exception.
    /// </summary>
    public static TResult ExecuteWithLibraryTarget<TResult>(
        LibraryReference library,
        LibraryOperationLease lease,
        string type,
        ApiSurfaceScope scope,
        ApiSurfaceExtractionBounds bounds,
        Func<InspectionEnvelope<ExactTypeDefinitionSelection>, LibraryOperationLease, TResult> use,
        ExactTypeSelectionKind selectionKind = ExactTypeSelectionKind.Query,
        bool includePrefixDiscovery = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        using (lease)
        {
            ArgumentNullException.ThrowIfNull(use);
            ExactTypeDefinitionSelection result = ExactTypeInspectionQuery.SelectLibrary(
                library, lease, type, scope, bounds, selectionKind,
                includePrefixDiscovery, cancellationToken);
            var envelope = new InspectionEnvelope<ExactTypeDefinitionSelection>(
                result,
                new InspectionShare.NonProjectable(
                    "library-type/share", "A portable Workspace scenario was not supplied."),
                result.Outcome == ExactTypeInspectionOutcome.Available
                    ? []
                    : [new InspectionDiagnostic(
                        $"exact-type.{result.Outcome.ToString().ToLowerInvariant()}",
                        InspectionDiagnosticSeverity.Error,
                        result.Failure ?? "The exact Type selection is unavailable.")]);
            return use(envelope, lease);
        }
    }
}
