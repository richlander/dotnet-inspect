using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    public static TheoryData<string, string, string> RestoredRidCases()
    {
        using JsonDocument cases = JsonDocument.Parse(
            File.ReadAllBytes(FixtureCatalog.RestoredRidAssets.AssetPath("rid-cases.json")));
        var data = new TheoryData<string, string, string>();
        foreach (JsonElement item in cases.RootElement.EnumerateArray())
        {
            data.Add(
                item.GetProperty("rid").GetString()!,
                item.GetProperty("sdkRuntimeAsset").GetString()!,
                item.GetProperty("workspaceRuntimeAsset").GetString()!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(RestoredRidCases))]
    public async Task RestoredAssets_CreatesWorkspaceWithDocumentedRidSelection(
        string rid,
        string expectedSdkRuntimeAsset,
        string expectedWorkspaceRuntimeAsset)
    {
        const string framework = "net10.0";
        const string packageId = "System.Security.Cryptography.Pkcs";
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] bytes = await File.ReadAllBytesAsync(
            FixtureCatalog.RestoredRidAssets.AssetPath("project.assets.json"), token);
        RestoredProjectDependencyFacts facts =
            Assert.IsType<RestoredProjectDependencyFactsResult.Available>(
                RestoredProjectDependencyFactsQuery.Execute(
                    bytes, new RestoredProjectTargetRequest(framework, rid))).Value;
        Assert.NotNull(facts.SelectedTarget);
        Assert.Equal(framework, facts.SelectedTarget.FrameworkIdentity);
        Assert.Equal(rid, facts.SelectedTarget.RuntimeIdentifierIdentity);
        var graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        RestoredProjectPackageNode node = Assert.Single(
            graph.Packages,
            package => package.Identity.Coordinate.PackageId.Equals(
                packageId, StringComparison.OrdinalIgnoreCase));
        string version = node.Identity.Coordinate.Version;

        using JsonDocument assets = JsonDocument.Parse(bytes);
        JsonElement sdkPackage = assets.RootElement.GetProperty("targets")
            .GetProperty($"{framework}/{rid}")
            .GetProperty($"{packageId}/{version}");
        string sdkCompile = Assert.Single(
            sdkPackage.GetProperty("compile").EnumerateObject()).Name;
        string sdkRuntime = Assert.Single(
            sdkPackage.GetProperty("runtime").EnumerateObject()).Name;
        Assert.Equal(expectedSdkRuntimeAsset, sdkRuntime);

        var store = new InMemoryPackageStore();
        await using (FileStream archive = File.OpenRead(
            FixtureCatalog.RestoredRidAssets.AssetPath("pkcs.nupkg")))
        {
            await store.CommitAsync(
                node.Identity.Coordinate.PackageId,
                version,
                NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
                archive,
                token);
        }

        string encoded = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                $$$"""
                {"f":3,"t":[["{{{node.Identity.Coordinate.PackageId}}}","{{{version}}}","{{{framework}}}","{{{rid}}}"]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"}}]}
                """,
                token));
        var authority = new TestIntentAuthority();
        var preparation = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestorationWithCancellation(
                encoded, authority, token));
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation, authority, new TestHost(),
                Options(client, store) with { CaptureInventory = true }, token);
        if (result is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
            Assert.Fail($"{failed.Failure.GetType().Name}: {failed.Failure.Message}");
        var activated =
            Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        await using InspectionWorkspace workspace = activated.Activation;
        var loaded = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            Assert.Single(activated.Workspace.Contexts).ContextLoadOutcome);
        Assert.Equal(rid, loaded.RuntimeIdentifier);
        AssemblyContextParticipant participant = Assert.Single(loaded.Group.Participants);
        var provenance = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
            participant.Assembly.Provenance);
        Assert.Equal(expectedWorkspaceRuntimeAsset, provenance.AssetPath);
        Assert.Equal(packageId, participant.Assembly.Identity.Name);
        Assert.Equal(
            expectedWorkspaceRuntimeAsset.StartsWith("runtimes/", StringComparison.Ordinal)
                ? expectedWorkspaceRuntimeAsset.Split('/')[1] : null,
            provenance.Rid);
        var image = Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            loaded.Group.UseAssemblyImage(
                participant.Assembly, static view => view.Content.Length));
        Assert.True(image.Value > 0);
        Assert.Equal(
            encoded,
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket);
        Assert.True((await workspace.CloseAsync()).Succeeded);

        CompleteRestorationPackageInventory inventory = Assert.Single(
            Assert.IsType<CompleteRestorationInventory>(
                activated.Workspace.Snapshot.Inventory).Packages);
        Assert.Equal(framework, inventory.Package.Coordinate.Framework);
        Assert.Equal(rid, inventory.Package.Coordinate.RuntimeIdentifier);
        Assert.Equal(sdkCompile, Assert.Single(inventory.Selection.Assets).Path);
        Assert.Equal(
            expectedWorkspaceRuntimeAsset,
            Assert.Single(inventory.Selection.ImplementationAssets).Path);
        Assert.Equal(
            expectedWorkspaceRuntimeAsset,
            inventory.Selection.FindImplementationAsset(
                Assert.Single(inventory.Selection.Assets))!.Path);
        var surface = Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(inventory.Surface.Assemblies.Assemblies));
        Assert.Contains(
            surface.Value.Surface.Types,
            type => type.FullName == "System.Security.Cryptography.Pkcs.SignedCms");

        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{rid}: compile={sdkCompile}; SDK runtime={sdkRuntime}; "
            + $"Workspace runtime={provenance.AssetPath}; "
            + $"SDK agreement={sdkRuntime == provenance.AssetPath}");
    }
}
