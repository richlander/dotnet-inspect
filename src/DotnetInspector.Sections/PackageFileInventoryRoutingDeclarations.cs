using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

internal static class PackageFileInventoryRoutingDeclarations
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
            "package-files",
            "Package Files",
            "Inspection Capability Composition",
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
    ];

}
