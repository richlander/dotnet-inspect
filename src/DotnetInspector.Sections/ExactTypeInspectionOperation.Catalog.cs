using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static partial class ExactTypeInspectionOperation
{
    /// <summary>
    /// Selects from a source-authorized API catalog whose forwarding and
    /// admission have already been settled by its producer.
    /// </summary>
    public static InspectionEnvelope<ExactTypeCatalogSelection> SelectCatalog(
        ApiSurface catalog,
        string type,
        bool allowImpliedMember = false,
        ExactTypeSelectionKind selectionKind = ExactTypeSelectionKind.Query)
    {
        ExactTypeCatalogSelection result = ExactTypeInspectionQuery.SelectCatalog(
            catalog, type, allowImpliedMember, selectionKind);
        return new(result,
            new InspectionShare.NonProjectable(
                "catalog-type/share", "A portable Workspace scenario was not supplied."),
            result.Outcome == ExactTypeInspectionOutcome.Available
                ? []
                : [new InspectionDiagnostic(
                    $"exact-type.{result.Outcome.ToString().ToLowerInvariant()}",
                    InspectionDiagnosticSeverity.Error,
                    result.Outcome switch
                    {
                        ExactTypeInspectionOutcome.Ambiguous => $"Type '{type}' resolved to more than one exact Metadata definition.",
                        ExactTypeInspectionOutcome.Unavailable => $"Type '{type}' could not be inspected completely.",
                        _ => $"Type '{type}' not found.",
                    })]);
    }

    /// <summary>Bounded Type prefixes for the existing first-available operation.</summary>
    public static IEnumerable<TypeMemberSelectionCandidate> GetTypeMemberCandidates(string type)
        => ExactTypeInspectionQuery.TypeMemberCandidates(type);
}
