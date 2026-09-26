using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
internal static class BrowserCapabilityCatalogSearch
{
    internal static InspectionEnvelope<CapabilityCatalogSearchDocument> Search(
        string text,
        int maximumResults)
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCapabilityBinding.Module,
                ]);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                PackageQueryCapabilityResourcePaths.Create(catalog));
        return CapabilityCatalogSearch.Search(
            catalog,
            explanation,
            new(text, maximumResults));
    }
}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static string SearchCapabilities(
        string text,
        int maximumResults)
    {
        InspectionEnvelope<CapabilityCatalogSearchDocument> envelope =
            BrowserCapabilityCatalogSearch.Search(text, maximumResults);
        BrowserCapabilityCatalogSearchInspection inspection =
            Project(envelope);
        return JsonSerializer.Serialize(
            inspection,
            BrowserPackageJsonContext.Default
                .BrowserCapabilityCatalogSearchInspection);
    }

    private static BrowserCapabilityCatalogSearchInspection Project(
        InspectionEnvelope<CapabilityCatalogSearchDocument> envelope) =>
        new(
            new BrowserCapabilityCatalogSearchDocument(
                envelope.Content.Query,
                envelope.Content.SimilarityThreshold,
                envelope.Content.CandidateResourceCount,
                envelope.Content.MatchCount,
                envelope.Content.ReturnedCount,
                envelope.Content.IsTruncated,
                [.. envelope.Content.Results.Select(Project)]),
            Project(envelope.Share),
            [.. envelope.Diagnostics.Select(BrowserPackageWireProjection.Project)]);

    private static BrowserCapabilityCatalogSearchShare Project(
        InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                new(
                    BrowserCapabilityCatalogSearchShareKind.Available,
                    available.FullUrl,
                    available.Packet,
                    Path: null,
                    Reason: null),
            InspectionShare.NonProjectable nonProjectable =>
                new(
                    BrowserCapabilityCatalogSearchShareKind.NonProjectable,
                    FullUrl: null,
                    Packet: null,
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown capability-search Share outcome."),
        };

    private static BrowserCapabilityCatalogSearchResult Project(
        CapabilityCatalogSearchResult result) =>
        new(
            result.Similarity,
            result.MatchedTerm,
            result.MatchSource switch
            {
                CapabilityCatalogSearchMatchSource.CanonicalKey =>
                    BrowserCapabilityCatalogSearchMatchSource.CanonicalKey,
                CapabilityCatalogSearchMatchSource.OwnerIdentity =>
                    BrowserCapabilityCatalogSearchMatchSource.OwnerIdentity,
                CapabilityCatalogSearchMatchSource.ResourcePath =>
                    BrowserCapabilityCatalogSearchMatchSource.ResourcePath,
                CapabilityCatalogSearchMatchSource.ResourceName =>
                    BrowserCapabilityCatalogSearchMatchSource.ResourceName,
                CapabilityCatalogSearchMatchSource.Summary =>
                    BrowserCapabilityCatalogSearchMatchSource.Summary,
                CapabilityCatalogSearchMatchSource.RelatedRoute =>
                    BrowserCapabilityCatalogSearchMatchSource.RelatedRoute,
                CapabilityCatalogSearchMatchSource.ProductionBinding =>
                    BrowserCapabilityCatalogSearchMatchSource.ProductionBinding,
                _ => throw new InvalidOperationException(
                    "Unknown capability-search match source."),
            },
            result.IsSegment,
            new BrowserCapabilityResourceIdentity(
                result.ResourceIdentity.Kind switch
                {
                    InspectionCapabilityResourceKind.Document =>
                        BrowserCapabilityResourceKind.Document,
                    InspectionCapabilityResourceKind.Route =>
                        BrowserCapabilityResourceKind.Route,
                    InspectionCapabilityResourceKind.QuerySpace =>
                        BrowserCapabilityResourceKind.QuerySpace,
                    InspectionCapabilityResourceKind.QueryFacet =>
                        BrowserCapabilityResourceKind.QueryFacet,
                    InspectionCapabilityResourceKind.ConsumerBinding =>
                        BrowserCapabilityResourceKind.ConsumerBinding,
                    _ => throw new InvalidOperationException(
                        "Unknown capability resource kind."),
                },
                result.ResourceIdentity.Identity,
                result.ResourceIdentity.ParentIdentity),
            result.ResourceKind switch
            {
                ResourceExplanationResourceKind.Catalog =>
                    BrowserResourceExplanationResourceKind.Catalog,
                ResourceExplanationResourceKind.NavigationCollection =>
                    BrowserResourceExplanationResourceKind.NavigationCollection,
                ResourceExplanationResourceKind.StructuralCategory =>
                    BrowserResourceExplanationResourceKind.StructuralCategory,
                ResourceExplanationResourceKind.StructuralSection =>
                    BrowserResourceExplanationResourceKind.StructuralSection,
                ResourceExplanationResourceKind.StructuralItem =>
                    BrowserResourceExplanationResourceKind.StructuralItem,
                ResourceExplanationResourceKind.InspectionDocument =>
                    BrowserResourceExplanationResourceKind.InspectionDocument,
                ResourceExplanationResourceKind.HostNeutralRoute =>
                    BrowserResourceExplanationResourceKind.HostNeutralRoute,
                ResourceExplanationResourceKind.QuerySpace =>
                    BrowserResourceExplanationResourceKind.QuerySpace,
                ResourceExplanationResourceKind.QueryFacet =>
                    BrowserResourceExplanationResourceKind.QueryFacet,
                ResourceExplanationResourceKind.ConsumerBinding =>
                    BrowserResourceExplanationResourceKind.ConsumerBinding,
                _ => throw new InvalidOperationException(
                    "Unknown Resource Explanation resource kind."),
            },
            result.ResourceName,
            [.. result.CanonicalKeys],
            result.ResourcePath,
            [
                .. result.OwningRoutes.Select(route =>
                    new BrowserCapabilityCatalogSearchRoute(
                        route.Identity,
                        route.Name,
                        route.ResourcePath)),
            ],
            [
                .. result.ProductionBindings.Select(binding =>
                    new BrowserCapabilityCatalogSearchBinding(
                        binding.Identity,
                        binding.Name,
                        binding.ConsumerKind switch
                        {
                            InspectionConsumerKind.Cli =>
                                BrowserInspectionConsumerKind.Cli,
                            InspectionConsumerKind.Browser =>
                                BrowserInspectionConsumerKind.Browser,
                            InspectionConsumerKind.OperationBackedSection =>
                                BrowserInspectionConsumerKind.OperationBackedSection,
                            _ => throw new InvalidOperationException(
                                "Unknown inspection consumer kind."),
                        },
                        binding.Gesture,
                        binding.ResourcePath)),
            ]);
}
