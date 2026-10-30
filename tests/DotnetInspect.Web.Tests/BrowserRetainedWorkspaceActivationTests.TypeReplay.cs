using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    // PR-fast: actual format-5 CLI packet, pinned archive and production activation owner.
    [Theory]
    [InlineData("workspace-type-replay.json", StructuralSubjectKind.Type, "type.api")]
    [InlineData("workspace-member-replay.json", StructuralSubjectKind.Member, "member.overview")]
    public async Task SubjectReplay_ReopensRecordedCliPacketWithExactSubject(
        string exampleFile, StructuralSubjectKind kind, string facet)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "docs", "examples", exampleFile),
            TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.ParseJson(
            json, TestContext.Current.CancellationToken));
        byte[] archive = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "FrameworkActivation",
            "system.text.json.10.0.0.nupkg"), TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(new BrowserPackage(
            "System.Text.Json", "10.0.0", archive, fromCache: false,
            producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        CompleteRestorationExecutionOptions options =
            (await OptionsAsync()) with { CaptureSelectedSubject = true };
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting = await ActivateAsync(owner, "saved-type", packet);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "saved-type");
        Assert.Equal(packet, posting.CanonicalPacket);
        Assert.Equal(facet, posting.Navigation.Snapshot.LensOutcome.EffectiveLens?.Facet);
        Assert.Equal(kind, posting.Navigation.Snapshot.ActiveSubject.Kind);
        Assert.Contains(kind == StructuralSubjectKind.Type ? "JsonSerializer" : "Serialize",
            posting.Navigation.Snapshot.ActiveSubject.Label, StringComparison.Ordinal);
        Assert.Equal("10.0.0", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.Coordinate.Version);
        Assert.Equal("net10.0", Assert.Single(operation.Scope.Packages)
            .Occurrence.Package.Coordinate.Framework);
    }
}
