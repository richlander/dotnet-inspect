using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Web.Tests;

// PR-fast: resource-free facade reads over the same captures as the CLI.
[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserComponentExplanationTests : IDisposable
{
    private int requests;

    public BrowserComponentExplanationTests() =>
        CoreHttpClientFactory.SetAuthenticationDecorator(
            handler => new RefuseNetwork(handler, () => requests++));

    public void Dispose() => CoreHttpClientFactory.SetAuthenticationDecorator(null);

    [Fact]
    public void RootReachesEveryAdmittedResourceAndContractLinksRemainLocal()
    {
        var composition = CatalogExports.CreateExplanationRoot();
        Assert.True(composition.IsPublishable);
        Assert.Empty(composition.Unreachable);
        var root = CatalogExports.ExplainResourcesCore("", "hal");
        Assert.Equal("Explained", root.Outcome);
        JsonElement content = root.Explanation!.Content;
        Assert.Equal("_links", content.EnumerateObject().First().Name);
        Assert.DoesNotContain("?projection=", content.GetRawText());
        Assert.DoesNotContain("maximum_values", content.GetRawText());
        foreach (var resource in composition.Catalog.Resources.DistinctBy(r => r.ResourceType))
        {
            Assert.NotNull(resource.Path);
            Assert.Equal("Explained", CatalogExports.ExplainResourcesCore(
                "inspect-resource:/" + resource.Path.Value, "hal").Outcome);
        }
        var collection = CatalogExports.ExplainResourcesCore("component-contracts", "hal");
        string href = collection.Explanation!.Content.GetProperty("_links")
            .GetProperty("collection-component-contract")[0].GetProperty("href").GetString()!;
        Assert.Equal("inspect-resource:/workspace/components/package", href);
        var contract = CatalogExports.ExplainResourcesCore(href, "hal");
        Assert.Equal(8, contract.Explanation!.Content.GetProperty("_embedded").GetProperty("data")
            .GetProperty("facts").GetProperty("fields").GetArrayLength());
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("data", "explain-package-declaration_direct.json")]
    [InlineData("hal", "explain-package-declaration_hal.json")]
    public async Task StandaloneContentEqualsTheCliCapture(string projection, string capture)
    {
        string json = await ReadExample("workspace-package-component.json");
        var result = CatalogExports.ExplainWorkspaceComponentCore(json, null, projection);
        Assert.Equal("Explained", result.Outcome);
        Assert.Null(result.Error);
        using JsonDocument expected = JsonDocument.Parse(await ReadExample(capture));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, result.Explanation!.Content));
        Assert.IsType<BrowserVocabularyNonProjectableShare>(result.Explanation.Share);
        Assert.Empty(result.Explanation.Diagnostics);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task InventoryPassesExactOwnerDeclarationsToQualifiedExplanation()
    {
        string packet = await ParentPacket();
        var inventory = CatalogExports.DescribeWorkspaceComponentsCore(packet);
        Assert.Equal("Described", inventory.Outcome);
        Assert.Null(inventory.Error);
        var component = Assert.Single(inventory.Components);
        using JsonDocument actual = JsonDocument.Parse(component.Declaration);
        using JsonDocument expected = JsonDocument.Parse(await ReadExample("workspace-package-component.json"));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        var result = CatalogExports.ExplainWorkspaceComponentCore(component.Declaration, packet, "hal");
        using JsonDocument qualified = JsonDocument.Parse(await ReadExample("explain-package-occurrence_hal.json"));
        Assert.True(JsonElement.DeepEquals(qualified.RootElement, result.Explanation!.Content));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ExportRoundTripsTheCompletedEnvelopeAndTypedDocument()
    {
        string json = await ReadExample("workspace-package-component.json");
        var wire = JsonSerializer.Deserialize(CatalogExports.ExplainWorkspaceComponent(json, null, "contract"),
            BrowserCatalogJsonContext.Default.BrowserResourceExplanationResult)!;
        var shared = WorkspaceComponentExplanationOperation.Explain(
            WorkspacePackageComponentExplanation.Parse(json));
        using JsonDocument expected = JsonDocument.Parse(JsonSerializer.Serialize(shared.Content,
            ResourceExplanationJsonContext.Default.ResourceExplanationDocument));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, wire.Explanation!.Content));
        var share = Assert.IsType<BrowserVocabularyNonProjectableShare>(wire.Explanation.Share);
        var sharedShare = Assert.IsType<InspectionShare.NonProjectable>(shared.Share);
        Assert.Equal(sharedShare.Path, share.Path);
        Assert.Equal(sharedShare.Reason.ToString(), share.Reason);
        Assert.Equal(shared.Diagnostics.Length, wire.Explanation.Diagnostics.Length);
        Assert.Equal("Described", JsonSerializer.Deserialize(
            CatalogExports.DescribeWorkspaceComponents(await ParentPacket()),
            BrowserCatalogJsonContext.Default.BrowserWorkspaceComponentInventory)!.Outcome);
        Assert.Equal("Explained", JsonSerializer.Deserialize(CatalogExports.ExplainResources("root", "hal"),
            BrowserCatalogJsonContext.Default.BrowserResourceExplanationResult)!.Outcome);
    }

    [Theory]
    [InlineData("contract", "unknown", "UnsupportedContract")]
    [InlineData("schema_version", "2", "UnsupportedVersion")]
    public async Task OwnerFailuresRetainTheirTypedOutcome(string field, string replacement, string outcome)
    {
        string json = await ReadExample("workspace-package-component.json");
        json = field == "contract"
            ? json.Replace("workspace/components/package", replacement)
            : json.Replace("\"schema_version\": 1", "\"schema_version\": " + replacement);
        var result = CatalogExports.ExplainWorkspaceComponentCore(json, null, "hal");
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.NotEmpty(result.Error!);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ParentClaimsAreValidatedAndDifferentPacketsDoNotCollapseIdentity()
    {
        string json = await ReadExample("workspace-package-component.json");
        string packet = await ParentPacket();
        var mismatch = CatalogExports.ExplainWorkspaceComponentCore(
            json.Replace("\"focused\": true", "\"focused\": false"), packet, "hal");
        Assert.Equal("ParentMismatch", mismatch.Outcome);
        Assert.Null(mismatch.Explanation);
        string parentJson = await ReadExample("workspace-package-replay.json");
        string other = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            parentJson.Replace("https://api.nuget.org/v3/index.json", "https://example.test/v3/index.json"),
            TestContext.Current.CancellationToken));
        string Identity(string parent) => CatalogExports.ExplainWorkspaceComponentCore(json, parent, "hal")
            .Explanation!.Content.GetProperty("_embedded").GetProperty("data")
            .GetProperty("identity").GetProperty("value").GetString()!;
        Assert.NotEqual(Identity(packet), Identity(other));
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("{", null, "hal", "InvalidInput")]
    [InlineData("{}", null, "hal", "InvalidInput")]
    [InlineData("{}", "invalid", "hal", "InvalidPacket")]
    [InlineData("{}", null, "unknown", "InvalidProjection")]
    public void InvalidInputNeverBecomesAnEmptySuccess(
        string json, string? parent, string projection, string outcome)
    {
        var result = CatalogExports.ExplainWorkspaceComponentCore(json, parent, projection);
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.NotEmpty(result.Error!);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("unknown/resource", "hal", "Unknown")]
    [InlineData("https://example.test", "hal", "InvalidPath")]
    [InlineData("root", "unknown", "InvalidProjection")]
    public void ResourceRefusalsAreVisible(string path, string projection, string outcome)
    {
        var result = CatalogExports.ExplainResourcesCore(path, projection);
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Explanation);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public void InvalidPacketHasNoPartialComponentInventory()
    {
        var result = CatalogExports.DescribeWorkspaceComponentsCore("invalid");
        Assert.Equal("InvalidPacket", result.Outcome);
        Assert.Empty(result.Components);
        Assert.NotEmpty(result.Error!);
    }

    private static async Task<string> ParentPacket() =>
        WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            await ReadExample("workspace-package-replay.json"), TestContext.Current.CancellationToken));

    private static Task<string> ReadExample(string name)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return File.ReadAllTextAsync(Path.Combine(directory.FullName, "docs", "examples", name),
                    TestContext.Current.CancellationToken);
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class RefuseNetwork(HttpMessageHandler inner, Action record) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record();
            throw new InvalidOperationException("Declaration explanation must not request Package content.");
        }
    }
}
