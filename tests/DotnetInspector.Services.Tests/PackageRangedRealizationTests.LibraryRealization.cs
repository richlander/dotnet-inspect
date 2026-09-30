using System.IO.Compression;
using System.Text;

using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Host-neutral exact Library realization gates for
/// <c>docs/design/package-read-demand.md#exact-library-realization</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task
        PackageLibraryRealization_ColdWarmAndMissing_AreOrchestrated()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        var cold = new RangeFeed(
            PclStorage,
            PclStorageVersion,
            archive);
        await using (RangedEnvironment first =
            RangedEnvironment.Create(cold))
        {
            var realized = Assert.IsType<
                PackageLibraryRealizationResult.Realized>(
                await first.RealizeLibraryAsync(
                    store,
                    "net45",
                    new PackageLibrarySelector("PCLStorage.dll"),
                    PackageLibraryRealizationDepth.Implementation));
            Assert.Equal(
                "lib/net45/PCLStorage.dll",
                realized.Handoff.Asset.Path);
            Assert.Same(
                realized.Realization,
                realized.Acquired.Result.Evidence.Realization);
            PackageTransferReceipt receipt = Transfer(
                realized.Settlement,
                PackagePayloadOrigin.Ranged);
            Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
            Assert.True(receipt.BytesReceived < archive.Length);
        }

        var warm = new RangeFeed(
            PclStorage,
            PclStorageVersion,
            archive);
        await using (RangedEnvironment second =
            RangedEnvironment.Create(warm))
        {
            var realized = Assert.IsType<
                PackageLibraryRealizationResult.Realized>(
                await second.RealizeLibraryAsync(
                    store,
                    "net45",
                    new PackageLibrarySelector(
                        "compile:lib/net45/PCLStorage.dll",
                        PackageLibrarySelectionKind.AssetId),
                    PackageLibraryRealizationDepth.Implementation));
            PackageTransferReceipt receipt = Transfer(
                realized.Settlement,
                PackagePayloadOrigin.Cache);
            Assert.Equal(PackageTransferPath.EntryCache, receipt.Path);
            Assert.Empty(receipt.Requests);
            Assert.Equal(
                0,
                warm.RangedRequests + warm.FullRequests);
        }

        await using RangedEnvironment missingEnvironment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    archive));
        PackageLibraryRealizationResult missing =
            await missingEnvironment.RealizeLibraryAsync(
                new InMemoryPackageStore(),
                "net45",
                new PackageLibrarySelector("Missing.dll"),
                PackageLibraryRealizationDepth.Selection);
        Assert.Equal(
            PackageLibraryRealizationStatus.Missing,
            missing.Status);
        Assert.IsType<PackageHouseResult.Settled>(
            missing.Settlement.Result);
    }

    [Fact]
    public async Task
        PackageLibraryRealization_DepthReachesRangedAcquisition()
    {
        byte[] archive = ReadAvalonia();
        await using RangedEnvironment apiEnvironment =
            RangedEnvironment.Create(
                new RangeFeed(
                    Avalonia,
                    AvaloniaVersion,
                    archive));
        var selection = Assert.IsType<
            PackageLibraryRealizationResult.Realized>(
            await apiEnvironment.RealizeLibraryAsync(
                new InMemoryPackageStore(),
                "net10.0",
                new PackageLibrarySelector(
                    "Avalonia.Dialogs.dll"),
                PackageLibraryRealizationDepth.Selection,
                packageId: Avalonia,
                version: AvaloniaVersion));
        var selectionContent = Assert.IsType<RangedPackageContent>(
            selection.Acquired.Payload.Content);
        int selectionImplementationEntries =
            selectionContent.MaterializedEntries.Count(
                path => path.StartsWith(
                    AvaloniaImplementation,
                    StringComparison.Ordinal));
        Assert.True(selectionImplementationEntries > 1);
        Assert.NotNull(selection.Handoff.ImplementationAsset);

        await using RangedEnvironment implementationEnvironment =
            RangedEnvironment.Create(
                new RangeFeed(
                    Avalonia,
                    AvaloniaVersion,
                    archive));
        var implementation = Assert.IsType<
            PackageLibraryRealizationResult.Realized>(
            await implementationEnvironment.RealizeLibraryAsync(
                new InMemoryPackageStore(),
                "net10.0",
                new PackageLibrarySelector(
                    "Avalonia.Dialogs.dll"),
                PackageLibraryRealizationDepth.Implementation,
                packageId: Avalonia,
                version: AvaloniaVersion));
        var implementationContent =
            Assert.IsType<RangedPackageContent>(
                implementation.Acquired.Payload.Content);
        Assert.Contains(
            AvaloniaImplementation + "Avalonia.Dialogs.dll",
            implementationContent.MaterializedEntries);
        Assert.True(
            implementationContent.MaterializedEntries.Count(
                path => path.StartsWith(
                    AvaloniaImplementation,
                    StringComparison.Ordinal))
            < selectionImplementationEntries);
        Assert.NotNull(implementation.Handoff.ImplementationAsset);
    }

    [Fact]
    public async Task
        PackageLibraryRealization_QueryAmbiguityIsTyped()
    {
        const string Id = "Ambiguous.Library";
        const string Version = "1.0.0";
        byte[] archive = CreateArchive(
            Id,
            Version,
            "ref/net8.0/a/Same.dll",
            "ref/net8.0/b/Same.dll");
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(Id, Version, archive));

        PackageLibraryRealizationResult result =
            await environment.RealizeLibraryAsync(
                new InMemoryPackageStore(),
                "net8.0",
                new PackageLibrarySelector("Same.dll"),
                PackageLibraryRealizationDepth.Selection,
                packageId: Id,
                version: Version);

        Assert.Equal(
            PackageLibraryRealizationStatus.Ambiguous,
            result.Status);
        Assert.IsType<PackageHouseResult.Settled>(
            result.Settlement.Result);
    }

    private static byte[] CreateArchive(
        string packageId,
        string version,
        params string[] paths)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Add(
                $"{packageId}.nuspec",
                Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\"?><package><metadata>"
                    + $"<id>{packageId}</id>"
                    + $"<version>{version}</version>"
                    + "<authors>test</authors>"
                    + "<description>test</description>"
                    + "</metadata></package>"));
            foreach (string path in paths)
                Add(path, [1]);

            void Add(string path, byte[] content)
            {
                using Stream entry = archive.CreateEntry(
                    path,
                    CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }
        }
        return output.ToArray();
    }
}
