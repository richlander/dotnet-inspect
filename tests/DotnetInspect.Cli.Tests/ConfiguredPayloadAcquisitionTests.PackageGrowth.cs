using System.Collections.Concurrent;
using System.Globalization;

using DotnetInspect.Cli.Views;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task PackageBaseInventory_RealPackagePreservesMeasuredEcosystemDependencyRows()
    {
        const string id = "Microsoft.AspNetCore.App";
        const string version = "2.2.8";
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            "microsoft.aspnetcore.app.2.2.8.nupkg");
        byte[] archive = await File.ReadAllBytesAsync(
            packagePath,
            TestContext.Current.CancellationToken);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>(),
                version: version));

        var result = await RunCommandAsync(
            [
                "package",
                $"{id}@{version}",
                "--source",
                FirstFeed,
                "-S",
                PackageSections.EcosystemDependencies,
                "--count",
                "--tips",
                "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(
            139.ToString(CultureInfo.InvariantCulture),
            result.Output.Trim());
    }
}
