using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task MetadataScope_ReferenceOnlyLibraryRetainsItsSurfaceFallback()
    {
        string id = $"metadata.reference.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>")),
            ("ref/net11.0/Reference.dll", image),
            ("content/padding.bin", new byte[2 * 1024 * 1024])));
        using IPackageSourceClient source = Gallery(handler);
        await using var selected = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", "net11.0", "compile:ref/net11.0/Reference.dll", source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(selected.Scope.ImplementationParticipants);
        BrowserWorkspaceParticipant participant = selected.Scope.LibraryParticipant(
            selected.Scope.Coordinates[0], "compile:ref/net11.0/Reference.dll");
        Assert.Equal("ref/net11.0/Reference.dll", participant.Asset.Path);
        Assert.False(Assert.IsType<RangedPackageContent>(selected.Scope.Coordinates[0].Package.Content)
            .IsMaterialized("content/padding.bin"));
    }

    [Fact]
    public async Task MetadataScope_NamesOneImplementationAndDoesNotReplaceTheFullScope()
    {
        string id = $"metadata.selected.{Guid.NewGuid():N}";
        byte[] chosen = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] otherImage = File.ReadAllBytes(typeof(DotnetInspect.Web.Interop.Metadata.MetadataExports).Assembly.Location);
        byte[] other = new byte[2 * 1024 * 1024];
        otherImage.CopyTo(other, 0);
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version><icon>images/icon.png</icon></metadata></package>")),
            ("ref/net11.0/Chosen.dll", chosen),
            ("ref/net11.0/Other.dll", otherImage),
            ("lib/net11.0/Chosen.dll", chosen),
            ("lib/net11.0/Other.dll", other),
            ("images/icon.png", PackageIconPng)));
        using IPackageSourceClient source = Gallery(handler);
        await using var selected = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", "net11.0", "compile:ref/net11.0/Chosen.dll", source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = selected.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        RangedPackageContent content = Assert.IsType<RangedPackageContent>(coordinate.Package.Content);
        Assert.Single(scope.ImplementationParticipants);
        Assert.True(content.IsMaterialized("lib/net11.0/Chosen.dll"));
        Assert.False(content.IsMaterialized("lib/net11.0/Other.dll"));
        Assert.False(content.IsMaterialized("images/icon.png"));
        BrowserWorkspaceParticipant participant = scope.LibraryParticipant(coordinate, "compile:ref/net11.0/Chosen.dll");
        Assert.Equal("lib/net11.0/Chosen.dll", participant.Asset.Path);
        int requests = handler.Requested.Count;
        await using var repeated = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", "net11.0", "compile:ref/net11.0/Chosen.dll", source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(scope, repeated.Scope);
        Assert.Equal(requests, handler.Requested.Count);
        await using var neighbor = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", "net11.0", "compile:ref/net11.0/Other.dll", source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotSame(scope, neighbor.Scope);
        Assert.Single(neighbor.Scope.ImplementationParticipants);
        Assert.Equal("lib/net11.0/Other.dll", neighbor.Scope.LibraryParticipant(
            neighbor.Scope.Coordinates[0], "compile:ref/net11.0/Other.dll").Asset.Path);
        BrowserPackageRealization full = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(id, "1.0.0", "net11.0", source,
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Realization;
        Assert.Null(full.Coordinate.Package.Icon);
        PackageIconRangeResult icon = await BrowserPackageWorkspace.ReadPackageIconAsync(
            id, "1.0.0", source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.IsType<PackageIconResult.Available>(
            Assert.IsType<PackageIconRangeResult.Completed>(icon).Icon);
        await using var broad = await BrowserPackageWorkspace.OpenScopeAsync(full,
            TestContext.Current.CancellationToken);
        Assert.NotSame(scope, broad.Scope);
        Assert.Equal(2, broad.Scope.ImplementationParticipants.Length);
        Assert.True(Assert.IsType<RangedPackageContent>(full.Coordinate.Package.Content)
            .IsMaterialized("lib/net11.0/Other.dll"));
        await using var afterSummary = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", "net11.0", "compile:ref/net11.0/Chosen.dll", source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(broad.Scope, afterSummary.Scope);
    }

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
