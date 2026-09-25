using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PlatformTypeLocatorRoutingTests
{
    [Fact]
    public async Task
        InstalledPopulationRoutesExactTypeWithAssemblyIdentityAndMvid()
    {
        string dotnetRoot = Assert.IsType<string>(
            PlatformTypeCatalogRouting.FindActiveDotnetRoot());
        using var client = new HttpClient();
        var context = new CommandContext(
            verbose: false,
            client,
            () => throw new InvalidOperationException(
                "Installed routing must not activate Package Source."));

        CliPlatformTypeLocatorOutcome.Completed completed =
            Assert.IsType<CliPlatformTypeLocatorOutcome.Completed>(
                await PlatformTypeLocatorRouting.LocateAsync(
                    dotnetRoot,
                    context,
                    new NuGetSourceOptions(),
                    "System.Text.Json.JsonSerializer",
                    TestContext.Current.CancellationToken));

        var resolved =
            Assert.IsType<PlatformTypeCatalogRouteOutcome.Resolved>(
                PlatformTypeLocatorRouting.ResolveType(
                    completed,
                    TestContext.Current.CancellationToken).Content);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            resolved.Candidate.Type.ToMetadataFullName());
        Assert.Equal(
            "System.Text.Json",
            resolved.Candidate.Assembly.Identity.Name);
        Assert.NotEqual(
            Guid.Empty,
            resolved.Candidate.Assembly.ModuleVersionId);
        Assert.Null(resolved.Request.MemberSelector);
        Assert.Equal(
            PlatformTypeCatalogRouteTargetKind.Type,
            resolved.Request.TargetKind);
    }

    [Fact]
    public async Task
        InstalledPopulationRoutesMemberAfterWorkspaceRetiresAuthorities()
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

        CliPlatformTypeLocatorOutcome.Completed completed =
            Assert.IsType<CliPlatformTypeLocatorOutcome.Completed>(
                await PlatformTypeLocatorRouting.LocateAsync(
                    dotnetRoot,
                    context,
                    new NuGetSourceOptions(),
                    "System.Collections.Generic.List<T>.Add",
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, packageCompositionCount);
        Assert.True(
            PlatformVersion.SemanticPrecedenceComparer.Compare(
                PlatformVersion.Parse(completed.Target.Version),
                PlatformVersion.Parse("10.0.1")) >= 0);
        Assert.All(
            completed.HouseReceipt.SourceSettlements,
            settlement => Assert.Contains(
                "installed",
                settlement.Contribution.Capability.Name,
                StringComparison.Ordinal));
        var evaluated =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                completed.Envelope.Content);
        Assert.True(evaluated.IsSuccess);
        Assert.All(evaluated.Members, member => Assert.True(member.IsComplete));

        InspectionEnvelope<PlatformTypeCatalogRouteOutcome> route =
            PlatformTypeLocatorRouting.ResolveType(
                completed,
                TestContext.Current.CancellationToken);
        var member =
            Assert.IsType<PlatformTypeCatalogRouteOutcome.Resolved>(
                route.Content);
        Assert.Equal(
            "System.Collections.Generic",
            member.Candidate.Type.Namespace);
        Assert.Equal(["List`1"], member.Candidate.Type.Segments);
        Assert.Equal(
            "System.Collections",
            member.Candidate.Assembly.Identity.Name);
        Assert.NotEqual(Guid.Empty, member.Candidate.Assembly.ModuleVersionId);
        Assert.Equal("Add", member.Request.MemberSelector);
        Assert.Equal(
            PlatformTypeCatalogRouteTargetKind.Member,
            member.Request.TargetKind);
    }

    [Fact]
    public async Task
        InstalledPopulationRoutesExactNamespaceThroughLocatorEnvelope()
    {
        string dotnetRoot = Assert.IsType<string>(
            PlatformTypeCatalogRouting.FindActiveDotnetRoot());
        using var client = new HttpClient();
        var context = new CommandContext(
            verbose: false,
            client,
            () => throw new InvalidOperationException(
                "Installed routing must not activate Package Source."));

        CliPlatformTypeLocatorOutcome.Completed completed =
            Assert.IsType<CliPlatformTypeLocatorOutcome.Completed>(
                await PlatformTypeLocatorRouting.LocateAsync(
                    dotnetRoot,
                    context,
                    new NuGetSourceOptions(),
                    "System.Text.Json.Nodes",
                    TestContext.Current.CancellationToken));

        Assert.IsType<PlatformTypeCatalogRouteOutcome.Missing>(
            PlatformTypeLocatorRouting.ResolveType(
                completed,
                TestContext.Current.CancellationToken).Content);
        var found =
            Assert.IsType<PlatformNamespaceDiscoveryOutcome.Found>(
                PlatformTypeLocatorRouting.ResolveNamespace(
                    completed,
                    "System.Text.Json.Nodes",
                    TestContext.Current.CancellationToken).Content);
        PlatformNamespaceDiscoveryHit hit = Assert.Single(found.Hits);
        Assert.Equal("System.Text.Json", hit.Library);
        Assert.Equal("System.Text.Json.Nodes", hit.Namespace);
        Assert.Contains(
            hit.Declarations,
            declaration =>
                declaration.Type.Namespace == "System.Text.Json.Nodes"
                && declaration.Type.Segments.SequenceEqual(["JsonArray"]));

        var evaluated =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                completed.Envelope.Content);
        var namesakeLibraries =
            LibraryNamespaceDiscovery.NamesakeLibraryCandidates(
                "System.Text.Json.Nodes");
        var ranks = namesakeLibraries
            .Select((library, rank) => (library, rank))
            .ToDictionary(
                static item => item.library,
                static item => item.rank,
                StringComparer.OrdinalIgnoreCase);
        var expectedHitOrder =
            evaluated.Answers[^1].Candidates
                .Where(
                    candidate =>
                        candidate.IsPublicSurface
                        && ranks.ContainsKey(
                            candidate.Observation.AssemblyIdentity.Name))
                .GroupBy(
                    static candidate => (
                        candidate.Observation.ContextOrder,
                        candidate.Observation.MemberOrder))
                .Select(static group => group.First())
                .OrderBy(
                    candidate =>
                        ranks[
                            candidate.Observation.AssemblyIdentity.Name])
                .ThenBy(
                    static candidate =>
                        candidate.Observation.ContextOrder)
                .ThenBy(
                    static candidate =>
                        candidate.Observation.MemberOrder)
                .Select(
                    static candidate =>
                        candidate.Observation.AssemblyIdentity.Name);
        Assert.Equal(
            expectedHitOrder,
            found.Hits.Select(static candidate => candidate.Library));
    }
}
