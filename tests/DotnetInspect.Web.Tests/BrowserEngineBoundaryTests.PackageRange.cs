using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task PackageSummary_PreservesProductDefaultAlongsideCompleteLibraryInventory()
    {
        string id = $"Summary.Default.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>")),
            ("lib/net11.0/AAA.dll", assembly),
            ($"lib/net11.0/{id}.dll", assembly));
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(id, "1.0.0", archive, fromCache: false,
                producerKey: BrowserPackageWorkspace.Gallery.Source.Producer.Key));
        BrowserPackageLoadResult summary = Assert.IsType<BrowserPackageLoadResult>(
            JsonSerializer.Deserialize(await PackageExports.QueryPackageSummary(id, "1.0.0", "net11.0"),
                BrowserPackageJsonContext.Default.BrowserPackageLoadResult));
        Assert.Null(summary.Surface);
        Assert.Equal($"compile:lib/net11.0/{id}.dll", summary.DefaultLibraryId);
        Assert.Equal(2, summary.PackageChildren!.Content.Libraries.Length);
        Assert.Equal("AAA.dll", summary.PackageChildren.Content.Libraries[0].AssemblyName);
        Assert.Contains(summary.PackageChildren.Content.Libraries, library => library.AssetId == summary.DefaultLibraryId);
        BrowserPackageRealization realization = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(id, "1.0.0", "net11.0",
                cancellationToken: TestContext.Current.CancellationToken)).Realization;
        string rootRequest = realization.Coordinate.Binding!.CreateReacquisitionRequest().Encode();
        BrowserPackageRootLoadResult root = Assert.IsType<BrowserPackageRootLoadResult>(
            JsonSerializer.Deserialize(await PackageExports.QueryPackageRoot(rootRequest),
                BrowserPackageJsonContext.Default.BrowserPackageRootLoadResult));
        Assert.Equal(summary.DefaultLibraryId, root.DefaultLibraryId);
        Assert.Equal("AAA.dll", root.PackageChildren.Content.Libraries[0].AssemblyName);
    }

    [Fact]
    public async Task PackageRealization_RangeRetainsSelectedFoldersAndReusesGeneration()
    {
        string id = $"browser.range.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>")),
            ("lib/net11.0/Library.dll", assembly),
            ("lib/net11.0/Library.xml", Encoding.UTF8.GetBytes("<doc/>")),
            ("lib/net8.0/Library.dll", assembly),
            ("content/padding.bin", new byte[2_000_000]));
        var handler = new GalleryPackageHandler(id, "1.0.0", archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageRealization first = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken)).Realization;
        RangedPackageContent content = Assert.IsType<RangedPackageContent>(first.Coordinate.Package.Content);
        Assert.True(content.IsMaterialized("lib/net11.0/Library.dll"));
        Assert.True(content.IsMaterialized("lib/net11.0/Library.xml"));
        Assert.False(content.IsMaterialized("lib/net8.0/Library.dll"));
        Assert.False(content.IsMaterialized("content/padding.bin"));
        Assert.Equal(content.MaterializedBytes, first.Coordinate.Package.RetainedSize);
        Assert.Equal(PackageInfoMeasurementStatus.Measured, first.PackageInfo.Content.Status);
        Assert.Equal(archive.LongLength, first.PackageInfo.Content.CompressedPackageBytes);
        Assert.True(handler.RangedPackageResponses > 0);
        Assert.True(handler.PackageBytesServed < archive.LongLength);
        int requests = handler.Requested.Count;

        BrowserPackageRealization second = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                id, "1.0.0", "net11.0", source, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken)).Realization;
        Assert.Equal(requests, handler.Requested.Count);
        Assert.Same(content.GenerationIdentity, second.Coordinate.Package.Content.GenerationIdentity);
        await using var lease = await BrowserPackageWorkspace.OpenScopeAsync(
            second, TestContext.Current.CancellationToken);
        Assert.Single(lease.Scope.SurfaceParticipants);

        BrowserPackageRealization otherTarget = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                id, "1.0.0", "net8.0", source, TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken)).Realization;
        Assert.NotEqual(first.Coordinate.Package.CacheKey, otherTarget.Coordinate.Package.CacheKey);
        Assert.Same(content.GenerationIdentity,
            BrowserPackageWorkspace.CoordinateFor(second.Coordinate.Binding!).Package.Content.GenerationIdentity);
        Assert.True(Assert.IsType<RangedPackageContent>(otherTarget.Coordinate.Package.Content)
            .IsMaterialized("lib/net8.0/Library.dll"));

        var store = new BrowserPackageWorkspace.BrowserSessionPackageStore(source);
        Assert.Null(store.TryGetCached(id, "1.0.0", [source.Source.Producer.Key]));
    }

    [Fact]
    public async Task PackageRealization_RangeCapacityRejectsBeforeEntryBodies()
    {
        string id = $"browser.range.capacity.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>")),
            ("lib/net11.0/Library.dll", new byte[1024 * 1024])));
        using IPackageSourceClient source = Gallery(handler);
        // Exercise the host boundary directly: an oversized selected demand
        // must fail without issuing any payload-body request or keeping a charge.
        using var deadline = new BrowserPackageWorkspace.BrowserPackageOperationDeadline(TimeSpan.FromSeconds(5), default);
        var policy = new BrowserPackageWorkspace.BrowserPackageRealizationTransferPolicy(
            new BrowserPackageWorkspace.BrowserSessionPackageStore(source), deadline);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await policy.ReserveRangedAsync(PackageSourceCoordinate.Create(id, "1.0.0"),
                129L * 1024 * 1024, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requested);
    }
}
