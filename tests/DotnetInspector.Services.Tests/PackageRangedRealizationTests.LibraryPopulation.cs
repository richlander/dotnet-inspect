using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task
        LibraryPopulation_RuntimeOnlyReadsOneSelectedRuntimeFolder()
    {
        const string PackageId = "RuntimeOnly";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("runtimes/linux-x64/lib/net10.0/RuntimeOnly.dll", new byte[13]),
            ("runtimes/linux-x64/lib/net10.0/Companion.dll", new byte[17]),
            ("runtimes/linux-x64/native/Native.dll", new byte[19]),
            ("analyzers/dotnet/cs/Analyzer.dll", new byte[23]),
            ("build/net10.0/Task.dll", new byte[29]));
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        var content =
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            [
                "runtimes/linux-x64/lib/net10.0/Companion.dll",
                "runtimes/linux-x64/lib/net10.0/RuntimeOnly.dll",
            ],
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var population = Assert.IsType<
            PackageLibraryPopulationSelection.Runtime>(
                realization.Selection);
        Assert.Same(
            content.GenerationIdentity,
            population.CompileReceipt.Generation);
        Assert.Same(
            content.GenerationIdentity,
            population.RuntimeReceipt.Generation);
        Assert.Equal(0, server.FullRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_CompileSelectionPreventsRuntimeBodyReads()
    {
        const string PackageId = "CompileFirst";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/CompileFirst.dll", new byte[13]),
            ("runtimes/linux-x64/lib/net10.0/CompileFirst.dll", new byte[17]),
            ("analyzers/dotnet/cs/Analyzer.dll", new byte[19]));
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        var content =
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            ["lib/net10.0/CompileFirst.dll"],
            content.MaterializedEntries);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        Assert.IsType<PackageLibraryPopulationSelection.Compile>(
            realization.Selection);
        Assert.Equal(0, server.FullRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_CachedCompileSettlesWithoutManifestAcquisition()
    {
        const string PackageId = "CachedCompile";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", OrdinaryNuspec(PackageId, Version)),
            ("lib/net10.0/CachedCompile.dll", new byte[13]));
        var store = new InMemoryPackageStore();
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Complete,
                "net10.0",
                sizeCut: archive.Length,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));
        int manifestRequests = server.ManifestRequests;
        server.ManifestDelay = TimeSpan.FromSeconds(1);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation,
                operationTimeout:
                    TimeSpan.FromMilliseconds(50)));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        Assert.IsType<PackageLibraryPopulationSelection.Compile>(
            realization.Selection);
        Assert.Equal(manifestRequests, server.ManifestRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_DeclaredToolReadsOnlyTheSelectedToolLibraries()
    {
        const string PackageId = "DeclaredTool";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", ToolNuspec(PackageId, Version)),
            ("tools/net8.0/any/Older.dll", new byte[11]),
            ("tools/net10.0/any/DeclaredTool.dll", new byte[13]),
            ("tools/net10.0/any/runtimes/win/lib/net10.0/Companion.dll",
                new byte[17]),
            ("tools/net10.0/any/runtimes/win/native/Native.dll",
                new byte[19]),
            ("tools/net10.0/any/fr/DeclaredTool.resources.dll",
                new byte[23]),
            ("analyzers/dotnet/cs/Analyzer.dll", new byte[29]),
            ("build/net10.0/Task.dll", new byte[31]));
        var server = new RangeFeed(PackageId, Version, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation,
                libraryHandoff:
                    PackageHouseLibraryHandoffMode.SelectedLibraries));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        var content =
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        string[] expected =
        [
            "tools/net10.0/any/DeclaredTool.dll",
            "tools/net10.0/any/runtimes/win/lib/net10.0/Companion.dll",
        ];
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var population = Assert.IsType<
            PackageLibraryPopulationSelection.Tool>(
                realization.Selection);
        Assert.Same(
            content.GenerationIdentity,
            population.ToolEvidence.Generation);
        Assert.Same(
            content.GenerationIdentity,
            population.CompileReceipt.Generation);
        Assert.Same(
            content.GenerationIdentity,
            population.RuntimeReceipt.Generation);
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            realization.LibraryHandoffs
                .Select(handoff =>
                    Assert.IsType<PackageHouseLibraryHandoff.Tool>(handoff)
                        .AssetPath)
                .Order(StringComparer.Ordinal));
        Assert.Equal(1, server.ManifestRequests);
        Assert.Equal(0, server.FullRequests);
        PackageTransferReceipt transfer =
            Transfer(acquired, PackagePayloadOrigin.Ranged);
        Assert.Equal(
            server.FullRequests + server.RangedRequests,
            transfer.RequestCount);
        int packageRequests =
            server.FullRequests + server.RangedRequests;

        var cached = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation,
                libraryHandoff:
                    PackageHouseLibraryHandoffMode.SelectedLibraries));

        Assert.IsType<PackageHouseResult.Settled>(cached.Result);
        PackageTransferReceipt cachedTransfer =
            Transfer(cached, PackagePayloadOrigin.Cache);
        Assert.Equal(PackageTransferPath.EntryCache, cachedTransfer.Path);
        Assert.Equal(0, cachedTransfer.RequestCount);
        Assert.Equal(
            packageRequests,
            server.FullRequests + server.RangedRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_ManifestCannotAuthorizeAnotherAuthorityPayload()
    {
        const string PackageId = "CrossSourceTool";
        const string Version = "1.0.0";
        byte[] declared = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", ToolNuspec(PackageId, Version)),
            ("tools/net10.0/any/Declared.dll", new byte[11]));
        byte[] undeclared = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", OrdinaryNuspec(PackageId, Version)),
            ("tools/net10.0/any/Undeclared.dll", new byte[13]));
        var manifestOnly = new RangeFeed(PackageId, Version, declared)
        {
            Host = "manifest-only.example",
            RangedStatus = System.Net.HttpStatusCode.NotFound,
            FullRequestStatus = System.Net.HttpStatusCode.NotFound,
        };
        var payloadOnly = new RangeFeed(PackageId, Version, undeclared)
        {
            Host = "payload-only.example",
        };
        await using RangedEnvironment environment =
            RangedEnvironment.Create(manifestOnly, payloadOnly);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var population = Assert.IsType<
            PackageLibraryPopulationSelection.NonSuccess>(
                realization.Selection);
        Assert.False(population.ToolDeclarationUnavailable);
        Assert.Empty(
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
                .MaterializedEntries);
        Assert.Equal(0, manifestOnly.ManifestRequests);
        Assert.Equal(1, payloadOnly.ManifestRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_UndeclaredToolShapeStopsBeforeBodyTransfer()
    {
        const string PackageId = "UndeclaredTool";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", OrdinaryNuspec(PackageId, Version)),
            ("tools/net10.0/any/UndeclaredTool.dll", new byte[13]));
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Empty(
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
                .MaterializedEntries);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var population = Assert.IsType<
            PackageLibraryPopulationSelection.NonSuccess>(
                realization.Selection);
        Assert.Null(population.ToolEvidence);
        Assert.Null(population.ToolSelection);
        Assert.False(population.ToolDeclarationUnavailable);
        Assert.Equal(1, server.ManifestRequests);
        Assert.Equal(0, server.FullRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_UnavailableToolDeclarationStopsBeforeBodyTransfer()
    {
        const string PackageId = "UnknownTool";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ($"{PackageId}.nuspec", ToolNuspec(PackageId, Version)),
            ("tools/net10.0/any/UnknownTool.dll", new byte[13]));
        var server = new RangeFeed(PackageId, Version, archive)
        {
            ServeManifest = false,
        };
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Empty(
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
                .MaterializedEntries);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var population = Assert.IsType<
            PackageLibraryPopulationSelection.NonSuccess>(
                realization.Selection);
        Assert.True(population.ToolDeclarationUnavailable);
        Assert.NotEmpty(acquired.Result.Evidence.Failures);
        Assert.Equal(1, server.ManifestRequests);
        Assert.Equal(0, server.FullRequests);
    }

    [Fact]
    public async Task
        LibraryPopulation_MultipleRuntimeIdentifiersStopBeforeBodyTransfer()
    {
        const string PackageId = "AmbiguousRuntime";
        const string Version = "1.0.0";
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("runtimes/linux-x64/lib/net10.0/AmbiguousRuntime.dll", new byte[13]),
            ("runtimes/win-x64/lib/net10.0/AmbiguousRuntime.dll", new byte[17]),
            ("analyzers/dotnet/cs/Analyzer.dll", new byte[19]));
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net10.0",
                sizeCut: 0,
                PackageId,
                Version,
                selection:
                    PackageHouseAssetSelectionKind.LibraryPopulation));

        Assert.IsType<PackageHouseResult.Ambiguous>(acquired.Result);
        Assert.Empty(
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
                .MaterializedEntries);
        var realization = Assert.IsType<
            PackageHouseRealizationReceipt.LibraryPopulation>(
                acquired.Result.Evidence.Realization);
        var nonSuccess = Assert.IsType<
            PackageLibraryPopulationSelection.NonSuccess>(
                realization.Selection);
        Assert.IsType<PackageAssetSelection.Ambiguous>(
            nonSuccess.RuntimeReceipt!.Selection);
        Assert.Equal(0, server.FullRequests);
    }

    private static byte[] ToolNuspec(
        string packageId,
        string version) =>
        System.Text.Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
                <packageTypes>
                  <packageType name="DotnetTool" />
                </packageTypes>
              </metadata>
            </package>
            """);

    private static byte[] OrdinaryNuspec(
        string packageId,
        string version) =>
        System.Text.Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
              </metadata>
            </package>
            """);
}
