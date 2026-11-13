using System.Collections.Immutable;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Schema Query's routing declarations: one per structural subject kind a
/// reader can choose from the explanation root, each routing to the
/// structural catalogs that describe that subject. Identities are the
/// <see cref="StructuralSubjectKind"/> names; destinations are the catalog
/// roots <see cref="StructuralCatalogPaths.Name"/> issues.
/// </summary>
public static class StructuralRoutingDeclarations
{
    private const string Issuer = "Schema Query";

    public static InspectionCapabilityModule Module { get; } = new(
        "schema-query.routing", routingDeclarations: static () => Cache.Declarations);

    private static class Cache
    {
        internal static readonly ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations = Create();
    }

    private static ImmutableArray<ResourceExplanationRoutingDeclaration>
        Create() =>
    [
        SubjectKind(
            StructuralSubjectKind.Package,
            "Package",
            "A NuGet package version: its metadata, dependencies, target "
            + "frameworks, files, libraries, and audit and vulnerability "
            + "facts.",
            [
                "What does this package depend on, per target framework?",
                "Does this package version have known vulnerabilities?",
                "Which files and libraries does this package ship?",
            ],
            "One package version.",
            "The package archive and its source registration.",
            InspectionCatalogIdentity.Package),
        SubjectKind(
            StructuralSubjectKind.Library,
            "Library",
            "A .NET library image: its references, metadata, dependency "
            + "structure, IL-derived analyses, and the types it declares.",
            [
                "Which assemblies does this library reference?",
                "Does this library have namespace dependency cycles?",
                "Where does this library allocate, box, or use unsafe code?",
                "Which types does this library declare?",
            ],
            "One library image.",
            "The library's metadata and IL, with its symbols where "
            + "available.",
            InspectionCatalogIdentity.Library,
            InspectionCatalogIdentity.ApiType),
        SubjectKind(
            StructuralSubjectKind.Type,
            "Type",
            "One type in a library: its members, base class, interfaces, "
            + "derived types, and source.",
            [
                "What members does this type declare?",
                "What does this type derive from, and what derives from it?",
                "Where is this type's source?",
            ],
            "One type.",
            "The type's declaring library.",
            InspectionCatalogIdentity.ApiMember),
        SubjectKind(
            StructuralSubjectKind.Member,
            "Member",
            "One member of a type, or one overload of it: its signature, "
            + "body, calls and callers, costs, and source.",
            [
                "What does this method call, and what calls it?",
                "Where is this member's source?",
                "What does this method allocate?",
            ],
            "One member, or one overload of it.",
            "The member's declaring type.",
            InspectionCatalogIdentity.ApiMemberOverload,
            InspectionCatalogIdentity.ApiMemberDetail),
    ];

    private static ResourceExplanationRoutingDeclaration SubjectKind(
        StructuralSubjectKind kind,
        string title,
        string purpose,
        string[] questions,
        string grain,
        string scope,
        params InspectionCatalogIdentity[] destinations) =>
        new(
            ResourceExplanationRoutingRole.SubjectKind,
            Identity(kind),
            title,
            Issuer,
            purpose,
            questions,
            destinations.Select(static destination =>
                ResourceExplanationCatalog.StructuralCatalogKey(
                    StructuralCatalogPaths.Name(destination))),
            grain: grain,
            scope: scope);

    /// <summary>The routing identity of one structural subject kind.</summary>
    public static string Identity(StructuralSubjectKind kind) =>
        kind switch
        {
            StructuralSubjectKind.Workspace => "workspace",
            StructuralSubjectKind.Ecosystem => "ecosystem",
            StructuralSubjectKind.Package => "package",
            StructuralSubjectKind.Library => "library",
            StructuralSubjectKind.Type => "type",
            StructuralSubjectKind.Member => "member",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
