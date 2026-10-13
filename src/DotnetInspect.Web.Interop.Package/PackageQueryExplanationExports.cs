using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Package;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageQueryExplanationOutcome>))]
public enum BrowserPackageQueryExplanationOutcome
{
    Explained,
    InvalidPath,
    Unknown,
    InvalidSelection,
}

public sealed record BrowserPackageQueryExplanationResult(
    BrowserPackageQueryExplanationOutcome Outcome,
    JsonElement? Content,
    string? Message);

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    private static readonly Lazy<ResourceExplanationCatalog> QueryExplanationCatalog = new(() =>
    {
        InspectionCapabilityCatalog catalog = BrowserCapabilityCatalogSearch.CreateCatalog();
        return ResourceExplanationCatalog.CreateCapabilities(catalog,
            PackageQueryCapabilityResourcePaths.Create(catalog));
    });

    /// <summary>
    /// Reads installed query knowledge without planning or acquiring packages.
    /// Null keys select the whole root; an explicit array selects exact facet keys.
    /// HAL addresses are operands for this export, not HTTP endpoints.
    /// </summary>
    [JSExport]
    public static string ExplainPackageQuery(string path,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[]? facetKeys)
    {
        BrowserPackageQueryExplanationResult result = ExplainPackageQueryCore(path, facetKeys);
        return JsonSerializer.Serialize(result,
            BrowserPackageJsonContext.Default.BrowserPackageQueryExplanationResult);
    }

    internal static BrowserPackageQueryExplanationResult ExplainPackageQueryCore(string path, string[]? keys)
    {
        if (keys?.Any(static key => key is null) == true)
            return new(BrowserPackageQueryExplanationOutcome.InvalidSelection, null, "Facet keys must be strings.");
        string resourcePath = path ?? "";
        bool hal = true;
        const string prefix = "inspect-resource:/";
        if (resourcePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            resourcePath = resourcePath[prefix.Length..];
            int query = resourcePath.IndexOf('?');
            if (query >= 0)
            {
                string projection = resourcePath[(query + 1)..];
                if (projection is not ("projection=hal" or "projection=data"))
                    return new(BrowserPackageQueryExplanationOutcome.InvalidSelection, null,
                        "Query reading addresses support projection=hal or projection=data.");
                hal = projection == "projection=hal";
                resourcePath = resourcePath[..query];
            }
        }

        ResourceExplanationCatalog catalog = QueryExplanationCatalog.Value;
        ResourcePathResolution resolution = catalog.Resolve(resourcePath);
        if (resolution is ResourcePathResolution.Invalid invalid)
            return new(BrowserPackageQueryExplanationOutcome.InvalidPath, null, invalid.Reason);
        if (resolution is not ResourcePathResolution.Resolved resolved)
            return new(BrowserPackageQueryExplanationOutcome.Unknown, null,
                "The installed Package Query resource path was not found.");
        try
        {
            JsonElement content = ResourceExplanationDataset.Create(catalog, resolved, keys).ToJson(
                static resource => "inspect-resource:/" + resource.Value,
                hal: hal,
                bindHalAddress: static resource => "inspect-resource:/" + resource.Value + "?projection=hal");
            return new(BrowserPackageQueryExplanationOutcome.Explained, content, null);
        }
        catch (InvalidOperationException exception)
        {
            return new(BrowserPackageQueryExplanationOutcome.InvalidSelection, null, exception.Message);
        }
    }
}
