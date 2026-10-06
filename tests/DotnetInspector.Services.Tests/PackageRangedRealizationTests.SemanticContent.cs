using System.IO.Compression;
using System.Net;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using NuGetFetch;
using ZipFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Semantic exact-entry acquisition gates for
/// <c>docs/design/package-house-semantic-content-demand.md#content-query</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task SemanticFiles_RangedReadPublishesOnlyExactEntries()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        string[] entries =
        [
            "lib/net45/PCLStorage.xml",
            "lib/sl5/PCLStorage.dll",
        ];
        PackageHouseContentQuery query =
            PackageHouseContentQuery.PackageFiles(entries);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Same(query, acquired.Result.Request.ContentQuery);
        Assert.Equal(
            entries.Order(StringComparer.Ordinal),
            acquired.Payload.Content
                .EnumerateEntries()
                .Order(StringComparer.Ordinal));
        Assert.Null(acquired.Payload.Content.RootPath);
        Assert.Null(acquired.Payload.Content.NupkgPath);
        Assert.False(acquired.Payload.Content.TryOpenArchive(out _));
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "lib/net45/PCLStorage.dll",
            out _));

        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Ranged);
        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.True(receipt.BytesReceived < archive.Length);
    }

    [Fact]
    public async Task SemanticFiles_CompleteFallbackRetainsArchiveButPublishesOnlyExactEntries()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                PackageHouseContentQuery.PackageFilesWithFileList([Path]),
                sizeCut: archive.Length));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal([Path], acquired.Payload.Content.EnumerateEntries());
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "PCLStorage.nuspec",
            out _));
        IPackageContent? cached = store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]);
        Assert.NotNull(cached);
        Assert.Contains("PCLStorage.nuspec", cached.EnumerateEntries());
        Assert.Contains(
            acquired.Result.Evidence.FileList!.Entries,
            entry => entry.Path == "PCLStorage.nuspec");
        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Download);
        Assert.Equal(PackageTransferPath.Download, receipt.Path);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(0, server.RangedRequests);
    }

    [Fact]
    public async Task SemanticFileList_CompleteAndCacheExcludeStoreSidecars()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        using var store = new TemporaryFileSystemPackageStore();
        PackageHouseContentQuery query =
            PackageHouseContentQuery.PackageFilesWithFileList([Path]);

        var first = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length));
        var second = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length));

        using var oracle = new ZipArchive(new MemoryStream(archive));
        string[] expected =
        [
            .. oracle.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => entry.FullName),
        ];
        AssertFileList(first);
        AssertFileList(second);
        Assert.Equal(1, server.FullRequests);

        void AssertFileList(PackageHouseSettlement.Acquired acquired)
        {
            Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
            Assert.Equal([Path], acquired.Payload.Content.EnumerateEntries());
            PackageHouseFileList fileList =
                Assert.IsType<PackageHouseFileList>(
                    acquired.Result.Evidence.FileList);
            Assert.Equal(
                expected,
                fileList.Entries.Select(static entry => entry.Path));
            Assert.DoesNotContain(
                fileList.Entries,
                static entry => entry.Path.EndsWith(
                    ".nupkg",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                fileList.Entries,
                static entry => entry.Path.Equals(
                    NuGetCache.CommitMarkerFileName,
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SemanticFileList_RangedReadPublishesOnlyDirectoryEvidence()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var query = new PackageHouseContentQuery(
            new PackageHouseContentNarrowing.PackageWide(),
            [new PackageHouseContentTerminal.FileList()]);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFileList fileList =
            Assert.IsType<PackageHouseFileList>(
                acquired.Result.Evidence.FileList);
        using var oracle = new ZipArchive(new MemoryStream(archive));
        Assert.Equal(
            oracle.Entries.Select(static entry => entry.FullName),
            fileList.Entries.Select(static entry => entry.Path));
        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Ranged);
        Assert.Equal(
            [
                PackageTransferRequestPurpose.SizeProbe,
                PackageTransferRequestPurpose.DirectoryTail,
            ],
            receipt.Requests.Select(static request => request.Purpose));
    }

    [Fact]
    public async Task TfmFileList_RidTargetPublishesOnlySelectedTargetFolders()
    {
        const string RuntimePath =
            "runtimes/linux-x64/lib/net10.0/System.Text.Json.dll";
        const string RuntimePdbPath =
            "runtimes/linux-x64/lib/net10.0/System.Text.Json.pdb";
        byte[] archive = CreateTfmSemanticArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0", "linux-x64");
        PackageHouseContentQuery query =
            PackageHouseContentQuery.TfmFileList(target);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFileList fileList =
            Assert.IsType<PackageHouseFileList>(
                acquired.Result.Evidence.FileList);
        Assert.Equal(
            [
                AddressApiPath,
                "ref/net10.0/System.Text.Json.xml",
                RuntimePath,
                RuntimePdbPath,
            ],
            fileList.Entries.Select(static entry => entry.Path));
        PackageHouseContentNarrowingReceipt narrowing =
            Assert.IsType<PackageHouseContentNarrowingReceipt>(
                acquired.Result.Evidence.ContentNarrowing);
        Assert.Same(narrowing, fileList.Narrowing);
        Assert.Same(
            acquired.Result.Evidence.Acquisition,
            narrowing.Acquisition);
        PackageCompileAssetSelectionReceipt selection =
            Assert.IsType<PackageCompileAssetSelectionReceipt>(
                narrowing.TargetSelection);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.Selected,
            selection.Selection.Status);
        Assert.Equal("net10.0", selection.RequestedTargetFramework);
        Assert.Equal("linux-x64", selection.RequestedRuntimeIdentifier);
        PackageContentEntry apiEntry = Assert.Single(
            fileList.Entries,
            static entry => entry.Path == AddressApiPath);
        PackageHouseContentQuery exactFiles =
            fileList.CreateFilesQuery([apiEntry]);
        Assert.Same(
            narrowing.Narrowing,
            exactFiles.Narrowing);
        Assert.Same(
            fileList,
            exactFiles.RetainedFileList);
        var exactAcquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await environment.AcquireContentAsync(
                    store,
                    exactFiles,
                    packageId: AddressPackageId,
                    version: AddressPackageVersion,
                    targetContext: target));
        Assert.IsType<PackageHouseResult.Settled>(
            exactAcquired.Result);
        Assert.Equal(
            [AddressApiPath],
            exactAcquired.Payload.Content.EnumerateEntries());
        Assert.Throws<ArgumentException>(
            () => new PackageHouseRequest(
                new PackageHouseDemand.Exact(
                    PackageSourceCoordinate.Create(
                        "Other.Package",
                        AddressPackageVersion)),
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Acquire),
                targetContext: target,
                contentQuery: exactFiles));
        Assert.Throws<ArgumentException>(
            () => fileList.CreateFilesQuery(
                [new PackageContentEntry("README.md", 1)]));
        Assert.Equal(
            [
                PackageTransferRequestPurpose.SizeProbe,
                PackageTransferRequestPurpose.DirectoryTail,
            ],
            Transfer(acquired, PackagePayloadOrigin.Ranged)
                .Requests
                .Select(static request => request.Purpose));
    }

    [Fact]
    public async Task
        LibraryInventory_RangedSelectsAlphabeticalLibraryAndIssuesExactReferences()
    {
        byte[] archive = CreateLibraryInventoryArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");
        PackageHouseContentQuery query =
            PackageHouseContentQuery
                .GetLibraryAndInventoryForTarget(target);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Equal(
            ["ref/net10.0/Alpha.dll"],
            acquired.Payload.Content.EnumerateEntries());
        PackageHouseLibraryAndInventory result =
            Assert.IsType<PackageHouseLibraryAndInventory>(
                acquired.Result.Evidence.LibraryAndInventory);
        Assert.Equal(3, result.Inventory.Rows.Length);
        Assert.Equal(
            [
                "ref/net10.0/Alpha.dll",
                "ref/net10.0/Beta.dll",
                "ref/net10.0/Gamma.dll",
            ],
            result.Inventory.Rows.Select(
                static row => row.CompileEntry.Path));
        Assert.Same(
            result.Inventory.Rows[0],
            result.Selection.SelectedRow);
        Assert.False(result.Selection.IsPackageNamesake);
        PackageHouseLibraryMaterializationOutcome.Completed materialized =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer
                    .MaterializeSelectionAsync(
                        acquired,
                        result.SelectedLibrary,
                        cancellationToken:
                            TestContext.Current.CancellationToken));
        Assert.Null(
            materialized.Receipt.Library.ImplementationAssembly);
        Assert.Single(materialized.Receipt.Library.Contents);
        await materialized.Owner.DisposeAsync();
        await materialized.Artifacts.DisposeAsync();

        PackageHouseLibraryInventoryRow alpha =
            result.Inventory.Rows[0];
        Assert.Equal(
            "lib/net10.0/Alpha.dll",
            alpha.ImplementationEntry?.Path);
        Assert.Equal(
            PackageHousePortablePdbEvidenceKind.Listed,
            alpha.PortablePdbEvidence);
        Assert.Equal(
            "lib/net10.0/Alpha.pdb",
            alpha.PortablePdbEntry?.Path);
        PackageHouseLibraryInventoryRow beta =
            result.Inventory.Rows[1];
        Assert.Equal(
            PackageHousePortablePdbEvidenceKind.Absent,
            beta.PortablePdbEvidence);
        Assert.Null(beta.PortablePdbEntry);
        PackageHouseLibraryInventoryRow gamma =
            result.Inventory.Rows[2];
        Assert.Null(gamma.ImplementationEntry);
        Assert.Equal(
            PackageHousePortablePdbEvidenceKind.NotApplicable,
            gamma.PortablePdbEvidence);

        PackageHouseContentQuery files =
            result.Inventory.CreateFilesQuery(
                [
                    alpha.ImplementationEntry!.Value,
                    alpha.PortablePdbEntry!.Value,
                ]);
        var exact = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                files,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));
        Assert.IsType<PackageHouseResult.Settled>(exact.Result);
        Assert.Equal(
            [
                "lib/net10.0/Alpha.dll",
                "lib/net10.0/Alpha.pdb",
            ],
            exact.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task
        LibraryInventoryOnly_RangedMaterializesNoLibraryAndIssuesExactPdbReference()
    {
        byte[] archive = CreateLibraryInventoryArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                PackageHouseContentQuery
                    .GetLibraryInventoryForTarget(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        Assert.Null(acquired.Result.Evidence.LibraryAndInventory);
        PackageHouseLibraryInventory inventory =
            Assert.IsType<PackageHouseLibraryInventory>(
                acquired.Result.Evidence.LibraryInventory);
        PackageHouseLibraryInventoryRow alpha =
            Assert.Single(
                inventory.Rows,
                static row =>
                    row.CompileEntry.Path
                    == "ref/net10.0/Alpha.dll");
        Assert.Equal(
            "lib/net10.0/Alpha.pdb",
            alpha.PortablePdbEntry?.Path);

        PackageHouseContentQuery files =
            inventory.CreateFilesQuery(
                [alpha.PortablePdbEntry!.Value]);
        var exact = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                files,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));
        Assert.IsType<PackageHouseResult.Settled>(exact.Result);
        Assert.Equal(
            ["lib/net10.0/Alpha.pdb"],
            exact.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task
        LibraryInventory_UnmatchedTargetIsTypedNoMatch()
    {
        byte[] archive = CreateLibraryInventoryArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net40");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery
                    .GetLibraryAndInventoryForTarget(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Null(acquired.Result.Evidence.LibraryAndInventory);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task
        LibraryInventory_ExplicitEmptyCompileGroupIsTypedNoMatch()
    {
        byte[] archive = CreateFrameworkArchive(
            ("ref/net10.0/_._", []),
            ("lib/net10.0/Unread.dll", [1, 2, 3]));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery
                    .GetLibraryAndInventoryForTarget(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Null(acquired.Result.Evidence.LibraryAndInventory);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task
        LibraryInventory_LibraryPrimaryUsesSameImplementationEntry()
    {
        byte[] assembly = File.ReadAllBytes(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PackageHouse",
                "System.Text.Json.dll"));
        byte[] archive = CreateFrameworkArchive(
            ("lib/net10.0/Alpha.dll", assembly),
            ("lib/net10.0/Alpha.pdb", "alpha-pdb"u8.ToArray()));
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery
                    .GetLibraryAndInventoryForTarget(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        PackageHouseLibraryInventoryRow row = Assert.Single(
            acquired.Result.Evidence.LibraryAndInventory!
                .Inventory.Rows);
        Assert.Equal(
            PackageCompileAssetKind.Library,
            row.CompileAsset.Kind);
        Assert.Equal(row.CompileEntry, row.ImplementationEntry);
        Assert.Equal(
            PackageHousePortablePdbEvidenceKind.Listed,
            row.PortablePdbEvidence);
        Assert.Equal(
            "lib/net10.0/Alpha.pdb",
            row.PortablePdbEntry?.Path);
        Assert.Equal(
            ["lib/net10.0/Alpha.dll"],
            acquired.Payload.Content.EnumerateEntries());
        PackageHouseLibraryMaterializationOutcome.Completed materialized =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer
                    .MaterializeSelectionAsync(
                        acquired,
                        acquired.Result.Evidence
                            .LibraryAndInventory!
                            .SelectedLibrary,
                        cancellationToken:
                            TestContext.Current.CancellationToken));
        Assert.Same(
            materialized.Receipt.Library.ApiAssembly,
            materialized.Receipt.Library.ImplementationAssembly);
        await materialized.Owner.DisposeAsync();
        await materialized.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_RangeAcquiresOnlyIssuedPdb()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        string testPdb =
            Path.ChangeExtension(testAssembly, ".pdb");
        byte[] archive = CreateFrameworkArchive(
            (DllPath, File.ReadAllBytes(testAssembly)),
            (PdbPath, File.ReadAllBytes(testPdb)));
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                store);

        PortablePdbPackageBindingResult bindingResult =
            await PortablePdbPackageComposition.PrepareAsync(
                PackageSourceCoordinate.Create(
                    AddressPackageId,
                    AddressPackageVersion),
                PackageHouseTargetContext.Exact("net11.0"),
                packageSource,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    bindingResult)
                .Value;
        Assert.Equal(
            DllPath,
            binding.Candidate.Row.CompileEntry.Path);
        Assert.Equal(
            PdbPath,
            binding.Candidate.Row.PortablePdbEntry?.Path);

        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new RejectNetworkHandler());
        var pdbStore = new InMemoryPdbStore();
        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    binding.Assembly,
                    client,
                    pdbStore,
                    new UniformPackageSourceAuthorization(
                        [PackageSource.NuGetOrg]))
                {
                    PackageCandidate = binding.Candidate,
                },
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    result);
        Assert.Equal(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.Same(
            binding.Candidate.Row,
            acquired.PackageLibraryRow);
        Assert.False(source.Context.HasPdb);
        await acquired.LoadIntoAsync(
            source.Context,
            TestContext.Current.CancellationToken);
        Assert.True(source.Context.HasPdb);
        PortablePdbSettlementReceipt packageReceipt =
            Assert.Single(
                acquired.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .PackageLocal);
        Assert.Equal(
            packageSource.Settlements[1]
                .Result.Evidence.Acquisition!
                .Transfer.RequestCount,
            packageReceipt.RequestCount);

        Assert.Collection(
            packageSource.Queries,
            query => Assert.IsType<
                PackageHouseContentTerminal
                    .LibraryAndInventoryForTarget>(
                    Assert.Single(query.Terminals)),
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [PdbPath],
                    files.Entries);
            });

        using SourceLinkService warmSource =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        var warm =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            warmSource.Context,
                            binding.Assembly,
                            client,
                            pdbStore,
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackageCandidate =
                                binding.Candidate,
                        },
                        TestContext.Current
                            .CancellationToken));
        Assert.Equal(
            PortablePdbSettlementSource.PackageLocal,
            warm.Source);
        Assert.Equal(
            PortablePdbPositiveStoreDisposition.Reused,
            warm.PositiveStore);
        Assert.Same(
            binding.Candidate.Row,
            warm.PackageLibraryRow);
        Assert.Equal(2, packageSource.Queries.Count);
    }

    [Fact]
    public async Task
        DeferredPackagePortablePdbSettlement_AccountsForInventoryAndPdbWork()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] assemblyBytes =
            File.ReadAllBytes(testAssembly);
        byte[] pdbBytes =
            File.ReadAllBytes(
                Path.ChangeExtension(
                    testAssembly,
                    ".pdb"));
        byte[] archive = CreateFrameworkArchive(
            (DllPath, assemblyBytes),
            (PdbPath, pdbBytes));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive)
                {
                    Host = "api.nuget.org",
                },
                PackageSource.NuGetOrg);
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        ResolvedAssemblyReference assembly =
            CreatePackageAssembly(
                assemblyBytes,
                DllPath);
        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                assembly);
        using var client =
            new HttpClient(
                new RejectNetworkHandler());

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            source.Context,
                            assembly,
                            client,
                            new InMemoryPdbStore(),
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackagePreparation =
                                PortablePdbPackageComposition
                                    .DeferForAssembly(
                                        assembly,
                                        packageSource,
                                        PackageProducerIdentity
                                            .NuGetOrg),
                        },
                        TestContext.Current
                            .CancellationToken));

        Assert.Equal(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.True(acquired.NetworkOccurred);
        PortablePdbSettlementReceipt packageReceipt =
            Assert.Single(
                acquired.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate.PackageLocal);
        Assert.Equal(
            packageSource.Settlements.Sum(
                static settlement =>
                    settlement.Result.Evidence.Acquisition!
                        .Transfer.RequestCount),
            packageReceipt.RequestCount);
        Assert.Equal(
            packageSource.Settlements[0]
                .Result.Evidence.Acquisition!
                .Transfer.BytesReceived
                + packageSource.Settlements[1]
                    .Result.Evidence.Acquisition!
                    .Transfer.BytesReceived,
            packageReceipt.BodyBytesRead);
        Assert.True(packageReceipt.Elapsed > TimeSpan.Zero);
        Assert.Collection(
            packageSource.Queries,
            query => Assert.IsType<
                PackageHouseContentTerminal
                    .LibraryInventoryForTarget>(
                    Assert.Single(query.Terminals)),
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [PdbPath],
                    files.Entries);
            });
        Assert.Empty(
            Assert.IsType<PackageHouseSettlement.Acquired>(
                    packageSource.Settlements[0])
                .Payload.Content.EnumerateEntries());

        using SourceLinkService cachedSource =
            SourceLinkService.OpenEmbeddedPdbOnly(
                assembly);
        var cached =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            cachedSource.Context,
                            assembly,
                            client,
                            new InMemoryPdbStore(),
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackagePreparation =
                                PortablePdbPackageComposition
                                    .DeferForAssembly(
                                        assembly,
                                        packageSource,
                                        PackageProducerIdentity
                                            .NuGetOrg),
                        },
                        TestContext.Current
                            .CancellationToken));
        PortablePdbSettlementReceipt cachedReceipt =
            Assert.Single(
                cached.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate.PackageLocal);
        Assert.Equal(
            packageSource.Settlements[2]
                .Result.Evidence.Acquisition!
                .Transfer.RequestCount
                + packageSource.Settlements[3]
                    .Result.Evidence.Acquisition!
                    .Transfer.RequestCount,
            cachedReceipt.RequestCount);
        Assert.Equal(
            packageSource.Settlements[2]
                .Result.Evidence.Acquisition!
                .Transfer.BytesReceived
                + packageSource.Settlements[3]
                    .Result.Evidence.Acquisition!
                    .Transfer.BytesReceived,
            cachedReceipt.BodyBytesRead);
        Assert.Equal(0, cachedReceipt.RequestCount);
        Assert.Equal(0, cachedReceipt.BodyBytesRead);
    }

    [Fact]
    public async Task
        DeferredPackageBindingFailure_AccountsForInventoryBeforeProviderFallback()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string MissingPath =
            "lib/net11.0/Missing.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] assemblyBytes =
            File.ReadAllBytes(testAssembly);
        byte[] pdbBytes =
            File.ReadAllBytes(
                Path.ChangeExtension(
                    testAssembly,
                    ".pdb"));
        byte[] archive = CreateFrameworkArchive(
            (DllPath, assemblyBytes),
            (PdbPath, pdbBytes));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive)
                {
                    Host = "api.nuget.org",
                },
                PackageSource.NuGetOrg);
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        ResolvedAssemblyReference assembly =
            CreatePackageAssembly(
                assemblyBytes,
                MissingPath);
        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                assembly);
        using var client =
            new HttpClient(
                new RawPdbNetworkHandler(pdbBytes));

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            source.Context,
                            assembly,
                            client,
                            new InMemoryPdbStore(),
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackagePreparation =
                                PortablePdbPackageComposition
                                    .DeferForAssembly(
                                        assembly,
                                        packageSource,
                                        PackageProducerIdentity
                                            .NuGetOrg),
                        },
                        TestContext.Current
                            .CancellationToken));

        Assert.NotEqual(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.True(acquired.NetworkOccurred);
        PortablePdbSettlementReceipt packageReceipt =
            Assert.Single(
                acquired.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate.PackageLocal);
        Assert.Equal(
            PortablePdbSettlementAttemptOutcome.Failed,
            packageReceipt.Outcome);
        Assert.Equal(
            PortablePdbPackageBindingFailureKind
                .SelectedLibraryUnavailable,
            packageReceipt.PackageBindingFailure);
        PackageTransferReceipt transfer =
            packageSource.Settlements[0]
                .Result.Evidence.Acquisition!
                .Transfer;
        Assert.Equal(
            transfer.RequestCount,
            packageReceipt.RequestCount);
        Assert.Equal(
            transfer.BytesReceived,
            packageReceipt.BodyBytesRead);
        Assert.True(packageReceipt.Elapsed > TimeSpan.Zero);
        Assert.Single(packageSource.Queries);
        Assert.IsType<
            PackageHouseContentTerminal
                .LibraryInventoryForTarget>(
                Assert.Single(
                    packageSource.Queries[0].Terminals));
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_StoreReadBackFailureIsVisible()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] archive = CreateFrameworkArchive(
            (DllPath, File.ReadAllBytes(testAssembly)),
            (
                PdbPath,
                File.ReadAllBytes(
                    Path.ChangeExtension(
                        testAssembly,
                        ".pdb"))));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;
        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new NotFoundNetworkHandler());

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    binding.Assembly,
                    client,
                    new PdbDroppingStore(),
                    new UniformPackageSourceAuthorization(
                        [PackageSource.NuGetOrg]))
                {
                    PackageCandidate = binding.Candidate,
                },
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                PortablePdbSettlementResult.Failed>(result);
        Assert.Equal(
            PortablePdbSettlementFailureKind.PositiveStoreFailed,
            failed.Failure);
        Assert.Equal(
            PortablePdbStoreFailureKind.PublicationNotRetained,
            failed.StoreFailure);
        Assert.Contains(
            failed.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .PackageLocal
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome
                        .Failed
                && receipt.StoreFailure
                    == PortablePdbStoreFailureKind
                        .PublicationNotRetained);
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_ReferencePrimaryBindsImplementationFirst()
    {
        const string ReferencePath =
            "ref/net11.0/SettlementWitness.dll";
        const string ImplementationPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        string testPdb =
            Path.ChangeExtension(testAssembly, ".pdb");
        byte[] archive = CreateFrameworkArchive(
            (ReferencePath, File.ReadAllBytes(testAssembly)),
            (ImplementationPath, File.ReadAllBytes(testAssembly)),
            (PdbPath, File.ReadAllBytes(testPdb)));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;

        Assert.Equal(
            ReferencePath,
            binding.Candidate.Row.CompileEntry.Path);
        Assert.Equal(
            ImplementationPath,
            Assert.IsType<
                AssemblyResolutionProvenance.PackageAsset>(
                    binding.Assembly.Provenance)
                .AssetPath);
        Assert.Collection(
            packageSource.Queries,
            query => Assert.IsType<
                PackageHouseContentTerminal
                    .LibraryAndInventoryForTarget>(
                    Assert.Single(query.Terminals)),
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [ImplementationPath],
                    files.Entries);
            });

        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new RejectNetworkHandler());
        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            source.Context,
                            binding.Assembly,
                            client,
                            new InMemoryPdbStore(),
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackageCandidate =
                                binding.Candidate,
                        },
                        TestContext.Current
                            .CancellationToken));

        Assert.Equal(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.Collection(
            packageSource.Queries,
            _ => { },
            _ => { },
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [PdbPath],
                    files.Entries);
            });
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_BindsRequestedNonDefaultLibrary()
    {
        const string DefaultPath =
            "lib/net11.0/Alpha.dll";
        const string RequestedPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] archive = CreateFrameworkArchive(
            (DefaultPath, File.ReadAllBytes(testAssembly)),
            (RequestedPath, File.ReadAllBytes(testAssembly)),
            (
                PdbPath,
                File.ReadAllBytes(
                    Path.ChangeExtension(
                        testAssembly,
                        ".pdb"))));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());

        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            selectedLibraryPath:
                                RequestedPath,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;

        Assert.Equal(
            DefaultPath,
            binding.Library.Selection
                .SelectedRow.CompileEntry.Path);
        Assert.Equal(
            RequestedPath,
            binding.Candidate.Row.CompileEntry.Path);
        Assert.Collection(
            packageSource.Queries,
            query => Assert.IsType<
                PackageHouseContentTerminal
                    .LibraryAndInventoryForTarget>(
                    Assert.Single(query.Terminals)),
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [RequestedPath],
                    files.Entries);
            });

        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new RejectNetworkHandler());
        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    await PortablePdbSettlement.SettleAsync(
                        new PortablePdbSettlementRequest(
                            source.Context,
                            binding.Assembly,
                            client,
                            new InMemoryPdbStore(),
                            new UniformPackageSourceAuthorization(
                                [PackageSource.NuGetOrg]))
                        {
                            PackageCandidate =
                                binding.Candidate,
                        },
                        TestContext.Current
                            .CancellationToken));

        Assert.Equal(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.Same(
            binding.Candidate.Row,
            acquired.PackageLibraryRow);
        Assert.Collection(
            packageSource.Queries,
            _ => { },
            _ => { },
            query =>
            {
                var files = Assert.IsType<
                    PackageHouseContentTerminal.Files>(
                        Assert.Single(query.Terminals));
                Assert.Equal(
                    [PdbPath],
                    files.Entries);
            });
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_CacheOnlySkipsPackageAcquisition()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] archive = CreateFrameworkArchive(
            (DllPath, File.ReadAllBytes(testAssembly)),
            (
                PdbPath,
                File.ReadAllBytes(
                    Path.ChangeExtension(
                        testAssembly,
                        ".pdb"))));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;
        Assert.Single(packageSource.Queries);

        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new RejectNetworkHandler());
        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    binding.Assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [PackageSource.NuGetOrg]))
                {
                    CacheOnly = true,
                    PackageCandidate = binding.Candidate,
                },
                TestContext.Current.CancellationToken);

        Assert.IsType<
            PortablePdbSettlementResult.Unavailable>(result);
        Assert.Single(packageSource.Queries);
        Assert.Contains(
            result.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .PackageLocal
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome
                        .Skipped
                && receipt.SkipReason
                    == PortablePdbSettlementSkipReason
                        .CacheOnly);
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_RejectsMismatchedImplementationIdentity()
    {
        byte[] archive = CreateFrameworkArchive(
            (
                "ref/net11.0/SettlementWitness.dll",
                File.ReadAllBytes(
                    typeof(PackageRangedRealizationTests)
                        .Assembly.Location)),
            (
                "lib/net11.0/SettlementWitness.dll",
                File.ReadAllBytes(
                    typeof(PackageHouse)
                        .Assembly.Location)));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());

        var terminal =
            Assert.IsType<
                PortablePdbPackageBindingResult.Terminal>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken));

        Assert.Equal(
            PortablePdbPackageBindingFailureKind
                .AssemblyIdentityMismatch,
            terminal.Failure);
        Assert.Equal(2, packageSource.Queries.Count);
    }

    [Fact]
    public async Task
        PackagePortablePdbSettlement_ExternalProviderCanFollowRejectedPackagePdb()
    {
        const string DllPath =
            "lib/net11.0/SettlementWitness.dll";
        const string PdbPath =
            "lib/net11.0/SettlementWitness.pdb";
        string testAssembly =
            typeof(PackageRangedRealizationTests)
                .Assembly.Location;
        byte[] expectedPdb =
            File.ReadAllBytes(
                Path.ChangeExtension(
                    testAssembly,
                    ".pdb"));
        string foreignAssembly =
            typeof(PackageHouse).Assembly.Location;
        byte[] foreignPdb =
            File.ReadAllBytes(
                Path.ChangeExtension(
                    foreignAssembly,
                    ".pdb"));
        byte[] archive = CreateFrameworkArchive(
            (DllPath, File.ReadAllBytes(testAssembly)),
            (PdbPath, foreignPdb));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive)
                {
                    Host = "api.nuget.org",
                },
                PackageSource.NuGetOrg);
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;
        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new RawPdbNetworkHandler(expectedPdb));

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    binding.Assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [PackageSource.NuGetOrg]))
                {
                    PackageCandidate = binding.Candidate,
                },
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    result);
        Assert.NotEqual(
            PortablePdbSettlementSource.PackageLocal,
            acquired.Source);
        Assert.Contains(
            acquired.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .PackageLocal
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome
                        .Rejected);
        Assert.Contains(
            acquired.Receipts,
            receipt =>
                receipt.Candidate
                    is PortablePdbSettlementCandidate
                        .SymbolPackage
                    or PortablePdbSettlementCandidate
                        .NuGetSymbolServer
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome
                        .Acquired);
    }

    [Theory]
    [InlineData(
        "lib/net11.0/SettlementWitness.dll",
        PackageHousePortablePdbEvidenceKind.Absent,
        PortablePdbSettlementAttemptOutcome.Unavailable)]
    [InlineData(
        "ref/net11.0/SettlementWitness.dll",
        PackageHousePortablePdbEvidenceKind.NotApplicable,
        PortablePdbSettlementAttemptOutcome.Skipped)]
    public async Task
        PackagePortablePdbSettlement_NegativeInventoryDoesNotRequestPackageFile(
        string assemblyPath,
        PackageHousePortablePdbEvidenceKind evidenceKind,
        PortablePdbSettlementAttemptOutcome expectedOutcome)
    {
        byte[] archive = CreateFrameworkArchive(
            (
                assemblyPath,
                File.ReadAllBytes(
                    typeof(PackageRangedRealizationTests)
                        .Assembly.Location)));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    AddressPackageId,
                    AddressPackageVersion,
                    archive));
        var packageSource =
            new TestPortablePdbPackageContentSource(
                environment,
                new InMemoryPackageStore());
        PortablePdbPackageBinding binding =
            Assert.IsType<
                PortablePdbPackageBindingResult.Bound>(
                    await PortablePdbPackageComposition
                        .PrepareAsync(
                            PackageSourceCoordinate.Create(
                                AddressPackageId,
                                AddressPackageVersion),
                            PackageHouseTargetContext
                                .Exact("net11.0"),
                            packageSource,
                            cancellationToken:
                                TestContext.Current
                                    .CancellationToken))
                .Value;
        Assert.Equal(
            evidenceKind,
            binding.Candidate.Row.PortablePdbEvidence);
        using SourceLinkService source =
            SourceLinkService.OpenEmbeddedPdbOnly(
                binding.Assembly);
        using var client =
            new HttpClient(
                new NotFoundNetworkHandler());

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    binding.Assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [PackageSource.NuGetOrg]))
                {
                    PackageCandidate =
                        binding.Candidate,
                },
                TestContext.Current.CancellationToken);

        Assert.IsType<
            PortablePdbSettlementResult.Unavailable>(
                result);
        Assert.Single(packageSource.Queries);
        PortablePdbSettlementReceipt receipt =
            Assert.Single(
                result.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .PackageLocal);
        Assert.Equal(
            expectedOutcome,
            receipt.Outcome);
    }

    [Fact]
    public async Task
        LibraryInventory_RangeCompleteAndCacheAgree()
    {
        byte[] archive = CreateLibraryInventoryArchive();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");
        PackageHouseContentQuery query =
            PackageHouseContentQuery
                .GetLibraryAndInventoryForTarget(target);

        var rangedServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment rangedEnvironment =
            RangedEnvironment.Create(rangedServer);
        var ranged = Assert.IsType<PackageHouseSettlement.Acquired>(
            await rangedEnvironment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        var completeServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment completeEnvironment =
            RangedEnvironment.Create(completeServer);
        using var store = new TemporaryFileSystemPackageStore();
        var complete = Assert.IsType<PackageHouseSettlement.Acquired>(
            await completeEnvironment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));
        var cached = Assert.IsType<PackageHouseSettlement.Acquired>(
            await completeEnvironment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        AssertEquivalent(ranged);
        AssertEquivalent(complete);
        AssertEquivalent(cached);
        Assert.Equal(1, completeServer.FullRequests);

        static void AssertEquivalent(
            PackageHouseSettlement.Acquired acquired)
        {
            Assert.IsType<PackageHouseResult.Settled>(
                acquired.Result);
            Assert.Equal(
                ["ref/net10.0/Alpha.dll"],
                acquired.Payload.Content.EnumerateEntries());
            PackageHouseLibraryAndInventory result =
                Assert.IsType<PackageHouseLibraryAndInventory>(
                    acquired.Result.Evidence.LibraryAndInventory);
            Assert.Equal(
                [
                    "ref/net10.0/Alpha.dll",
                    "ref/net10.0/Beta.dll",
                    "ref/net10.0/Gamma.dll",
                ],
                result.Inventory.Rows.Select(
                    static row => row.CompileEntry.Path));
            Assert.Equal(
                "lib/net10.0/Alpha.pdb",
                result.Selection.SelectedRow.PortablePdbEntry?.Path);
        }
    }

    [Fact]
    public async Task TfmFiles_ExistingEntryOutsideTargetFailsWithoutBodyRead()
    {
        const string OutsideTarget =
            "lib/net10.0/System.Text.Json.dll";
        byte[] archive = CreateTfmSemanticArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0", "linux-x64");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.TfmFilesWithFileList(
                    target,
                    [OutsideTarget]),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Contains(
            OutsideTarget,
            noMatch.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        Assert.DoesNotContain(
            noMatch.Evidence.FileList!.Entries,
            entry => entry.Path.Equals(
                OutsideTarget,
                StringComparison.Ordinal));
        Assert.Equal(
            [
                PackageTransferRequestPurpose.SizeProbe,
                PackageTransferRequestPurpose.DirectoryTail,
            ],
            Transfer(acquired, PackagePayloadOrigin.Ranged)
                .Requests
                .Select(static request => request.Purpose));
    }

    [Fact]
    public async Task TfmFileList_UnmatchedTargetIsTypedNoMatch()
    {
        byte[] archive = CreateTfmSemanticArchive();
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net40");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.TfmFileList(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Empty(noMatch.Evidence.FileList!.Entries);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            noMatch.Evidence.ContentNarrowing!
                .TargetSelection!
                .Selection
                .Status);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task TfmFilesAndFileList_RangeCompleteAndCacheAgree()
    {
        byte[] archive = CreateTfmSemanticArchive();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0", "linux-x64");
        PackageHouseContentQuery query =
            PackageHouseContentQuery.TfmFilesWithFileList(
                target,
                [AddressApiPath]);

        var rangedServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment rangedEnvironment =
            RangedEnvironment.Create(rangedServer);
        var ranged = Assert.IsType<PackageHouseSettlement.Acquired>(
            await rangedEnvironment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        var completeServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        await using RangedEnvironment completeEnvironment =
            RangedEnvironment.Create(completeServer);
        using var store = new TemporaryFileSystemPackageStore();
        var complete = Assert.IsType<PackageHouseSettlement.Acquired>(
            await completeEnvironment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));
        var cached = Assert.IsType<PackageHouseSettlement.Acquired>(
            await completeEnvironment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        AssertEquivalent(ranged);
        AssertEquivalent(complete);
        AssertEquivalent(cached);
        Assert.Equal(1, completeServer.FullRequests);

        static void AssertEquivalent(
            PackageHouseSettlement.Acquired acquired)
        {
            Assert.IsType<PackageHouseResult.Settled>(
                acquired.Result);
            Assert.Equal(
                [AddressApiPath],
                acquired.Payload.Content.EnumerateEntries());
            Assert.Equal(
                [
                    AddressApiPath,
                    "ref/net10.0/System.Text.Json.xml",
                    "runtimes/linux-x64/lib/net10.0/System.Text.Json.dll",
                    "runtimes/linux-x64/lib/net10.0/System.Text.Json.pdb",
                ],
                acquired.Result.Evidence.FileList!.Entries.Select(
                    static entry => entry.Path));
            Assert.Equal(
                PackageCompileAssetSelectionStatus.Selected,
                acquired.Result.Evidence.ContentNarrowing!
                    .TargetSelection!
                    .Selection
                    .Status);
        }
    }

    [Fact]
    public async Task
        TfmFiles_FailedSourceNarrowingDoesNotCrossToSuccessfulSource()
    {
        const string Net9Path =
            "lib/net9.0/System.Text.Json.dll";
        const string Net10Path =
            "lib/net10.0/System.Text.Json.dll";
        byte[] padding = new byte[128 * 1024];
        new Random(42).NextBytes(padding);
        var failing = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            CreateFrameworkArchive(
                (Net9Path, new byte[] { 9 }),
                ("content/padding.bin", padding)))
        {
            Host = "failed-narrowing.example",
            EntryRangeStatus = HttpStatusCode.InternalServerError,
            SubsequentFullRequestStatus = HttpStatusCode.BadGateway,
        };
        var successful = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            CreateFrameworkArchive(
                (Net9Path, new byte[] { 9 }),
                (Net10Path, new byte[] { 10 })))
        {
            Host = "successful-narrowing.example",
            OmitLength = true,
        };
        await using RangedEnvironment environment =
            RangedEnvironment.Create(failing, successful);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.TfmFiles(
                    target,
                    [Net9Path]),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Equal(
            "net10.0",
            noMatch.Evidence.ContentNarrowing!
                .TargetSelection!
                .Selection
                .TargetFramework);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.True(failing.RangedRequests > 1);
        Assert.Equal(1, successful.FullRequests);
    }

    [Fact]
    public async Task
        TfmFiles_ForeignCacheUsesRetainedArchiveDirectory()
    {
        const string DllPath =
            "lib/net10.0/System.Text.Json.dll";
        const string XmlPath =
            "lib/net10.0/System.Text.Json.xml";
        byte[] xml = "archive documentation"u8.ToArray();
        byte[] archive = CreateFrameworkArchive(
            (DllPath, new byte[] { 1 }),
            (XmlPath, xml));
        string root = Directory.CreateTempSubdirectory(
            "inspect-tfm-cache-").FullName;
        try
        {
            string nupkg = System.IO.Path.Combine(
                root,
                $"{AddressPackageId}.{AddressPackageVersion}.nupkg");
            File.WriteAllBytes(nupkg, archive);
            string library = System.IO.Path.Combine(
                root,
                "lib",
                "net10.0");
            Directory.CreateDirectory(library);
            File.WriteAllBytes(
                System.IO.Path.Combine(
                    library,
                    "System.Text.Json.dll"),
                [1]);
            File.WriteAllText(
                System.IO.Path.Combine(
                    root,
                    $"{AddressPackageId}.nuspec"),
                "<package />");

            var server = new RangeFeed(
                AddressPackageId,
                AddressPackageVersion,
                archive);
            await using RangedEnvironment environment =
                RangedEnvironment.Create(server);
            ConfiguredPackageAuthority authority =
                Assert.Single(
                    environment.Authorization
                        .AuthorizeSourcesFor(AddressPackageId)
                        .Authorities);
            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: PackageSourceClientFactory
                    .GetProducerIdentity(authority.Source)
                    .Key);
            PackageHouseTargetContext target =
                PackageHouseTargetContext.Exact("net10.0");

            var acquired =
                Assert.IsType<PackageHouseSettlement.Acquired>(
                    await environment.AcquireContentAsync(
                        new CachedContentStore(content),
                        PackageHouseContentQuery
                            .TfmFilesWithFileList(
                                target,
                                [XmlPath]),
                        packageId: AddressPackageId,
                        version: AddressPackageVersion,
                        targetContext: target));

            Assert.IsType<PackageHouseResult.Settled>(
                acquired.Result);
            Assert.Equal(
                [DllPath, XmlPath],
                acquired.Result.Evidence.FileList!.Entries.Select(
                    static entry => entry.Path));
            Assert.Equal([XmlPath], acquired.Payload.Content.EnumerateEntries());
            Assert.True(
                acquired.Payload.Content.TryOpenEntry(
                    XmlPath,
                    out Stream? selected));
            using (selected)
                Assert.Equal(xml, ReadAllBytes(selected));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        TfmFiles_RetainedEvidenceRevalidatesBeforeBodyAcquisition()
    {
        const string Net9Path =
            "lib/net9.0/System.Text.Json.dll";
        const string Net10Path =
            "lib/net10.0/System.Text.Json.dll";
        byte[] firstArchive =
            CreateFrameworkArchive(
                (Net9Path, new byte[] { 9 }));
        var firstServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            firstArchive);
        await using RangedEnvironment firstEnvironment =
            RangedEnvironment.Create(firstServer);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net10.0");
        var first = Assert.IsType<PackageHouseSettlement.Acquired>(
            await firstEnvironment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.TfmFileList(target),
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));
        PackageContentEntry retainedEntry = Assert.Single(
            first.Result.Evidence.FileList!.Entries);
        PackageHouseContentQuery retainedQuery =
            first.Result.Evidence.FileList.CreateFilesQuery(
                [retainedEntry]);

        byte[] currentArchive =
            CreateFrameworkArchive(
                (Net9Path, new byte[] { 9 }),
                (Net10Path, new byte[] { 10 }));
        var currentServer = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            currentArchive);
        await using RangedEnvironment currentEnvironment =
            RangedEnvironment.Create(currentServer);

        var current = Assert.IsType<PackageHouseSettlement.Acquired>(
            await currentEnvironment.AcquireContentAsync(
                new InMemoryPackageStore(),
                retainedQuery,
                packageId: AddressPackageId,
                version: AddressPackageVersion,
                targetContext: target));

        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(current.Result);
        Assert.Equal(
            "net10.0",
            noMatch.Evidence.ContentNarrowing!
                .TargetSelection!
                .Selection
                .TargetFramework);
        Assert.Empty(current.Payload.Content.EnumerateEntries());
        Assert.Equal(
            [
                PackageTransferRequestPurpose.SizeProbe,
                PackageTransferRequestPurpose.DirectoryTail,
            ],
            Transfer(current, PackagePayloadOrigin.Ranged)
                .Requests
                .Select(static request => request.Purpose));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SemanticFilesAndFileList_RangedInadmissibleDirectoryIsRejected(
        bool cacheDirectory,
        bool unsafePath)
    {
        const string PackageId = "Rejected.Directory";
        const string Version = "1.0.0";
        byte[] archive = unsafePath
            ? TestPackageArchive.CreateWithContent(
                ("README.md", "valid"u8.ToArray()),
                ("../escape.txt", "invalid"u8.ToArray()),
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()))
            : TestPackageArchive.CreateWithContent(
                ("README.md", "valid"u8.ToArray()),
                ("content", "file"u8.ToArray()),
                ("content/readme.txt", "descendant"u8.ToArray()),
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()));
        var store = new InMemoryPackageStore();
        IPackageEntryStore entryStore = store;
        if (cacheDirectory)
        {
            ZipDirectory directory =
                ZipArchiveReader.ReadDirectoryFromRegion(
                    archive,
                    archive.LongLength,
                    ZipReadLimits.Default);
            await entryStore.PublishDirectoryAsync(
                PackageId,
                Version,
                directory.Region,
                directory.ArchiveLength);
        }

        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var settlement =
            Assert.IsType<PackageHouseSettlement.ResourceFree>(
                await environment.AcquireContentAsync(
                    store,
                    PackageHouseContentQuery.PackageFilesWithFileList(
                        ["README.md"]),
                    packageId: PackageId,
                    version: Version));

        PackageHouseResult.Rejected rejected =
            Assert.IsType<PackageHouseResult.Rejected>(settlement.Result);
        Assert.DoesNotContain(
            "../escape.txt",
            rejected.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Null(settlement.Result.Evidence.FileList);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(1, server.RangedRequests);
        Assert.Null(
            await entryStore.ReadEntryAsync(
                PackageId,
                Version,
                "README.md"));
        Assert.Equal(
            cacheDirectory,
            await entryStore.ReadDirectoryAsync(PackageId, Version) is not null);
        Assert.Equal(
            cacheDirectory,
            environment.Log.Any(
                static message => message.Contains(
                    "cannot be admitted",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SemanticFiles_MissingEntryFailsVisibly()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"])));

        var noMatch = Assert.IsType<PackageHouseResult.NoMatch>(
            acquired.Result);
        Assert.Contains(
            "'README.md'",
            noMatch.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFailure.Stage failure =
            Assert.IsType<PackageHouseFailure.Stage>(
                Assert.Single(noMatch.Evidence.Failures));
        Assert.Equal(
            PackageHouseFailureStage.Selection,
            failure.StageKind);
    }

    [Fact]
    public async Task SemanticFiles_CaseAliasedArchiveIsRejectedBeforeSelection()
    {
        const string PackageId = "Ambiguous.Files";
        const string Version = "1.0.0";
        byte[] archive = CreateCaseAmbiguousArchive();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(PackageId, Version, archive));

        var houseResult = Assert.IsType<PackageHouseSettlement.ResourceFree>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"]),
                packageId: PackageId,
                version: Version));
        Assert.IsType<PackageHouseResult.Rejected>(houseResult.Result);

        PackageFileAcquisitionResult result =
            await environment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md",
                packageId: PackageId,
                version: Version);

        Assert.IsType<PackageFileAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PackageFileAcquisitionStatus.NotSettled,
            result.Status);
        Assert.IsType<PackageHouseSettlement.ResourceFree>(
            result.Settlement);
    }

    [Fact]
    public void SemanticFiles_AdmittedCaseAmbiguityIsReported()
    {
        var files = new PackageHouseContentTerminal.Files(["README.md"]);

        PackageHouseFilesResolution resolution =
            files.Resolve(["README.md", "readme.md"]);

        Assert.Empty(resolution.SelectedEntries);
        Assert.Empty(resolution.MissingEntries);
        Assert.Equal(["README.md"], resolution.AmbiguousEntries);
    }

    [Fact]
    public async Task SemanticFiles_GlobalPackagesOmissionReadsFromRetainedArchive()
    {
        const string Path = "[Content_Types].xml";
        byte[] archiveBytes = TestPackageArchive.Create(
            "lib/net10.0/Sample.dll",
            "PCLStorage.nuspec",
            Path,
            "_rels/.rels");
        string root = Directory.CreateTempSubdirectory(
            "inspect-global-package-").FullName;
        try
        {
            string nupkg = System.IO.Path.Combine(
                root,
                $"{PclStorage}.{PclStorageVersion}.nupkg");
            File.WriteAllBytes(nupkg, archiveBytes);
            Directory.CreateDirectory(
                System.IO.Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                System.IO.Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1, 2, 3]);
            File.WriteAllText(
                System.IO.Path.Combine(root, "pclstorage.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllText(
                System.IO.Path.Combine(root, ".nupkg.metadata"),
                "{}");

            await using RangedEnvironment environment =
                RangedEnvironment.Create(
                    new RangeFeed(
                        PclStorage,
                        PclStorageVersion,
                        archiveBytes));
            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: PackageSourceClientFactory
                    .GetProducerIdentity(
                        Assert.Single(
                            environment.Authorization
                                .AuthorizeSourcesFor(PclStorage)
                                .Authorities)
                            .Source)
                    .Key);
            Assert.False(content.TryOpenEntry(Path, out _));

            PackageFileAcquisitionResult.Acquired acquired =
                Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                    await environment.AcquireFileAsync(
                        new CachedContentStore(content),
                        Path));
            Assert.Equal(
                PackagePayloadOrigin.Cache,
                acquired.Settlement.Payload.Origin);
            Assert.Contains(
                acquired.FileList.Entries,
                entry => entry.Path == Path);

            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    out Stream? selected));
            using (selected)
            {
                Assert.Equal(
                    ReadEntry(archiveBytes, Path),
                    ReadAllBytes(selected));
            }

            PackageContentEntry selectedEntry =
                Assert.Single(
                    Assert.IsAssignableFrom<
                            IPackageContentEntryManifest>(
                            acquired.Settlement.Payload.Content)
                        .EnumerateEntriesWithLengths());
            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length,
                    out Stream? bounded));
            using (bounded)
            {
                Assert.Equal(
                    ReadEntry(archiveBytes, Path),
                    ReadAllBytes(bounded));
            }
            Assert.Throws<InvalidDataException>(
                () => acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length - 1,
                    out _));

            await using PackageHousePayloadRead read =
                acquired.OpenRead();
            using var actual = new MemoryStream();
            await read.CopyToAsync(
                actual,
                TestContext.Current.CancellationToken);
            using var oracle = new ZipArchive(
                new MemoryStream(archiveBytes));
            ZipArchiveEntry? expectedEntry =
                oracle.GetEntry(Path);
            Assert.NotNull(expectedEntry);
            using Stream expected = expectedEntry.Open();
            using var expectedBytes = new MemoryStream();
            expected.CopyTo(expectedBytes);
            Assert.Equal(
                expectedBytes.ToArray(),
                actual.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticFiles_GlobalPackagesDivergentPhysicalEntryIsRejected()
    {
        const string Path = "lib/net10.0/Sample.dll";
        byte[] archiveBytes = TestPackageArchive.Create(
            Path,
            "PCLStorage.nuspec");
        string root = Directory.CreateTempSubdirectory(
            "inspect-global-package-divergent-").FullName;
        try
        {
            string nupkg = System.IO.Path.Combine(
                root,
                $"{PclStorage}.{PclStorageVersion}.nupkg");
            File.WriteAllBytes(nupkg, archiveBytes);
            Directory.CreateDirectory(
                System.IO.Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                System.IO.Path.Combine(
                    root,
                    "lib",
                    "net10.0",
                    "Sample.dll"),
                [3, 2, 1]);
            File.WriteAllText(
                System.IO.Path.Combine(root, "pclstorage.nuspec"),
                """<?xml version="1.0"?><package />""");

            await using RangedEnvironment environment =
                RangedEnvironment.Create(
                    new RangeFeed(
                        PclStorage,
                        PclStorageVersion,
                        archiveBytes));
            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: PackageSourceClientFactory
                    .GetProducerIdentity(
                        Assert.Single(
                            environment.Authorization
                                .AuthorizeSourcesFor(PclStorage)
                                .Authorities)
                            .Source)
                    .Key);

            PackageFileAcquisitionResult.Acquired acquired =
                Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                    await environment.AcquireFileAsync(
                        new CachedContentStore(content),
                        Path));
            Assert.Equal(
                PackagePayloadOrigin.Cache,
                acquired.Settlement.Payload.Origin);

            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    out Stream? selected));
            using (selected)
            {
                Assert.Throws<InvalidDataException>(
                    () => ReadAllBytes(selected));
            }

            PackageContentEntry selectedEntry =
                Assert.Single(
                    Assert.IsAssignableFrom<
                            IPackageContentEntryManifest>(
                            acquired.Settlement.Payload.Content)
                        .EnumerateEntriesWithLengths());
            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length,
                    out Stream? bounded));
            using (bounded)
            {
                Assert.Throws<InvalidDataException>(
                    () => ReadAllBytes(bounded));
            }

            InspectionEnvelope<PackageFileContentDocument> inspection =
                await PackageFileContentInspection.ExecuteAsync(
                    new(
                        acquired,
                        PackageDocumentContentLimits.MaxDecodedBytes),
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                PackageFileContentStatus.Failed,
                inspection.Content.Status);
            Assert.Empty(inspection.Content.Content);
            Assert.Equal(
                "package-file-content.read-failed",
                Assert.Single(inspection.Diagnostics).Code);
            Assert.Contains(
                "declared size or checksum",
                inspection.Content.Detail?.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticFiles_RequiresHouseOwnedPayloadPlanning()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PclStorage,
                    PclStorageVersion)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            contentQuery:
                PackageHouseContentQuery.PackageFiles(["README.md"]));
        var callerPlanned = new PackageHouse(
            environment.Authorization,
            new PackagePayloadAcquisitionPlan(
                static (_, _) => new InMemoryPackageStore(),
                access: PackagePayloadAccess.Ranged));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => callerPlanned.ExecuteAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout)));
        Assert.Contains(
            "House-planned",
            failure.Message,
            StringComparison.Ordinal);
    }

    private static byte[] CreateTfmSemanticArchive()
    {
        byte[] assembly = File.ReadAllBytes(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PackageHouse",
                "System.Text.Json.dll"));
        byte[] manifest = System.Text.Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><package><metadata>"
            + $"<id>{AddressPackageId}</id>"
            + $"<version>{AddressPackageVersion}</version>"
            + "<authors>test</authors>"
            + "<description>test</description>"
            + "</metadata></package>");
        return TestPackageArchive.CreateWithContent(
            ($"{AddressPackageId}.nuspec", manifest),
            (AddressApiPath, assembly),
            ("ref/net10.0/System.Text.Json.xml", "docs"u8.ToArray()),
            ("lib/net10.0/System.Text.Json.dll", [1]),
            ("lib/net10.0/System.Text.Json.pdb", "lib-pdb"u8.ToArray()),
            (
                "runtimes/linux-x64/lib/net10.0/System.Text.Json.dll",
                assembly),
            (
                "runtimes/linux-x64/lib/net10.0/System.Text.Json.pdb",
                "runtime-pdb"u8.ToArray()),
            (
                "runtimes/win-x64/lib/net10.0/System.Text.Json.dll",
                [2]),
            ("ref/net9.0/System.Text.Json.dll", [3]),
            ("lib/net9.0/System.Text.Json.dll", [4]),
            ("README.md", "outside target"u8.ToArray()));
    }

    private static byte[] CreateLibraryInventoryArchive()
    {
        byte[] assembly = File.ReadAllBytes(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PackageHouse",
                "System.Text.Json.dll"));
        return CreateFrameworkArchive(
            ("ref/net10.0/Alpha.dll", assembly),
            ("ref/net10.0/Beta.dll", assembly),
            ("ref/net10.0/Gamma.dll", assembly),
            ("lib/net10.0/Alpha.dll", assembly),
            ("lib/net10.0/Alpha.pdb", "alpha-pdb"u8.ToArray()),
            ("lib/net10.0/Beta.dll", assembly),
            ("README.md", "outside target"u8.ToArray()));
    }

    private static byte[] CreateFrameworkArchive(
        params (string Path, byte[] Content)[] entries)
    {
        byte[] manifest = System.Text.Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><package><metadata>"
            + $"<id>{AddressPackageId}</id>"
            + $"<version>{AddressPackageVersion}</version>"
            + "<authors>test</authors>"
            + "<description>test</description>"
            + "</metadata></package>");
        return TestPackageArchive.CreateWithContent(
            [
                ($"{AddressPackageId}.nuspec", manifest),
                .. entries,
            ]);
    }

    private sealed class TestPortablePdbPackageContentSource(
        RangedEnvironment environment,
        IPackageStore store)
        : IPortablePdbPackageContentSource
    {
        internal List<PackageHouseContentQuery> Queries { get; } =
            [];

        internal List<PackageHouseSettlement> Settlements { get; } =
            [];

        public async Task<PackageHouseSettlement> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageHouseContentQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            PackageHouseSettlement settlement =
                await environment.AcquireContentAsync(
                        store,
                        query,
                        packageId: coordinate.PackageId,
                        version: coordinate.Version,
                        targetContext:
                            query.Narrowing
                                is PackageHouseContentNarrowing.TfmWide
                                    tfmWide
                                    ? tfmWide.Target
                                    : null)
                    .ConfigureAwait(false);
            Settlements.Add(settlement);
            return settlement;
        }
    }

    private sealed class PdbDroppingStore : IPdbStore
    {
        private readonly Dictionary<string, byte[]> _content =
            new(StringComparer.Ordinal);

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream?>(
                _content.TryGetValue(
                    key,
                    out byte[]? content)
                    ? new MemoryStream(
                        content,
                        writable: false)
                    : null);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            if (key.EndsWith(
                    ".pdb",
                    StringComparison.Ordinal))
            {
                return;
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(
                    copy,
                    cancellationToken)
                .ConfigureAwait(false);
            _content[key] = copy.ToArray();
        }

        public string? TryGetLocalPath(string key) =>
            null;
    }

    private sealed class RejectNetworkHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Package-local settlement performed external network work.");
    }

    private static ResolvedAssemblyReference
        CreatePackageAssembly(
        byte[] assemblyBytes,
        string assetPath) =>
        ResolvedAssemblyReference.CreateFromStreamIfManaged(
            () => new MemoryStream(
                assemblyBytes,
                writable: false),
            AssemblyResolutionProvenance.Package(
                AddressPackageId,
                AddressPackageVersion,
                "net11.0",
                rid: null,
                assetPath),
            lastWriteTimeUtc: null,
            assetFileName:
                Path.GetFileName(assetPath))
        ?? throw new InvalidOperationException(
            "The settlement witness must be a managed assembly.");

    private sealed class RawPdbNetworkHandler(
        byte[] pdb)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content =
                        new ByteArrayContent(pdb),
                });
    }

    private sealed class NotFoundNetworkHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.NotFound));
    }

    private static byte[] CreateCaseAmbiguousArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Write("README.md", "upper");
            Write("readme.md", "lower");

            void Write(string path, string text)
            {
                using StreamWriter writer =
                    new(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        return output.ToArray();
    }

    private static byte[] ReadEntry(
        byte[] archiveBytes,
        string path)
    {
        using var archive = new ZipArchive(
            new MemoryStream(archiveBytes));
        ZipArchiveEntry? entry = archive.GetEntry(path);
        Assert.NotNull(entry);
        using Stream stream = entry.Open();
        return ReadAllBytes(stream);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private sealed class CachedContentStore(IPackageContent content) :
        IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            allowedSourceKeys?.Contains(
                content.ProducerKey,
                StringComparer.Ordinal) == true
                ? content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The test store serves only cached package content.");
    }

    private sealed class TemporaryFileSystemPackageStore :
        IPackageStore,
        IPreparedPackageStore,
        IDisposable
    {
        private readonly string _root =
            Directory.CreateTempSubdirectory(
                "inspect-semantic-content-").FullName;
        private IPackageContent? _content;

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            _content is { } content
            && allowedSourceKeys?.Contains(
                content.ProducerKey,
                StringComparer.Ordinal) == true
                ? content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The test store requires prepared package commits.");

        async ValueTask<PreparedPackageCommit>
            IPreparedPackageStore.CommitPreparedAsync(
            string packageName,
            string version,
            string sourceKey,
            PackageArchivePayload archive,
            CancellationToken cancellationToken)
        {
            PreparedPackageCommit prepared =
                await FileSystemPackageStore.CommitAsync(
                    packageName,
                    version,
                    archive,
                    () => Directory.CreateDirectory(
                        Path.Combine(
                            _root,
                            $"staging-{Guid.NewGuid():N}")).FullName,
                    (extractedPath, nupkgPath) =>
                        NuGetCache.CommitPackageToSlotWithDisposition(
                            extractedPath,
                            nupkgPath,
                            packageName,
                            version,
                            sourceKey,
                            Path.Combine(_root, "slot"),
                            "semantic-content-test",
                            useAppCache: false),
                    cancellationToken);
            _content = prepared.Content;
            return prepared;
        }

        public void Dispose() =>
            Directory.Delete(_root, recursive: true);
    }
}
