using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    // PR-fast: canonicalizes the actual CLI Member packet through the Browser codec.
    // The retained root owner admits Workspace/Package views; deep Member activation
    // remains outside that owner's supported entry scope.
    [Fact]
    public async Task MemberDerivedPacket_IsCanonicalForTheBrowserCodec()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "docs", "examples", "workspace-member-derived.json"),
            TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken));
        var result = DotnetInspect.Web.Interop.Catalog.BrowserWorkspaceShareOperations.Canonicalize(packet);
        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(packet, result.Packet);
    }

    // PR-fast: use the production retained-activation owner with opt-in handoff
    // capture; its existing Package presentation and current occurrence agree.
    [Fact]
    public async Task SelectedSubjectCapture_PreservesBrowserPackageActivation()
    {
        CompleteRestorationExecutionOptions options =
            (await OptionsAsync()) with { CaptureSelectedSubject = true };
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        string packet = Packet(["System.Text.Json"], selectedIndex: 0,
            packageVersion: "9.0.4", framework: "net9.0",
            packageFacet: "package.libraries");
        BrowserRetainedWorkspacePosting posting = await ActivateAsync(owner, "saved-package", packet);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "saved-package");
        Assert.Equal(packet, posting.CanonicalPacket);
        Assert.Equal("System.Text.Json", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.PackageId, ignoreCase: true);
        Assert.Equal("package.libraries", posting.Navigation.Snapshot.LensOutcome.EffectiveLens?.Facet);
    }

    // PR-fast: recorded CLI output plus the existing pinned NuGet payload.
    // workspace ecosystem add runtime; package System.Text.Json@10.0.0 --workspace PACKET --share packet
    [Theory]
    [InlineData("workspace-package-derived.json")]
    [InlineData("workspace-package-replay.json")]
    public async Task PackageAddAndActivate_ReopensRecordedCliPacket(string exampleFile)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "docs", "examples", exampleFile),
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
        WorkspaceSharePacket restored = WorkspaceSharePacketCodec.Decode(
            posting.CanonicalPacket!, TestContext.Current.CancellationToken);
        Assert.Equal("package.libraries", posting.Navigation.Snapshot.LensOutcome.EffectiveLens?.Facet);
        Assert.Equal(Assert.Single(restored.Tabs).Framework,
            Assert.Single(operation.Scope.Packages).Occurrence.Package.Coordinate.Framework);
        if (exampleFile == "workspace-package-derived.json")
            Assert.Equal("ecosystem.runtime", Assert.IsType<WorkspaceRegistration.Ecosystem>(
                Assert.Single(restored.Registrations)).Declaration.Id.Value);
        else
            Assert.Equal("https://api.nuget.org/v3/index.json",
                Assert.Single(restored.PackageSources).Endpoint);
    }
}
