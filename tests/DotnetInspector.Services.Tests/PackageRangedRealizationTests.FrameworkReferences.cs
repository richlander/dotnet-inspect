using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task FrameworkReferences_RangedHouseReadIncludesOnlyDemandedManifest()
    {
        const string PackageId = "Microsoft.Azure.SignalR";
        const string Version = "1.33.1";
        byte[] archive = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkReferences",
            "microsoft.azure.signalr.1.33.1.nupkg"));
        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net8.0",
                sizeCut: 0,
                PackageId,
                Version,
                evidenceDemand:
                    PackageHouseEvidenceDemand.FrameworkReferences));

        Assert.Equal(PackagePayloadOrigin.Ranged, acquired.Payload.Origin);
        var content =
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Contains(
            "Microsoft.Azure.SignalR.nuspec",
            content.MaterializedEntries);
        Assert.Contains(
            "lib/net8.0/Microsoft.Azure.SignalR.dll",
            content.MaterializedEntries);
        Assert.DoesNotContain(
            content.MaterializedEntries,
            path => path.StartsWith(
                "lib/netstandard2.0/",
                StringComparison.Ordinal));
        using (var oracle = new ZipArchive(new MemoryStream(archive)))
        {
            Assert.True(
                content.MaterializedEntries.Count
                < oracle.Entries.Count);
        }

        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));
        Assert.Same(acquired.Result, selected.Association.Result);
        Assert.Same(
            acquired.Result.Evidence.Acquisition,
            selected.Association.Acquisition);
        Assert.Same(
            acquired.Result.Evidence.Realization,
            selected.Association.Realization);
        Assert.Same(
            content.GenerationIdentity,
            selected.Association.Generation);
        Assert.Equal("net8.0", selected.Evidence.SelectedTargetFramework);
        Assert.Equal(
            "Microsoft.AspNetCore.App",
            Assert.Single(selected.Evidence.References).Name);
        Assert.Equal(PackageTransferPath.Ranged, Transfer(
            acquired,
            PackagePayloadOrigin.Ranged).Path);
    }

    [Fact]
    public async Task MicrosoftAzureSignalR_HouseFrameworkReferencesFollowTargetContext()
    {
        const string PackageId = "Microsoft.Azure.SignalR";
        const string Version = "1.33.1";
        byte[] archive = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkReferences",
            "microsoft.azure.signalr.1.33.1.nupkg"));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(PackageId, Version, archive));

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "netstandard2.0",
                sizeCut: 0,
                PackageId,
                Version,
                evidenceDemand:
                    PackageHouseEvidenceDemand.FrameworkReferences));

        var selected =
            Assert.IsType<PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(acquired));
        Assert.Equal(
            "netstandard2.0",
            selected.Evidence.SelectedTargetFramework);
        Assert.Empty(selected.Evidence.Occurrences);
        Assert.Empty(selected.Evidence.References);
    }
}
