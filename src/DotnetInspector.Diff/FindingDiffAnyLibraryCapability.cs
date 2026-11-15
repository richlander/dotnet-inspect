using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.Diff;

/// <summary>
/// Finding Diff Any's Library level as a host-neutral inspection route and
/// document (finding-diff-any.md#library, adoption step 9). The CLI and the
/// Browser each bind to <see cref="Route"/>; neither host type appears here.
/// </summary>
public static class FindingDiffAnyLibraryCapability
{
    public const string DocumentIdentity = "finding-diff-any/document/v1";

    public const string RouteIdentity = "finding-diff-any/route/library";

    public const string ProductModuleIdentity = "diff.finding-diff-any";

    /// <summary>The Library-level document: reported Types, counts, and receipt.</summary>
    public static InspectionDocumentRegistration<FindingDiffAnyLibraryDocument>
        Document { get; } =
        new(
            new(
                DocumentIdentity,
                "Finding Diff Any",
                "The Types of one Library pair whose API or Body state is "
                    + "Changed or Indeterminate, with what was compared, "
                    + "per-axis counts, and the comparison's receipt.",
                FindingDiffAnyLibraryInspection.ResultContractIdentity));

    /// <summary>
    /// The Library level as an inspection route: its typed entry point runs
    /// one comparison to completion without a Query Space.
    /// </summary>
    public static InspectionRouteRegistration<
        FindingDiffAnyLibraryRequest,
        FindingDiffAnyLibraryDocument> Route { get; } =
        new(
            new(
                RouteIdentity,
                "Finding Diff Any Library",
                "Decides, for every Type of one Library pair, whether its API "
                    + "and its Body differ, stopping each axis at its first "
                    + "difference, and returns the Finding Diff Any document."),
            Document,
            static (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    FindingDiffAnyLibraryInspection.Execute(
                        request,
                        cancellationToken));
            });

    /// <summary>
    /// The host-neutral module of the Library and Type levels, each route
    /// requiring a CLI and a Browser consumer.
    /// </summary>
    public static InspectionCapabilityModule ProductModule { get; } =
        new(
            ProductModuleIdentity,
            documents: [Document, FindingDiffAnyTypeCapability.Document],
            routes: [Route, FindingDiffAnyTypeCapability.Route],
            adoptionRequirements:
            [
                new(Route, InspectionConsumerKind.Cli),
                new(Route, InspectionConsumerKind.Browser),
                new(FindingDiffAnyTypeCapability.Route, InspectionConsumerKind.Cli),
                new(FindingDiffAnyTypeCapability.Route, InspectionConsumerKind.Browser),
            ],
            routingDeclarations: static () => RoutingCache.Declarations);

    private static class RoutingCache
    {
        internal static readonly
            ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations =
            [
                new(
                    ResourceExplanationRoutingRole.Operation,
                    FindingDiffAnyLibraryCapabilityResourcePaths.Document.Value,
                    "Finding Diff Any",
                    "Inspection Capability Composition",
                    "Decides which Types of one library differ between two "
                    + "versions, on the API axis and the Body axis, stopping "
                    + "each axis at its first difference.",
                    [
                        "Which types have any diff between two versions of "
                        + "this library?",
                        "Which types changed only in their implementation?",
                    ],
                    [
                        ResourceExplanationCatalog.CapabilityDocumentKey(
                            DocumentIdentity),
                    ],
                    grain: "One row per Type with a Changed or "
                        + "Indeterminate API or Body state.",
                    scope: "The two compared versions of one library.",
                    acceptedSubjectKinds:
                    [
                        StructuralRoutingDeclarations.Identity(
                            StructuralSubjectKind.Library),
                    ]),
                new(
                    ResourceExplanationRoutingRole.Operation,
                    FindingDiffAnyLibraryCapabilityResourcePaths.TypeDocument.Value,
                    "Finding Diff Any Members",
                    "Inspection Capability Composition",
                    "Decides which Members of one Type differ between two "
                    + "versions of its library, and whether the Type's own "
                    + "declaration differs, on the API axis and the Body "
                    + "axis, reading only that Type in each version.",
                    [
                        "Which members of this type differ between two "
                        + "versions?",
                        "Did this type's own declaration change?",
                    ],
                    [
                        ResourceExplanationCatalog.CapabilityDocumentKey(
                            FindingDiffAnyTypeCapability.DocumentIdentity),
                    ],
                    grain: "One row per Member with a Changed or "
                        + "Indeterminate API or Body state, and the Type's "
                        + "residual when it has one.",
                    scope: "One Type in the two compared versions of one "
                        + "library.",
                    acceptedSubjectKinds:
                    [
                        StructuralRoutingDeclarations.Identity(
                            StructuralSubjectKind.Type),
                    ]),
            ];
    }
}

/// <summary>
/// Canonical Resource Explanation paths for Finding Diff Any's Library and
/// Type documents, their routes, and the consumer bindings that execute them.
/// </summary>
public static class FindingDiffAnyLibraryCapabilityResourcePaths
{
    public static readonly ResourcePath Document = new("finding-diff-any");

    public static readonly ResourcePath Route =
        Document.Append("routes", "library");

    /// <summary>The Type level's document; its route and bindings sit beneath it.</summary>
    public static readonly ResourcePath TypeDocument = new("finding-diff-any-members");

    public static readonly ResourcePath TypeRoute =
        TypeDocument.Append("routes", "members");

    public static ImmutableArray<
        InspectionCapabilityResourcePathRegistration> Create(
        InspectionCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var registrations =
            ImmutableArray.CreateBuilder<
                InspectionCapabilityResourcePathRegistration>();

        foreach (InspectionDocumentRegistration document
                 in catalog.Documents)
        {
            ResourcePath path =
                ReferenceEquals(document, FindingDiffAnyLibraryCapability.Document)
                    ? Document
                    : ReferenceEquals(document, FindingDiffAnyTypeCapability.Document)
                        ? TypeDocument
                        : throw new ArgumentException(
                            "Finding Diff Any Resource Explanation paths cannot "
                            + "register another inspection document.",
                            nameof(catalog));
            registrations.Add(
                new(
                    ResourceExplanationCatalog.DocumentIdentity(document),
                    path));
        }

        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            registrations.Add(
                new(
                    ResourceExplanationCatalog.RouteIdentity(route),
                    IsTypeRoute(route) ? TypeRoute : Route));
        }

        foreach (InspectionConsumerBinding binding in catalog.Bindings)
        {
            bool type = IsTypeRoute(binding.Route);
            string segment = binding.Descriptor.Kind switch
            {
                InspectionConsumerKind.Cli => "cli",
                InspectionConsumerKind.Browser => "browser",
                InspectionConsumerKind.OperationBackedSection =>
                    "operation-backed-section",
                _ => throw new InvalidOperationException(
                    "Unknown Finding Diff Any consumer kind."),
            };
            registrations.Add(
                new(
                    ResourceExplanationCatalog.ConsumerBindingIdentity(
                        binding),
                    (type ? TypeDocument : Document).Append("bindings", segment)));
        }

        return registrations.ToImmutable();
    }

    private static bool IsTypeRoute(InspectionRouteRegistration route)
        => ReferenceEquals(route, FindingDiffAnyTypeCapability.Route)
            || (ReferenceEquals(route, FindingDiffAnyLibraryCapability.Route)
                ? false
                : throw new ArgumentException(
                    "Finding Diff Any Resource Explanation paths cannot "
                    + "register another inspection route.",
                    nameof(route)));
}
