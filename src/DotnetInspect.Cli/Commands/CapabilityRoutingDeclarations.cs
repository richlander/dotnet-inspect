using System.Collections.Immutable;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Inspection Capability Composition's routing declarations: one operation
/// per composed capability family the CLI admits. Package Query and Package
/// Files route to their inspection documents; the Diff analyses route to the
/// analysis collection, and accept the subject kinds their Compare
/// participation registers.
/// </summary>
/// <remarks>
/// Each operation accepts Schema Query subject kinds, so a host admits these
/// declarations only together with <see cref="StructuralRoutingDeclarations"/>.
/// </remarks>
internal static class CapabilityRoutingDeclarations
{
    private const string Issuer = "Inspection Capability Composition";

    public static ImmutableArray<ResourceExplanationRoutingDeclaration>
        Create() =>
    [
        new(
            ResourceExplanationRoutingRole.Operation,
            "package-query",
            "Package Query",
            Issuer,
            "Finds the packages in a source that match facet terms such as "
            + "prefix, license, dependencies, or a string literal in their "
            + "libraries.",
            [
                "Which packages depend on a given package?",
                "Which packages ship a library containing a given string?",
                "Which packages with a given prefix use a given license?",
            ],
            [
                ResourceExplanationCatalog.CapabilityDocumentKey(
                    PackageQueryCapability.DocumentIdentity),
            ],
            grain: "One package per match.",
            scope: "The packages of the queried source.",
            acceptedSubjectKinds:
            [
                StructuralRoutingDeclarations.Identity(
                    StructuralSubjectKind.Package),
            ]),
        new(
            ResourceExplanationRoutingRole.Operation,
            "package-files",
            "Package Files",
            Issuer,
            "Lists every file in one acquired package version, in a stable "
            + "order.",
            [
                "Which files does this package ship?",
                "Where in this package is a given file?",
            ],
            [
                ResourceExplanationCatalog.CapabilityDocumentKey(
                    PackageFileInventoryCapability.DocumentIdentity),
            ],
            grain: "One package file.",
            scope: "One acquired package version.",
            acceptedSubjectKinds:
            [
                StructuralRoutingDeclarations.Identity(
                    StructuralSubjectKind.Package),
            ]),
        new(
            ResourceExplanationRoutingRole.Operation,
            "diff-analysis",
            "Diff analyses",
            Issuer,
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
            .. DiffAnalysisCommandCapability.Catalog.Analyses
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
