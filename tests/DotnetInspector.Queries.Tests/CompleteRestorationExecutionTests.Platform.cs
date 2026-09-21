using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedPlatformSharedContext_RestoresExactPackageAssociation(
        bool reverseContexts)
    {
        PackageFixture package = await SystemTextJsonAndPlatformPackagesAsync();

        int mixedContextIndex = reverseContexts ? 1 : 0;
        string contexts = reverseContexts ? "[[2],[0,1]]" : "[[0,1],[2]]";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            $$$"""
            {"f":3,"t":[[":Platform","10.0.10","net10.0","linux-x64"],["System.Text.Json","9.0.4","net10.0","linux-x64"],["System.Text.Json","9.0.4","net9.0",null]],"g":{{{contexts}}},"r":[],"a":1,"x":{{{mixedContextIndex}}},"v":[{"t":null,"u":{"k":"workspace"}},{"t":0},{"t":1,"r":{"k":"package"},"u":{"k":"package"}},{"t":2,"r":{"k":"package"},"u":{"k":"package"}}]}
            """,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    encoded,
                    authority));
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new TestHost(),
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        if (result is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
            Assert.Fail($"{failed.Failure.GetType().Name}: {failed.Failure.Message}");
        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        await using InspectionWorkspace workspace = activated.Activation;
        NavigationConsumerSnapshot navigation =
            activated.Workspace.Snapshot.Navigation.State.Snapshot;
        Assert.Equal(StructuralSubjectKind.Package, navigation.ActiveSubject.Kind);
        NavigationConsumerPackageDescriptor active = Assert.Single(
            navigation.Packages,
            package => package.Subject.Id == navigation.ActiveSubject.Id);
        Assert.Equal("System.Text.Json", active.PackageId, ignoreCase: true);
        Assert.Equal("9.0.4", active.Version);
        Assert.Equal("net9.0", active.Framework);
        Assert.True(active.IsCurrent);
        var resolved = Assert.IsType<CompleteRestorationResolvedState.Version3>(
            activated.Workspace.Snapshot.Resolved);
        var activeSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.States[resolved.ActiveStateIndex!.Value]
                    .Initialization!.Subject);
        Assert.Equal("net10.0", activeSubject.Descriptor.Coordinate.Framework);
        Assert.Equal(
            "linux-x64",
            activeSubject.Descriptor.Coordinate.RuntimeIdentifier);

        var mixed = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            activated.Workspace.Contexts[mixedContextIndex].ContextLoadOutcome);
        RealizedMemberCoordinate.Platform[] platforms =
        [
            .. mixed.Members.Select(member => member.Realized)
                .OfType<RealizedMemberCoordinate.Platform>()
                .Distinct(),
        ];
        Assert.NotEmpty(platforms);
        Assert.All(platforms, platform =>
        {
            Assert.Equal("runtime", platform.Family);
            Assert.Equal("10.0.10", platform.Version);
            Assert.Equal("net10.0", platform.Framework);
        });
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(mixed.PackageRoots).Root.PackageId,
            ignoreCase: true);
        var neighbor = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            activated.Workspace.Contexts[1 - mixedContextIndex]
                .ContextLoadOutcome);
        Assert.Equal("net9.0", neighbor.Framework);
        Assert.DoesNotContain(
            neighbor.Members,
            member => member.Realized is RealizedMemberCoordinate.Platform);
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(neighbor.PackageRoots).Root.PackageId,
            ignoreCase: true);
        var neighborSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.States[3].Initialization!.Subject);
        Assert.Equal("net9.0", neighborSubject.Descriptor.Coordinate.Framework);
        Assert.Equal(
            encoded,
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket);
    }

    private static async Task<PackageFixture>
        SystemTextJsonAndPlatformPackagesAsync()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        foreach (string packageId in new[]
        {
            "Microsoft.NETCore.App.Ref",
            "Microsoft.NETCore.App.Runtime.linux-x64",
        })
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "CompleteRestoration",
                $"{packageId.ToLowerInvariant()}.10.0.10.nupkg");
            await using FileStream content = File.OpenRead(path);
            await package.Store.CommitAsync(
                packageId,
                "10.0.10",
                NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
                content,
                TestContext.Current.CancellationToken);
        }
        return package;
    }
}
