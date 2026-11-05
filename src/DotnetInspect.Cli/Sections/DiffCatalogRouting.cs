using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// The explanation root's routing entry for the <c>diff</c> structural
/// catalog: the comparison sections <c>diff -S</c> selects. The CLI issues it
/// beside the catalog, which only this host projects.
/// </summary>
internal static class DiffCatalogRouting
{
    public static InspectionCapabilityModule Module { get; } = new(
        "dotnet-inspect.cli/diff-routing",
        routingDeclarations: static () => Cache.Declarations);

    private static class Cache
    {
        internal static readonly
            ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations = Create();
    }

    private static ImmutableArray<ResourceExplanationRoutingDeclaration>
        Create() =>
    [
        new(
            ResourceExplanationRoutingRole.Operation,
            StructuralCatalogPaths.Name(InspectionCatalogIdentity.Diff),
            "Diff",
            "Schema Query",
            "Compares two versions of a package, platform library, or local "
            + "library: its public API changes classified as breaking, "
            + "potentially breaking, additive, or non-breaking, and its "
            + "body-level analysis, implementation, and complexity changes.",
            [
                "What breaking changes did this package make between two "
                + "versions?",
                "What public API was added or removed between versions?",
                "Which method bodies changed between two versions of this "
                + "library?",
                "Did complexity or allocations regress between versions?",
            ],
            [
                ResourceExplanationCatalog.StructuralCatalogKey(
                    StructuralCatalogPaths.Name(
                        InspectionCatalogIdentity.Diff)),
            ],
            grain: "One row per changed type, member, or body-level "
                + "observation, at each section's own grain.",
            scope: "The two compared versions, narrowed by any type or "
                + "member filter.",
            acceptedSubjectKinds:
            [
                .. new[]
                {
                    StructuralSubjectKind.Package,
                    StructuralSubjectKind.Library,
                    StructuralSubjectKind.Type,
                    StructuralSubjectKind.Member,
                }.Select(StructuralRoutingDeclarations.Identity),
            ]),
    ];
}
