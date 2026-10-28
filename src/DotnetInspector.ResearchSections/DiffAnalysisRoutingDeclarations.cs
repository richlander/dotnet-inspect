using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.ResearchSections;

internal static class DiffAnalysisRoutingDeclarations
{
    internal static ImmutableArray<ResourceExplanationRoutingDeclaration> Declarations =>
        Cache.Declarations;

    private static class Cache
    {
        internal static readonly ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations = Create();
    }

    private static ImmutableArray<ResourceExplanationRoutingDeclaration> Create() =>
    [
        new(
            ResourceExplanationRoutingRole.Operation,
            "diff-analysis",
            "Diff analyses",
            "Inspection Capability Composition",
            "Compares two versions of a library, type, or member through one "
            + "registered analysis, such as API, allocation, IL, or string "
            + "literals.",
            [
                "What changed in this library's public API between two "
                + "versions?",
                "Did this method's allocations or IL change between "
                + "versions?",
                "Which string literals did this library add or remove?",
            ],
            [
                ResourceExplanationCatalog.CapabilityDocumentKey(
                    DiffAnalysisCatalog.DocumentIdentity),
                ResourceExplanationCatalog.AnalysesCollectionKey,
            ],
            grain: "One finding per changed occurrence at each analysis's own "
                + "grain, such as an API member, IL operation, C# line, or "
                + "allocation, within the compared library, type, or member.",
            scope: "The two compared versions.",
            acceptedSubjectKinds: CompareSubjectKinds()),
    ];

    /// <summary>
    /// The subject kinds the registered analyses compare, in product order:
    /// Library, Type, Member. Root and Workspace surfaces have no subject-kind
    /// declaration.
    /// </summary>
    private static ImmutableArray<string> CompareSubjectKinds()
    {
        HashSet<AnalysisReportSurfaceKind> surfaces =
        [
            .. DiffAnalysisCatalog.Registrations
                .Select(static registration =>
                    registration.Analysis.ParticipationFor(
                        AnalysisOperationKind.Compare))
                .OfType<AnalysisOperationParticipation>()
                .SelectMany(static participation => participation.Surfaces)
                .Select(static surface => surface.Surface),
        ];
        (AnalysisReportSurfaceKind Surface, StructuralSubjectKind Kind)[]
            subjects =
            [
                (AnalysisReportSurfaceKind.Library, StructuralSubjectKind.Library),
                (AnalysisReportSurfaceKind.Type, StructuralSubjectKind.Type),
                (AnalysisReportSurfaceKind.Member, StructuralSubjectKind.Member),
            ];
        return
        [
            .. subjects
                .Where(subject => surfaces.Contains(subject.Surface))
                .Select(static subject =>
                    StructuralRoutingDeclarations.Identity(subject.Kind)),
        ];
    }
}
