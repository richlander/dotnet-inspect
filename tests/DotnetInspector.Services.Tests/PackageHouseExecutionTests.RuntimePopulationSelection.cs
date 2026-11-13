using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task RuntimePopulationSelectionPrefersRuntimeOverlayToNeutralFallback()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(environment);

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());

        var selected = Assert.IsType<
            PackageRuntimePopulationChoiceResult.Selected>(
                selection.Choice);
        Assert.Equal("linux-x64", selected.RuntimeIdentifier);
        Assert.Equal(
            ["linux-x64"],
            selected.AvailableRuntimeIdentifiers);
        Assert.Equal(
            "linux-x64",
            selection.Binding.Coordinate.RuntimeIdentifier);
        PackageCompileAssetSelection assetSelection =
            Assert.IsType<PackageCompileAssetSelection>(
                selection.Binding.Root.AssetSelection);
        Assert.Equal(
            $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
            Assert.Single(assetSelection.ImplementationAssets).Path);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        RuntimePopulationSelectionUsesCompatibleRuntimeGroupOverNewerNeutralFallback()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net8.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(environment);

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());

        var selected = Assert.IsType<
            PackageRuntimePopulationChoiceResult.Selected>(
                selection.Choice);
        Assert.Equal("linux-x64", selected.RuntimeIdentifier);
        Assert.Equal("net8.0", selected.SelectedTargetFramework);
        Assert.Equal("net10.0", selection.Binding.Coordinate.Framework);
        Assert.Equal(
            "linux-x64",
            selection.Binding.Coordinate.RuntimeIdentifier);
        PackageCompileAssetSelection assetSelection =
            Assert.IsType<PackageCompileAssetSelection>(
                selection.Binding.Root.AssetSelection);
        Assert.Equal("net8.0", assetSelection.ImplementationTargetFramework);
        Assert.True(assetSelection.UsesCompatibleImplementationSelection);
        Assert.Equal(
            $"runtimes/linux-x64/lib/net8.0/{PackageId}.dll",
            Assert.Single(assetSelection.ImplementationAssets).Path);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        RuntimePopulationSelectionExcludesRuntimeAboveOwnerDefaultFramework()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net11.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(
                    environment,
                    PackageHouseTargetContext.OwnerDefault());

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());

        Assert.Null(selection.Choice);
        Assert.Null(selection.Binding.Coordinate.Framework);
        Assert.Null(selection.Binding.Coordinate.RuntimeIdentifier);
        PackageRootReacquisitionRequest rootRequest =
            selection.Binding.CreateReacquisitionRequest();
        Assert.Null(rootRequest.RequestedTargetFramework);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        PackageCompileAssetSelection assetSelection =
            Assert.IsType<PackageCompileAssetSelection>(
                selection.Binding.Root.AssetSelection);
        Assert.Equal("net10.0", assetSelection.ImplementationTargetFramework);
        Assert.Equal(
            $"lib/net10.0/{PackageId}.dll",
            Assert.Single(assetSelection.ImplementationAssets).Path);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimePopulationSelectionRetainsOwnerDefaultFramework()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net8.0/{PackageId}.dll",
                        $"lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net8.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(
                    environment,
                    PackageHouseTargetContext.OwnerDefault());

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());

        var selected = Assert.IsType<
            PackageRuntimePopulationChoiceResult.Selected>(
                selection.Choice);
        Assert.Equal("net10.0", selected.SelectedTargetFramework);
        Assert.Equal("net10.0", selection.Binding.Coordinate.Framework);
        Assert.Equal(
            "linux-x64",
            selection.Binding.Coordinate.RuntimeIdentifier);
        PackageRootReacquisitionRequest rootRequest =
            selection.Binding.CreateReacquisitionRequest();
        Assert.Null(rootRequest.RequestedTargetFramework);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Equal("net10.0", rootRequest.SelectionTargetFramework);
        Assert.False(rootRequest.AllowsCompatibleTargetSelection);
        PackageCompileAssetSelection assetSelection =
            Assert.IsType<PackageCompileAssetSelection>(
                selection.Binding.Root.AssetSelection);
        Assert.Equal(
            $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
            Assert.Single(assetSelection.ImplementationAssets).Path);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimePopulationSelectionInfersSoleRuntime()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"runtimes/unix/lib/net10.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(
                    environment,
                    PackageHouseTargetContext.OwnerDefault());

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());
        var selected = Assert.IsType<
            PackageRuntimePopulationChoiceResult.Selected>(
                selection.Choice);

        Assert.Equal("unix", selected.RuntimeIdentifier);
        Assert.Equal("net10.0", selected.SelectedTargetFramework);
        Assert.Equal("net10.0", selection.Binding.Coordinate.Framework);
        Assert.Equal("unix", selection.Binding.Coordinate.RuntimeIdentifier);
        PackageRootReacquisitionRequest rootRequest =
            selection.Binding.CreateReacquisitionRequest();
        Assert.Null(rootRequest.RequestedTargetFramework);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            selection.Binding.ContentGenerationIdentity);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimePopulationSelectionUsesProductDefaultInventory()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"runtimes/win-x64/lib/net10.0/{PackageId}.dll",
                        $"runtimes/android-arm64/lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(environment);

        PackageHouseRuntimePopulationSelection selection =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());
        var selected = Assert.IsType<
            PackageRuntimePopulationChoiceResult.Selected>(
                selection.Choice);

        Assert.Equal("linux-x64", selected.RuntimeIdentifier);
        Assert.Equal(
            ["linux-x64", "android-arm64", "win-x64"],
            selected.AvailableRuntimeIdentifiers);
        Assert.Equal(
            "linux-x64",
            selection.Binding.Coordinate.RuntimeIdentifier);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimePopulationSelectionRetainsAmbiguityAndExplicitChoice()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"runtimes/win-x64/lib/net10.0/{PackageId}.dll",
                        $"runtimes/osx-arm64/lib/net10.0/{PackageId}.dll",
                    ]));
        (
            PackageHouseSettlement.Acquired acquired,
            PackageHouseRootContribution contribution) =
                await RuntimePopulationContributionAsync(environment);

        PackageHouseRuntimePopulationSelection ambiguous =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.None());
        Assert.IsType<PackageRuntimePopulationChoiceResult.Ambiguous>(
            ambiguous.Choice);
        Assert.Null(ambiguous.Binding.Coordinate.RuntimeIdentifier);

        PackageHouseRuntimePopulationSelection selected =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.Explicit(
                    "win-x64"));
        Assert.IsType<PackageRuntimePopulationChoiceResult.Selected>(
            selected.Choice);
        Assert.Equal(
            "win-x64",
            selected.Binding.Coordinate.RuntimeIdentifier);

        PackageHouseRuntimePopulationSelection missing =
            PackageHouseRuntimePopulationSelector.Select(
                acquired,
                contribution,
                new PackageRuntimePopulationAuthority.Explicit(
                    "linux-x64"));
        Assert.IsType<PackageRuntimePopulationChoiceResult.NoMatch>(
            missing.Choice);
        Assert.Null(missing.Binding.Coordinate.RuntimeIdentifier);
        await environment.AssertRootSettledAsync();
    }

    private static async Task<(
        PackageHouseSettlement.Acquired Acquired,
        PackageHouseRootContribution Contribution)>
        RuntimePopulationContributionAsync(
            HouseEnvironment environment,
            PackageHouseTargetContext? target = null)
    {
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            target ?? PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.PackageOnly,
            assetDemand:
                PackageAssetDemand.SurfaceAndImplementation);
        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));
        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(
                    acquired)).Contribution;
        return (acquired, contribution);
    }
}
