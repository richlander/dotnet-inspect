using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PlatformTypeCatalogRoutingTests
{
    [Fact]
    public async Task
        VersionlessInstalledCatalogRoutesAfterAuthoritiesRetire()
    {
        string dotnetRoot = Assert.IsType<string>(
            PlatformTypeCatalogRouting.FindActiveDotnetRoot());
        int packageCompositionCount = 0;
        using var client = new HttpClient();
        var context = new CommandContext(
            verbose: false,
            client,
            () =>
            {
                packageCompositionCount++;
                throw new InvalidOperationException(
                    "Installed routing must not activate Package Source.");
            });

        CliPlatformTypeCatalogOutcome.Completed completed =
            Assert.IsType<CliPlatformTypeCatalogOutcome.Completed>(
                await PlatformTypeCatalogRouting.LoadAsync(
                    dotnetRoot,
                    context,
                    new NuGetSourceOptions(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, packageCompositionCount);
        Assert.True(
            PlatformVersion.SemanticPrecedenceComparer.Compare(
                completed.Catalog.Target.Version,
                PlatformVersion.Parse("10.0.1")) >= 0);
        Assert.All(
            completed.Catalog.PopulationReceipt.HouseReceipt
                .SourceSettlements,
            settlement => Assert.Contains(
                "installed",
                settlement.Contribution.Capability.Name,
                StringComparison.Ordinal));

        var type = Assert.IsType<
            CliPlatformTypeRouteOutcome.Resolved>(
                PlatformTypeCatalogRouting.Resolve(
                    completed.Catalog,
                    "System.Text.Json.JsonSerializer",
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            type.TypeName);
        Assert.Equal("System.Text.Json", type.AssemblyName);
        Assert.Null(type.MemberSelector);
        Assert.True(type.InputWasFullName);
        Assert.Equal(
            $"runtime@{completed.Catalog.Target.Version.Value}",
            type.Framework);

        var member = Assert.IsType<
            CliPlatformTypeRouteOutcome.Resolved>(
                PlatformTypeCatalogRouting.Resolve(
                    completed.Catalog,
                    "System.Collections.Generic.List<T>.Add",
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            "System.Collections.Generic.List<T>",
            member.TypeName);
        Assert.Equal("System.Collections", member.AssemblyName);
        Assert.Equal("Add", member.MemberSelector);

        Assert.IsType<CliPlatformTypeRouteOutcome.Missing>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                "No.Such.Platform.Type",
                TestContext.Current.CancellationToken));
        Assert.IsType<CliPlatformTypeRouteOutcome.Rejected>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                " ",
                TestContext.Current.CancellationToken));
        Assert.IsType<CliPlatformTypeRouteOutcome.Ambiguous>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                "Timer",
                TestContext.Current.CancellationToken));
    }
}
