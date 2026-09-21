using System.Collections.Concurrent;
using System.Globalization;

using DotnetInspect.Cli.Views;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task PackageBaseInventory_ConfiguredFeedPreservesUnboundedVulnerabilityRows()
    {
        const string id = "Vulnerable.Package";
        const int vulnerabilityCount = 31;
        byte[] archive = CreatePackage(id, "Vulnerability growth fixture");
        string entries = string.Join(
            ",",
            Enumerable.Range(0, vulnerabilityCount).Select(index => $$"""
                {"versions":"[1.0.0]","severity":2,"url":"https://advisories.invalid/{{index}}"}
                """));
        string vulnerabilityPage = $$"""
            {"{{id.ToLowerInvariant()}}":[{{entries}}]}
            """;
        PayloadFeedHandler CreateHandler(string source) =>
            new(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>(),
                vulnerabilityPage: () => new StringContent(vulnerabilityPage));
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => CreateHandler(FirstFeed));
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            CreateHandler);

        var result = await RunCommandAsync(
            [
                "package",
                $"{id}@{Version}",
                "--source",
                FirstFeed,
                "-S",
                PackageSections.Vulnerabilities,
                "--count",
                "--tips",
                "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(
            vulnerabilityCount.ToString(CultureInfo.InvariantCulture),
            result.Output.Trim());
    }

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
