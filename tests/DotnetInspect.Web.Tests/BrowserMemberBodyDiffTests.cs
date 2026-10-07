using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Source;
using ILInspector.Research;

namespace DotnetInspect.Web.Tests;

[Collection("Type source operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserMemberBodyDiffTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AspireHosting_MemberBodyInventoryResolvesPublicPopulation()
    {
        var request = new BrowserMemberBodyDiffRequest("Aspire.Hosting", "13.6.0", "13.6.1",
            "net8.0", "compile:lib/net8.0/Aspire.Hosting.dll", Guid.NewGuid().ToString());
        string json = await SourceExports.QueryMemberBodyDiff(Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffRequest));
        var result = JsonSerializer.Deserialize(json, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult)!;
        Assert.True(result.Kind == "Available", $"{result.Kind}: {result.Detail}");
        Assert.NotNull(result.Inventory);
        Assert.NotNull(result.Inspection);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task JsonDocumentOptionsAddedProperty_FitsWorkerTransport()
    {
        var request = new BrowserMemberBodyDiffRequest("System.Text.Json", "9.0.20", "10.0.12",
            "netstandard2.0", "compile:lib/netstandard2.0/System.Text.Json.dll", Guid.NewGuid().ToString());
        string json = await SourceExports.QueryMemberBodyDiff(Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffRequest));
        var inventory = JsonSerializer.Deserialize(json, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult)!;
        Assert.True(inventory.Kind == "Available", $"{inventory.Kind}: {inventory.Detail}");
        Assert.NotNull(inventory.Inspection);
        Assert.True(inventory.Inspection.Content.TryGetProperty("implementation", out _));
        Assert.True(inventory.Inspection.Content.TryGetProperty("api", out _));
        using var wire = JsonDocument.Parse(json);
        long characters = BrowserOrdinaryWorkerJsonBudget.JsonStringifyCharacters(wire.RootElement) + 2;
        long entries = BrowserOrdinaryWorkerJsonBudget.CollectionEntries(wire.RootElement) + 2;
        Assert.True(characters > 16_777_216 || entries > 524_288);
        Assert.InRange(characters, 0, BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerTransportJsonCharacters);
        Assert.InRange(entries, 0, BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerTransportCollectionEntries);
        var type = Assert.Single(inventory.Inventory!.Types, type =>
            type.Identity == "System.Text.Json.JsonDocumentOptions");
        var property = Assert.Single(type.Members, member => member.Display.Contains("AllowDuplicateProperties", StringComparison.Ordinal)
            && member.Selector!.EndsWith(":1", StringComparison.Ordinal));
        string memberJson = await SourceExports.QueryMemberBodyDiff(Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request with { InventoryId = inventory.Inventory.Id, MemberId = property.Id },
                BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffRequest));
        var result = JsonSerializer.Deserialize(memberJson, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult)!;
        Assert.True(result.Kind == "Available", $"{result.Kind}: {result.Detail}");
        Assert.Equal("Absent", result.Document!.BeforeOutcome);
        Assert.All(result.Document.Media, medium => Assert.NotEmpty(medium.Diff!.Changes));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task JsonOptionsCopyConstructor_OpensBothMediaFromRetainedInventory()
    {
        const string package = "System.Text.Json";
        const string before = "11.0.0-preview.6.26359.118";
        const string after = "11.0.0-preview.7.26381.103";
        const string framework = "net10.0";
        await using var lease = await BrowserPackageWorkspace.OpenScopeAsync(package, after, framework,
            TestContext.Current.CancellationToken);
        string asset = lease.Scope.Coordinates[0].CompileAsset("System.Text.Json.dll").Id;
        var request = new BrowserMemberBodyDiffRequest(package, before, after, framework, asset, Guid.NewGuid().ToString());
        var inventory = await SourceExports.QueryMemberBodyDiffCore(request, TestContext.Current.CancellationToken);
        Assert.Equal("Available", inventory.Kind);
        Assert.NotNull(inventory.Inspection);
        var type = Assert.Single(inventory.Inventory!.Types, type =>
            type.Identity == "System.Text.Json.JsonSerializerOptions");
        var constructor = Assert.Single(type.Members, member => member.Display.Contains("JsonSerializerOptions)", StringComparison.Ordinal)
            && member.Display.Contains("#ctor", StringComparison.Ordinal));
        Assert.NotNull(constructor.Fingerprint);
        Assert.NotNull(constructor.MethodToken);
        var result = await SourceExports.QueryMemberBodyDiffCore(request with
        {
            InventoryId = inventory.Inventory.Id, MemberId = constructor.Id,
        }, TestContext.Current.CancellationToken);
        Assert.Equal("Available", result.Kind);
        Assert.NotNull(result.Document);
        Assert.Equal(["CSharp", "Il"], result.Document.Media.Select(medium => medium.Medium));
        Assert.Contains(result.Document.Media[0].Diff!.After.Lines,
            line => line.Contains("_inferClosedTypePolymorphism", StringComparison.Ordinal));
        Assert.NotEmpty(result.Document.Media[0].Diff!.Changes);
        Assert.NotEmpty(result.Document.Media[1].Diff!.Changes);
        var owned = AnnotatedSourceDiffJson.Deserialize(result.Inspection!.Content.GetProperty("document").GetRawText());
        Assert.Equal(AnnotatedSourceDiffSideOutcomeKind.Present, owned.Before.Outcome);
        Assert.Equal(AnnotatedSourceDiffSideOutcomeKind.Present, owned.After.Outcome);
        Assert.True(inventory.Inspection!.Content.TryGetProperty("implementation", out _));
        Assert.True(inventory.Inspection.Content.TryGetProperty("api", out _));
        var addition = Assert.Single(type.Members, member => member.Display.Contains("InferClosedTypePolymorphism", StringComparison.Ordinal)
            && member.Selector!.EndsWith(":1", StringComparison.Ordinal));
        var added = await SourceExports.QueryMemberBodyDiffCore(request with
        {
            InventoryId = inventory.Inventory.Id, MemberId = addition.Id,
        }, TestContext.Current.CancellationToken);
        Assert.Equal("Available", added.Kind);
        Assert.Equal("Absent", added.Document!.BeforeOutcome);
        Assert.Equal("Present", added.Document.AfterOutcome);
        Assert.All(added.Document.Media, medium =>
        {
            Assert.NotNull(medium.Diff);
            Assert.NotEmpty(medium.Diff.Changes);
        });
        var stale = await SourceExports.QueryMemberBodyDiffCore(request with
        {
            Generation = "another-generation", InventoryId = inventory.Inventory.Id, MemberId = constructor.Id,
        }, TestContext.Current.CancellationToken);
        Assert.Equal("Unavailable", stale.Kind);
        Assert.Null(stale.Document);
    }

    [Fact]
    public async Task InvalidRequest_ReportsFailureInsteadOfEmptyInventory()
    {
        var request = new BrowserMemberBodyDiffRequest("Example", "invalid", "1.0.0", "net10.0", "asset", "generation");
        string json = await SourceExports.QueryMemberBodyDiff(Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffRequest));
        var result = JsonSerializer.Deserialize(json, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult)!;
        Assert.Equal("Failed", result.Kind);
        Assert.Null(result.Inventory);
        Assert.Null(result.Document);
    }
}
