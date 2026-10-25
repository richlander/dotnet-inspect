using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Catalog;

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    private static readonly Lazy<ResourceExplanationRootComposition> ExplanationRoot =
        new(CreateExplanationRoot);

    internal static ResourceExplanationRootComposition CreateExplanationRoot()
    {
        ResourceExplanationCatalog catalog = ResourceExplanationCatalog.Combine(
        [
            ResourceExplanationCatalog.CreateWorkspaceComponents(),
            ResourceExplanationCatalog.CreateVocabularies(BrowserVocabularyComposition.Snapshot),
        ]);
        return ResourceExplanationCatalog.ComposeRoot(catalog, new("root"),
        [
            ResourceExplanationCatalog.ComponentContractsRoutingDeclaration,
            new(ResourceExplanationRoutingRole.Reference, "vocabularies",
                "Value vocabularies", "Product Vocabulary",
                "Values accepted by this host's product queries, and their meanings.",
                ["Which values can I pass to a query, and what do they mean?"],
                [ResourceExplanationCatalog.VocabulariesCollectionKey]),
        ]);
    }

    [JSExport]
    public static string ExplainResources(string path, string projection) =>
        JsonSerializer.Serialize(ExplainResourcesCore(path, projection),
            BrowserCatalogJsonContext.Default.BrowserResourceExplanationResult);

    internal static BrowserResourceExplanationResult ExplainResourcesCore(
        string path, string projection)
    {
        if (!IsExplanationProjection(projection))
            return RejectExplanation("InvalidProjection", "Choose hal, data or contract.");
        const string prefix = "inspect-resource:/";
        string operand = path ?? "";
        if (operand.StartsWith(prefix, StringComparison.Ordinal))
            operand = operand[prefix.Length..];
        if (operand.Length == 0)
            operand = "root";
        if (!ResourcePath.TryCreate(operand, out ResourcePath? resourcePath, out string? error))
            return RejectExplanation("InvalidPath", error ?? "An exact resource path is required.");
        ResourceExplanationRootComposition root = ExplanationRoot.Value;
        if (!root.IsPublishable)
            return RejectExplanation("Unavailable", "The explanation root has unreachable resources.");
        if (!root.Catalog.TryResolveExact(resourcePath!, out ResourcePathResolution.Resolved? resolved))
            return RejectExplanation("Unknown", "The resource is not in this host's explanation catalog.");
        int depth = resourcePath!.Value == "root" ? 2
            : resourcePath.Value is ResourceExplanationCatalog.RoutingSubjectKindsSegment
                or ResourceExplanationCatalog.RoutingOperationsSegment
                or ResourceExplanationCatalog.RoutingReferencesSegment ? 1 : 0;
        var inspection = root.Catalog.Explain(resolved!, ResourceExplanationRequest.ForHost(depth));
        string[] segments = resourcePath.Value.Split('/');
        bool vocabularyReading = segments[0] == ResourceExplanationCatalog.VocabulariesCollectionSegment
            && segments.Length == 2;
        JsonElement content = projection == "contract"
            ? JsonSerializer.SerializeToElement(inspection.Content,
                ResourceExplanationJsonContext.Default.ResourceExplanationDocument)
            : vocabularyReading ? ResourceExplanationDataset.Create(root.Catalog, resolved!).ToJson(
                BindExplanationAddress, hal: projection == "hal",
                bindHalAddress: BindExplanationAddress)
            : projection == "hal"
                ? ResourceExplanationDataProjection.CreateHal(inspection.Content, BindExplanationAddress)
                : ResourceExplanationDataProjection.Create(inspection.Content, BindExplanationAddress);
        return ProjectExplanation(inspection, content);
    }

    [JSExport]
    public static string DescribeWorkspaceComponents(string packet) =>
        JsonSerializer.Serialize(DescribeWorkspaceComponentsCore(packet),
            BrowserCatalogJsonContext.Default.BrowserWorkspaceComponentInventory);

    internal static BrowserWorkspaceComponentInventory DescribeWorkspaceComponentsCore(string packet)
    {
        try
        {
            WorkspaceComponentDocument components = WorkspaceComponentCatalog.Describe(
                WorkspaceSharePacketCodec.Decode(packet ?? ""));
            return new("Described", [.. components.Packages.Select(component =>
                new BrowserWorkspaceComponentInput(component.Path.Value, component.PackageId,
                    JsonSerializer.Serialize(component,
                        BrowserWorkspaceComponentJsonContext.Default.WorkspacePackageComponentDescriptor)))], null);
        }
        catch (WorkspaceSharePacketException error)
        {
            return new("InvalidPacket", [], error.Message);
        }
    }

    [JSExport]
    public static string ExplainWorkspaceComponent(
        string json, string? parentPacket, string projection) =>
        JsonSerializer.Serialize(ExplainWorkspaceComponentCore(json, parentPacket, projection),
            BrowserCatalogJsonContext.Default.BrowserResourceExplanationResult);

    internal static BrowserResourceExplanationResult ExplainWorkspaceComponentCore(
        string json, string? parentPacket, string projection)
    {
        if (!IsExplanationProjection(projection))
            return RejectExplanation("InvalidProjection", "Choose hal, data or contract.");
        try
        {
            WorkspaceSharePacket? parent = parentPacket is null ? null
                : WorkspaceSharePacketCodec.Decode(parentPacket);
            var input = WorkspacePackageComponentExplanation.Parse(json ?? "", parent);
            var inspection = WorkspaceComponentExplanationOperation.Explain(input);
            JsonElement content = projection switch
            {
                "contract" => JsonSerializer.SerializeToElement(inspection.Content,
                    ResourceExplanationJsonContext.Default.ResourceExplanationDocument),
                "hal" => ResourceExplanationDataProjection.CreateHal(inspection.Content, BindExplanationAddress),
                _ => ResourceExplanationDataProjection.Create(inspection.Content, BindExplanationAddress),
            };
            return ProjectExplanation(inspection, content);
        }
        catch (WorkspaceComponentExplanationException error)
        {
            return RejectExplanation(error.Kind.ToString(), error.Message);
        }
        catch (WorkspaceSharePacketException error)
        {
            return RejectExplanation("InvalidPacket", error.Message);
        }
    }

    private static bool IsExplanationProjection(string projection) =>
        projection is "hal" or "data" or "contract";

    private static string BindExplanationAddress(ResourcePath path) =>
        "inspect-resource:/" + path.Value;

    private static BrowserResourceExplanationResult ProjectExplanation(
        InspectionEnvelope<ResourceExplanationDocument> inspection, JsonElement content) =>
        new("Explained", new(content, BrowserVocabulary.ToBrowserShare(inspection.Share),
            [.. inspection.Diagnostics.Select(BrowserVocabulary.ToBrowserDiagnostic)]), null);

    private static BrowserResourceExplanationResult RejectExplanation(string outcome, string message) =>
        new(outcome, null, message);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspacePackageComponentDescriptor))]
internal partial class BrowserWorkspaceComponentJsonContext : JsonSerializerContext;
