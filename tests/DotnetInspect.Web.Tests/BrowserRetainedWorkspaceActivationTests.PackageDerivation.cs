using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    // PR-fast: recorded CLI output plus the existing pinned NuGet payload.
    // workspace ecosystem add runtime; package System.Text.Json@10.0.0 --workspace PACKET --share packet
    [Fact]
    public async Task PackageAddAndActivate_ReopensRecordedCliPacket()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "docs", "examples", "workspace-package-derived.json"),
            TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken));
        byte[] archive = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "FrameworkActivation",
            "system.text.json.10.0.0.nupkg"), TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            "System.Text.Json", "10.0.0", archive, fromCache: false,
            producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspacePosting posting = await ActivateAsync(owner, "package-derived", packet);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "package-derived");

        Assert.Equal(packet, posting.CanonicalPacket);
        Assert.Equal("system.text.json", Assert.Single(
            posting.Navigation.Snapshot.Packages, item => item.IsCurrent).PackageId,
            ignoreCase: true);
        Assert.Equal("10.0.0", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.Coordinate.Version);
        Assert.Equal("ecosystem.runtime", Assert.IsType<WorkspaceRegistration.Ecosystem>(
            Assert.Single(WorkspaceSharePacketCodec.Decode(posting.CanonicalPacket!, TestContext.Current.CancellationToken).Registrations))
            .Declaration.Id.Value);
    }
}
