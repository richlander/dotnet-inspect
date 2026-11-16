using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PlatformTypeLocatorRoutingTests
{
    [Fact]
    public async Task
        PackageBackedPopulationAnswersWholeLibraryWithoutInstalledRoot()
    {
        // Owner: docs/cli-architecture.md#bare-target-domain-selection - a Package-backed
        // Platform source remains Platform realization, not a Package subject fallback.
        // No dotnet root: the selected Runtime population is realized only from
        // a local feed holding the restored Microsoft.NETCore.App.Ref package
        // for the versionless package-backed default (net10.0, at least 10.0.1).
        const string version = "10.0.10";
        string nupkg = RestoredReferencePack(version);
        NuGetCache.Initialize("dotnet-inspect");
        using var feed = new TemporaryTestDirectory(
            "inspect-cli-platform-ref-feed");
        File.Copy(nupkg, Path.Combine(feed.FullName, Path.GetFileName(nupkg)));
        int packageCompositionCount = 0;
        using var client = new HttpClient();
        var context = new CommandContext(
            verbose: false,
            client,
            () =>
            {
                packageCompositionCount++;
                return new DesktopPackageSourceComposition(
                    TimeSpan.FromSeconds(30));
            });

        CliPlatformTypeLocatorOutcome.Completed completed =
            Assert.IsType<CliPlatformTypeLocatorOutcome.Completed>(
                await PlatformTypeLocatorRouting.LocateAsync(
                    dotnetRoot: null,
                    RouterTargetAnalysis.SelectPlatformPopulation(
                        "System.Text.Json"),
                    context,
                    new NuGetSourceOptions { Sources = [feed.FullName] },
                    "System.Text.Json",
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, packageCompositionCount);
        Assert.Equal(PlatformFamily.DotNetRuntime, completed.Target.Family);
        Assert.Equal(version, completed.Target.Version);
        Assert.All(
            completed.HouseReceipt.SourceSettlements,
            settlement => Assert.DoesNotContain(
                "installed",
                settlement.Contribution.Capability.Name,
                StringComparison.Ordinal));
        Assert.True(
            PlatformTypeLocatorRouting.ContainsFocusLibrary(
                completed, "System.Text.Json"));
        Assert.False(
            PlatformTypeLocatorRouting.ContainsFocusLibrary(
                completed, "System.Text.Json.JsonSerializer"));
        Assert.False(
            PlatformTypeLocatorRouting.ContainsFocusLibrary(
                completed, "Microsoft.Extensions.Logging"));
    }

    [Fact]
    public async Task
        RouterLibraryHandoffUsesExactVersionThroughSelectedLocalFeed()
    {
        // Owner: docs/cli-architecture.md#bare-target-domain-selection - the Router's
        // completed Library selection crosses as a typed fact; a Package-backed
        // Platform source remains Platform realization.
        // The only installed runtime is a 10.0.10 preview whose System.Text.Json
        // is an 11.x assembly, so a prefix match would be visible. The exact
        // 10.0.10 reference pack is available only from a local folder feed.
        const string version = "10.0.10";
        string nupkg = RestoredReferencePack(version);
        NuGetCache.Initialize("dotnet-inspect");
        using var root = new TemporaryTestDirectory("inspect-cli-preview-only-root");
        string preview = Path.Combine(
            root.FullName, "shared", "Microsoft.NETCore.App", "10.0.10-preview.1");
        Directory.CreateDirectory(preview);
        string planted = Path.Combine(preview, "System.Text.Json.dll");
        File.Copy(
            typeof(System.Text.Json.JsonSerializer).Assembly.Location,
            planted);
        using var feed = new TemporaryTestDirectory("inspect-cli-platform-ref-feed");
        File.Copy(nupkg, Path.Combine(feed.FullName, Path.GetFileName(nupkg)));
        string[] args =
        [
            "library", "System.Text.Json",
            "--framework", $"runtime@{version}",
            "--source", feed.FullName,
            "--json",
        ];

        string? originalRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root.FullName);

            // A user-authored partial selector keeps its prefix behavior.
            Assert.Equal(
                planted,
                PlatformResolver.ResolveAssembly(
                    "System.Text.Json",
                    $"runtime@{version}",
                    useRuntimeAssemblies: true).AssemblyPath);
            // The exact lookup never accepts the preview.
            Assert.Equal(
                (null, null, null, null),
                PlatformResolver.ResolveExactRuntimeAssembly(
                    "System.Text.Json", "runtime", version));

            var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            {
                var command = CommandLineBuilder.CreateRootCommand();
                string[] tokens = CommandLineBuilder.PreprocessArgs(args, command);
                return CommandLineBuilder.InvokeWithLineWindowAsync(
                    command.Parse(tokens),
                    tokens,
                    resolvedTarget: null,
                    new RouterResolvedLibrary(
                        "System.Text.Json",
                        PlatformFamily.DotNetRuntime,
                        version));
            });

            Assert.True(exit == 0, error);
            Assert.Contains(
                "System.Text.Json, Version=10.0.0.0",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "System.Text.Json, Version=11.",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalRoot);
        }
    }

    [Fact]
    public async Task
        RouterNamespaceHandoffUsesExactVersionThroughSelectedLocalFeed()
    {
        // Owner: docs/cli-architecture.md#bare-target-domain-selection - the namespace
        // route's namesake Library crosses as the same typed exact selection.
        // The only installed runtime is a 10.0.10 preview whose System.Text.Json
        // is an 11.x assembly without System.Text.Json.Nodes, so a prefix match
        // cannot produce the listing; only the exact 10.0.10 reference pack
        // (System.Text.Json 10.0.0.0) in the local feed can.
        const string version = "10.0.10";
        string nupkg = RestoredReferencePack(version);
        NuGetCache.Initialize("dotnet-inspect");
        using var root = new TemporaryTestDirectory("inspect-cli-preview-only-root");
        string preview = Path.Combine(
            root.FullName, "shared", "Microsoft.NETCore.App", "10.0.10-preview.1");
        Directory.CreateDirectory(preview);
        string planted = Path.Combine(preview, "System.Text.Json.dll");
        File.Copy(
            Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll"),
            planted);
        Assert.Equal(
            11,
            System.Reflection.AssemblyName.GetAssemblyName(planted).Version!.Major);
        using var feed = new TemporaryTestDirectory("inspect-cli-platform-ref-feed");
        File.Copy(nupkg, Path.Combine(feed.FullName, Path.GetFileName(nupkg)));
        string[] args =
        [
            "library", "System.Text.Json",
            "--namespace", "System.Text.Json.Nodes",
            "--framework", $"runtime@{version}",
            "--source", feed.FullName,
        ];

        string? originalRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root.FullName);
            Assert.Equal(
                planted,
                PlatformResolver.ResolveAssembly(
                    "System.Text.Json",
                    $"runtime@{version}",
                    useRuntimeAssemblies: true).AssemblyPath);

            var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
            {
                var command = CommandLineBuilder.CreateRootCommand();
                string[] tokens = CommandLineBuilder.PreprocessArgs(args, command);
                return CommandLineBuilder.InvokeWithLineWindowAsync(
                    command.Parse(tokens),
                    tokens,
                    resolvedTarget: null,
                    new RouterResolvedLibrary(
                        "System.Text.Json",
                        PlatformFamily.DotNetRuntime,
                        version));
            });

            Assert.True(exit == 0, error);
            Assert.StartsWith("# System.Text.Json", output, StringComparison.Ordinal);
            Assert.Contains(
                "| `System.Text.Json.Nodes.JsonNode` |",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "| `System.Text.Json.Nodes.JsonObject` |",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", originalRoot);
        }
    }

    internal static string RestoredReferencePack(string version)
    {
        string globalPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        string nupkg = Path.Combine(
            globalPackages,
            "microsoft.netcore.app.ref",
            version,
            $"microsoft.netcore.app.ref.{version}.nupkg");
        Assert.True(File.Exists(nupkg), nupkg);
        return nupkg;
    }

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
        InstalledPopulationDoesNotAddCompatibilitySuffixProbes()
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
                    "System.Numerics.Enumerator.X",
                    TestContext.Current.CancellationToken));

        var evaluated =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                completed.Envelope.Content);
        Assert.Equal(
            completed.TypeRequests.Length + 1,
            evaluated.Answers.Length);
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
            evaluated.Answers[completed.NamespaceAnswerIndex].Candidates
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
