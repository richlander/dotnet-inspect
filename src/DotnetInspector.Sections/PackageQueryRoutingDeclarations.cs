using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

internal static class PackageQueryRoutingDeclarations
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
            "package-query",
            "Package Query",
            "Inspection Capability Composition",
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
    ];

}
