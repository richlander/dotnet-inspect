using DotnetInspector.Sections;

namespace DotnetInspector.Diff;

/// <summary>
/// Finding Diff Any's Type level as a host-neutral inspection route and
/// document (finding-diff-any.md#type, diff-capabilities.md ladder step 6.3).
/// The CLI and the Browser each bind to <see cref="Route"/>; neither host type
/// appears here. <see cref="FindingDiffAnyLibraryCapability.ProductModule"/>
/// registers it beside the Library level.
/// </summary>
public static class FindingDiffAnyTypeCapability
{
    public const string DocumentIdentity = "finding-diff-any/member-document/v1";

    public const string RouteIdentity = "finding-diff-any/route/members";

    /// <summary>The Type-level document: reported Members and residual, counts, and receipt.</summary>
    public static InspectionDocumentRegistration<FindingDiffAnyTypeDocument>
        Document { get; } =
        new(
            new(
                DocumentIdentity,
                "Finding Diff Any Members",
                "The Members of one Type of one Library pair, and the Type's "
                    + "residual, whose API or Body state is Changed or "
                    + "Indeterminate, with what was compared, per-axis "
                    + "counts, and the comparison's receipt.",
                FindingDiffAnyTypeInspection.ResultContractIdentity));

    /// <summary>
    /// The Type level as an inspection route: its typed entry point pairs the
    /// addressed Type by its identity and runs one comparison to completion
    /// without a Query Space.
    /// </summary>
    public static InspectionRouteRegistration<
        FindingDiffAnyTypeRequest,
        FindingDiffAnyTypeDocument> Route { get; } =
        new(
            new(
                RouteIdentity,
                "Finding Diff Any Members",
                "Decides, for every Member of one Type of one Library pair "
                    + "and for the Type's residual, whether its API and its "
                    + "Body differ, reading only that Type in each version, "
                    + "and returns the Finding Diff Any Members document."),
            Document,
            static (request, cancellationToken) =>
                FindingDiffAnyTypeInspection.ExecuteAsync(
                    request,
                    cancellationToken));
}
