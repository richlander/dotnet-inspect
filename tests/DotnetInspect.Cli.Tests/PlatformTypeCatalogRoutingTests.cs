using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PlatformTypeCatalogRoutingTests
{
    [Fact]
    public async Task
        VersionlessFallbackUsesConfiguredPackageSources()
    {
        using var feed = new TemporaryTestDirectory(
            "inspect-cli-platform-empty-feed");
        int packageCompositionCount = 0;
        using var client = new HttpClient();
        var context = new CommandContext(
            verbose: false,
            client,
            () =>
            {
                packageCompositionCount++;
                return new DesktopPackageSourceComposition(
                    TimeSpan.FromSeconds(5));
            });

        CliPlatformTypeCatalogOutcome.NotCompleted notCompleted =
            Assert.IsType<CliPlatformTypeCatalogOutcome.NotCompleted>(
                await PlatformTypeCatalogRouting.LoadAsync(
                    dotnetRoot: null,
                    context,
                    new NuGetSourceOptions
                    {
                        Sources = [feed.FullName],
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, packageCompositionCount);
        Assert.Equal(
            CliPlatformTypeCatalogFailureKind.HouseUnavailable,
            notCompleted.Kind);
        Assert.NotNull(notCompleted.HouseReceipt);
        Assert.Contains(
            notCompleted.HouseReceipt.SourceSettlements,
            settlement => settlement.Contribution.Capability.Name.Contains(
                "package",
                StringComparison.Ordinal));
    }

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

        InspectionEnvelope<PlatformTypeCatalogRouteOutcome> typeEnvelope =
                PlatformTypeCatalogRouting.Resolve(
                    completed.Catalog,
                    "System.Text.Json.JsonSerializer",
                    TestContext.Current.CancellationToken);
        var type = Assert.IsType<
            PlatformTypeCatalogRouteOutcome.Resolved>(
                typeEnvelope.Content);
        Assert.Equal(
            "System.Text.Json",
            type.Candidate.Type.Namespace);
        Assert.Equal(["JsonSerializer"], type.Candidate.Type.Segments);
        Assert.Equal(
            "System.Text.Json",
            type.Candidate.Assembly.Identity.Name);
        Assert.Null(type.Request.MemberSelector);
        Assert.Equal(
            PlatformTypeCatalogRouteTargetKind.Type,
            type.Request.TargetKind);
        Assert.Equal(
            completed.Catalog.Target.Version.Value,
            type.Target.Version);

        InspectionEnvelope<PlatformTypeCatalogRouteOutcome> memberEnvelope =
                PlatformTypeCatalogRouting.Resolve(
                    completed.Catalog,
                    "System.Collections.Generic.List<T>.Add",
                    TestContext.Current.CancellationToken);
        var member = Assert.IsType<
            PlatformTypeCatalogRouteOutcome.Resolved>(
                memberEnvelope.Content);
        Assert.Equal(
            "System.Collections.Generic",
            member.Candidate.Type.Namespace);
        Assert.Equal(["List`1"], member.Candidate.Type.Segments);
        Assert.Equal(
            "System.Collections",
            member.Candidate.Assembly.Identity.Name);
        Assert.Equal("Add", member.Request.MemberSelector);
        Assert.Equal(
            PlatformTypeCatalogRouteTargetKind.Member,
            member.Request.TargetKind);

        Assert.IsType<PlatformTypeCatalogRouteOutcome.Missing>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                "No.Such.Platform.Type",
                TestContext.Current.CancellationToken).Content);
        Assert.IsType<PlatformTypeCatalogRouteOutcome.Rejected>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                " ",
                TestContext.Current.CancellationToken).Content);
        Assert.IsType<PlatformTypeCatalogRouteOutcome.Ambiguous>(
            PlatformTypeCatalogRouting.Resolve(
                completed.Catalog,
                "Timer",
                TestContext.Current.CancellationToken).Content);
    }
}
