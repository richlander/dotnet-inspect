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
    // PR-fast: managed fixture images and deterministic gated HTTP responses.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewDemand_SharesAcquisitionAndPreservesWaiterCancellation(bool cancelApi)
    {
        string id = $"library.overview.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(PackageSource).Assembly.Location);
        var inner = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"ref/net11.0/{id}.dll", assembly),
            ("ref/net11.0/Sibling.dll", assembly),
            ("content/padding.bin", new byte[2 * MiB]),
            ($"lib/net11.0/{id}.dll", assembly)));
        var handler = new LibraryDemandGateHandler(inner);
        using IPackageSourceClient source = Gallery(handler);
        var selector = new PackageLibrarySelector($"compile:ref/net11.0/{id}.dll", PackageLibrarySelectionKind.AssetId);
        using var apiCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<BrowserScopeLease<BrowserInspectionScope>> api = BrowserPackageWorkspace.OpenLibraryScopeAsync(
            id, "1.0.0", "net11.0", PackageLibraryInspectionDemandPlanner.Plan(selector,
                PackageLibraryInspectionRequirement.PublicApi, PackageLibraryInspectionRequirement.ImplementationFacts),
            source, TimeSpan.FromSeconds(10), apiCancellation.Token);
        await handler.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<BrowserScopeLease<BrowserInspectionScope>> document = BrowserPackageWorkspace.OpenLibraryScopeAsync(
            id, "1.0.0", "net11.0", PackageLibraryInspectionDemandPlanner.Plan(selector,
                PackageLibraryInspectionRequirement.ImplementationFacts),
            source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (cancelApi)
        {
            apiCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api);
        }
        handler.Release.SetResult();
        await using var documentLease = await document;
        if (!cancelApi)
        {
            await using var apiLease = await api;
            Assert.Same(documentLease.Scope, apiLease.Scope);
        }
        Assert.Equal(1, inner.OrdinaryPackageResponses);
        Assert.Single(documentLease.Scope.SurfaceParticipants);
        Assert.Single(documentLease.Scope.ImplementationParticipants);
        RangedPackageContent content = Assert.IsType<RangedPackageContent>(documentLease.Scope.Coordinates[0].Package.Content);
        Assert.False(content.IsMaterialized("ref/net11.0/Sibling.dll"));
    }

    sealed class LibraryDemandGateHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        readonly HttpMessageInvoker _inner = new(inner);
        int _started;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Range is not null && Interlocked.Exchange(ref _started, 1) == 0)
            {
                Started.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await _inner.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // PR-fast: the selected implementation transfer fails; surface acquisition remains valid.
    [Fact]
    public async Task OverviewDemand_ImplementationFailurePreservesIndependentApi()
    {
        string id = $"library.failure.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(PackageSource).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ($"ref/net11.0/{id}.dll", assembly),
            ("content/before.bin", new byte[2 * MiB]),
            ($"lib/net11.0/{id}.dll", assembly),
            ("content/after.bin", new byte[2 * MiB]));
        var handler = new LibraryImplementationFailureHandler(new GalleryPackageHandler(id, "1.0.0", archive),
            archive.Length, assembly.LongLength + MiB);
        using IPackageSourceClient source = Gallery(handler);
        var selector = new PackageLibrarySelector($"compile:ref/net11.0/{id}.dll", PackageLibrarySelectionKind.AssetId);
        await using var api = await BrowserPackageWorkspace.OpenLibraryScopeAsync(id, "1.0.0", "net11.0",
            PackageLibraryInspectionDemandPlanner.Plan(selector, PackageLibraryInspectionRequirement.PublicApi,
                PackageLibraryInspectionRequirement.ImplementationFacts),
            source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(handler.FailedRanges > 0);
        Assert.Single(api.Scope.SurfaceParticipants);
        Assert.Empty(api.Scope.ImplementationParticipants);
        ExactLibraryApiInspectionExecution result = api.Scope.UsePackageAssemblyRoles(api.Scope.Coordinates[0],
            (root, realization) => ExactLibraryApiInspectionOperation.Execute(root, realization,
                new(id, "1.0.0", "net11.0", selector.Value, ExactLibraryApiSelectionKind.AssetId),
                ExactLibraryApiInspectionOperation.DefaultLimits));
        Assert.True(result.Inspection.Content.IsAvailable);
        Assert.True(result.Inspection.Content.IsComplete);
        await Assert.ThrowsAsync<BrowserPackageLibraryAcquisitionException>(() =>
            BrowserPackageWorkspace.OpenLibraryScopeAsync(id, "1.0.0", "net11.0",
                PackageLibraryInspectionDemandPlanner.Plan(selector, PackageLibraryInspectionRequirement.ImplementationFacts),
                source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    sealed class LibraryImplementationFailureHandler(HttpMessageHandler inner, long archiveLength, long failureStart) : HttpMessageHandler
    {
        readonly HttpMessageInvoker _inner = new(inner);
        public int FailedRanges { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await _inner.SendAsync(request, cancellationToken);
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range?.From > failureStart && range.To != archiveLength - 1)
            {
                // The malformed implementation response leaves surface ranges usable.
                FailedRanges++;
                var contentRange = response.Content.Headers.ContentRange;
                response.Content.Dispose();
                response.Content = new ByteArrayContent([0]);
                response.Content.Headers.ContentRange = contentRange;
            }
            else if (range is null && request.RequestUri!.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                response.Content.Dispose();
                response.Content = new StreamContent(new LibraryFullPayloadFailureStream());
                response.Content.Headers.ContentLength = archiveLength;
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class LibraryFullPayloadFailureStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Complete transfer failed.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Complete transfer failed."));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // PR-fast: real managed fixture images, no broad analysis.
    [Theory]
    [InlineData("net11.0", PackageAssetDemand.Surface)]
    [InlineData("", PackageAssetDemand.Surface)]
    [InlineData("net99.0", PackageAssetDemand.SurfaceAndImplementation)]
    public async Task ExactLibraryDemand_NarrowsTransferAndWorkspace(string target, PackageAssetDemand demand)
    {
        string id = $"library.exact.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] other = File.ReadAllBytes(typeof(PackageExports).Assembly.Location);
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ("ref/net11.0/AAA.dll", other),
            ($"ref/net11.0/{id}.dll", assembly),
            ("content/padding.bin", new byte[2 * 1024 * 1024]),
            ($"lib/net11.0/{id}.dll", assembly),
            ("lib/net11.0/AAA.dll", other)));
        using IPackageSourceClient source = Gallery(handler);
        await BrowserPackageWorkspace.InventoryWithSettlementAsync(id, "1.0.0", target,
            source, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await using var lease = await BrowserPackageWorkspace.OpenLibraryScopeAsync(id, "1.0.0", target,
            PackageLibraryInspectionDemandPlanner.Plan(
                new($"compile:ref/net11.0/{id}.dll", PackageLibrarySelectionKind.AssetId),
                demand == PackageAssetDemand.Surface
                    ? PackageLibraryInspectionRequirement.PublicApi
                    : PackageLibraryInspectionRequirement.ImplementationFacts), source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = lease.Scope;
        Assert.Single(scope.SurfaceParticipants);
        Assert.Equal($"ref/net11.0/{id}.dll", scope.SurfaceParticipants[0].Asset.Path);
        RangedPackageContent content = Assert.IsType<RangedPackageContent>(scope.Coordinates[0].Package.Content);
        Assert.False(content.IsMaterialized("ref/net11.0/AAA.dll"));
        Assert.Equal(2, scope.Coordinates[0].Selection.Assets.Count);
        if (demand == PackageAssetDemand.Surface)
        {
            Assert.Empty(scope.ImplementationParticipants);
            Assert.False(content.IsMaterialized($"lib/net11.0/{id}.dll"));
        }
        else
        {
            Assert.Single(scope.ImplementationParticipants);
            Assert.Equal($"lib/net11.0/{id}.dll", scope.ImplementationParticipants[0].Asset.Path);
        }
        int requests = handler.Requested.Count;
        await using var repeated = await BrowserPackageWorkspace.OpenLibraryScopeAsync(id, "1.0.0", target,
            $"compile:ref/net11.0/{id}.dll", demand, source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(requests, handler.Requested.Count);
        Assert.Same(scope, repeated.Scope);
    }

    // PR-fast: no binary decoding; exercises empty and no-match settlements.
    [Theory]
    [InlineData("content/empty.txt", "net11.0", PackageInfoMeasurementStatus.NoCompileSlices)]
    [InlineData("ref/net11.0/_._", "net11.0", PackageInfoMeasurementStatus.SelectedEmpty)]
    [InlineData("lib/net11.0/Library.dll", "net8.0", PackageInfoMeasurementStatus.NoApplicableSlice)]
    public async Task PackageInventory_RangePreservesEmptyAndNoMatch(string path, string target,
        PackageInfoMeasurementStatus status)
    {
        string id = $"summary.empty.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(id, "1.0.0", PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            (path, new byte[4]),
            ("content/padding.bin", new byte[2 * 1024 * 1024]),
            path.EndsWith("/_._", StringComparison.Ordinal)
                ? ("lib/net8.0/Other.dll", new byte[4])
                : ("content/other.txt", new byte[4])));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealization inventory = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.InventoryWithSettlementAsync(id, "1.0.0", target, source,
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Realization;
        Assert.Equal(status, inventory.PackageInfo.Content.Status);
        Assert.Null(inventory.Coordinate.DefaultAsset);
        Assert.True(handler.PackageBytesServed < 256 * 1024);
    }

    // PR-fast: directory/transfer assertions; no whole-assembly analysis.
    [Theory]
    [InlineData("net11.0")]
    [InlineData("")]
    [InlineData("net99.0")]
    public async Task PackageInventory_RangePreservesSelectionWithoutLibraryBodies(string target)
    {
        string id = $"summary.inventory.{Guid.NewGuid():N}";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] other = File.ReadAllBytes(typeof(PackageExports).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(Nuspec(id, "1.0.0"))),
            ("ref/net11.0/AAA.dll", other),
            ($"ref/net11.0/{id}.dll", assembly),
            ("lib/net11.0/AAA.dll", other),
            ($"lib/net11.0/{id}.dll", assembly),
            ("lib/net11.0/AAA.xml", Encoding.UTF8.GetBytes("<doc/>")),
            ("README.md", Encoding.UTF8.GetBytes("# Document")),
            ("content/padding.bin", new byte[2 * 1024 * 1024]));
        var handler = new GalleryPackageHandler(id, "1.0.0", archive);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageRealization inventory = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.InventoryWithSettlementAsync(id, "1.0.0", target, source,
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Realization;
        RangedPackageContent content = Assert.IsType<RangedPackageContent>(inventory.Coordinate.Package.Content);
        Assert.False(content.IsMaterialized("ref/net11.0/AAA.dll"));
        Assert.False(content.IsMaterialized($"lib/net11.0/{id}.dll"));
        Assert.False(content.IsMaterialized("lib/net11.0/AAA.xml"));
        Assert.False(content.IsMaterialized("README.md"));
        Assert.True(content.IsMaterialized($"{id}.nuspec"));
        Assert.Equal(8, content.EnumerateEntries().Count());
        Assert.Equal(2, inventory.Coordinate.Package.Documents().Count);
        Assert.Contains(inventory.Coordinate.Package.Documents(), document => document.Kind == "metadata");
        Assert.Equal(PackageInfoMeasurementStatus.Measured, inventory.PackageInfo.Content.Status);
        Assert.Equal(2, inventory.PackageInfo.Content.SelectedLibraryCount);
        Assert.Equal((long)assembly.Length + other.Length, inventory.PackageInfo.Content.SelectedLibraryPayloadBytes);
        Assert.Equal($"compile:ref/net11.0/{id}.dll", inventory.Coordinate.DefaultAsset!.Id);
        Assert.True(handler.RangedPackageResponses > 0);
        Assert.True(handler.PackageBytesServed < 256 * 1024);
        int requests = handler.Requested.Count;
        BrowserPackageRealization repeated = Assert.IsType<BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.InventoryWithSettlementAsync(id, "1.0.0", target, source,
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Realization;
        Assert.Equal(requests, handler.Requested.Count);
        Assert.Same(content.GenerationIdentity, repeated.Coordinate.Package.Content.GenerationIdentity);

        // Later inspection must request content; directory-only authority cannot
        // replace a Library realization, even when the coordinate matches.
        await using var selected = await BrowserPackageWorkspace.OpenMetadataScopeAsync(
            id, "1.0.0", target, inventory.Coordinate.DefaultAsset.Id, source,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        RangedPackageContent selectedContent = Assert.IsType<RangedPackageContent>(
            selected.Scope.Coordinates[0].Package.Content);
        Assert.True(selectedContent.IsMaterialized($"lib/net11.0/{id}.dll"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageQuerySkill_InventoryDemandDoesNotMaterializeBodies(bool hasSkill)
    {
        string id = $"query.inventory.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        byte[] manifestBytes = Encoding.UTF8.GetBytes(Nuspec(id, version));
        byte[] package = PackageEntries(
            ($"{id}.nuspec", manifestBytes),
            (hasSkill ? "skills/SKILL.md" : "docs/README.md", new byte[2 * 1024 * 1024]));
        var handler = new GalleryPackageHandler(id, version, package);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest = Assert.IsType<PackageManifestFactsResult.Available>(
            PackageManifestFactsQuery.Execute(manifestBytes,
                PackageSourceCoordinate.Create(id, version))).Value;
        var candidate = new PackageQueryPackage(id, version, [], 0, false, source.Source, manifest);
        using var deadline = new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        PackageQueryContentInventory inventory =
            Assert.IsType<PackageQueryContentResult.InventoryAvailable>(
                await BrowserPackageWorkspace
                    .AcquirePackageQueryInventoryAsync(
                        candidate,
                        source,
                        deadline))
            .Inventory;

        Assert.Equal(hasSkill, inventory.Entries.Any(entry => entry.Path == "skills/SKILL.md"));
        Assert.Equal(2, inventory.Entries.Count);
        Assert.True(handler.RangedPackageResponses > 0);
        Assert.True(handler.PackageBytesServed < 256 * 1024,
            $"served {handler.PackageBytesServed} of {package.Length} bytes");
    }

    [Fact]
    public async Task PackageQueryEntryFiles_RangeTransfersOnlySelectedBodies()
    {
        string id = $"query.files.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string settingsPath =
            "tools/net11.0/any/DotnetToolSettings.xml";
        const string libraryPath = "lib/net11.0/Selected.dll";
        const string unrelatedPath = "content/unrelated.bin";
        byte[] manifestBytes = Encoding.UTF8.GetBytes(Nuspec(id, version));
        byte[] settingsBytes = Encoding.UTF8.GetBytes(
            """<DotNetCliTool Version="1" />""");
        byte[] libraryBytes = new byte[16 * 1024];
        byte[] package = PackageEntries(
            ($"{id}.nuspec", manifestBytes),
            (settingsPath, settingsBytes),
            (libraryPath, libraryBytes),
            (unrelatedPath, new byte[2 * 1024 * 1024]));
        var handler = new GalleryPackageHandler(id, version, package);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest =
            Assert.IsType<PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    PackageSourceCoordinate.Create(id, version)))
            .Value;
        var candidate = new PackageQueryPackage(
            id,
            version,
            [],
            0,
            false,
            source.Source,
            manifest);
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        PackageQueryContentInventory inventory =
            Assert.IsType<PackageQueryContentResult.InventoryAvailable>(
                await BrowserPackageWorkspace
                    .AcquirePackageQueryInventoryAsync(
                        candidate,
                        source,
                        deadline))
            .Inventory;
        PackageContentEntry settings = Assert.Single(
            inventory.Entries,
            entry => entry.Path == settingsPath);
        PackageContentEntry library = Assert.Single(
            inventory.Entries,
            entry => entry.Path == libraryPath);

        var available =
            Assert.IsType<PackageQueryContentResult.Available>(
                await BrowserPackageWorkspace
                    .AcquirePackageQueryFilesAsync(
                        candidate,
                        inventory,
                        [settings, library],
                        source,
                        deadline));

        Assert.Equal(
            [libraryPath, settingsPath],
            available.Content.EnumerateEntries()
                .Order(StringComparer.Ordinal));
        Assert.NotNull(available.Evidence);
        Assert.True(handler.RangedPackageResponses > 0);
        Assert.True(
            handler.PackageBytesServed < 512 * 1024,
            $"served {handler.PackageBytesServed} of "
            + $"{package.Length} package bytes");
        Assert.DoesNotContain(
            unrelatedPath,
            available.Content.EnumerateEntries(),
            StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MetadataOverview_DirectAbsentLibraryPreservesNoCompileAssets(string selector)
    {
        string id = $"metadata.empty.{Guid.NewGuid():N}";
        byte[] package = PackageEntries(
            ($"{id}.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>")),
            ("content/README.md", Encoding.UTF8.GetBytes("No managed Libraries.")));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(id, "1.0.0", package, fromCache: false));
        using JsonDocument metadata = JsonDocument.Parse(
            await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata(
                id, "1.0.0", "net11.0", selector));
        Assert.Empty(metadata.RootElement.GetProperty("assemblies").EnumerateArray());
        Assert.Equal("NoCompileAssets", metadata.RootElement.GetProperty("compileLibrary")
            .GetProperty("status").GetString());
        InvalidOperationException tableFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadataTable(
                id, "1.0.0", "net11.0", selector, "cli", 2, 1, 10));
        Assert.Contains("NoCompileAssets", tableFailure.Message);
    }

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
