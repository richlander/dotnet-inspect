using System.IO.Compression;
using System.Net;
using System.CommandLine;
using System.Text.Json;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactPackageWorkspaceRouteTests
{
    const string SelectedPackage = "selected.package";
    const string FocusedPackage = "focused.package";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string CompatibleAssetFramework = "net10.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source = new("test", SourceUrl);

    [Fact]
    public async Task LibraryHelpAdvertisesWorkspaceAndNamesakeNarrowing()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var result = await ConsoleCapture.RunAsync(
            () => Task.FromResult(
                root.Parse(["library", "--help"]).InvokeAsync().Result));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "--namesake-library",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task LibraryWorkspaceRequiresExplicitPackageSubject()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] arguments =
        [
            "library",
            "--workspace",
            "packet",
        ];

        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(arguments),
                arguments));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--workspace on library requires --package",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryWorkspaceRouteDefaultsToPackageAggregate()
    {
        var store = await LibraryStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = SelectedPackage,
            Select = ["Library Info"],
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"lib/{Framework}/{SelectedPackage}.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"lib/{Framework}/support.library.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "## Libraries",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "tool.library.dll",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryWorkspaceAggregateIntegrationsReuseRetainedPackageRoot()
    {
        var store = await LibraryStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = SelectedPackage,
            Select = ["Integrations"],
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "matched sections have no data across all libraries",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryWorkspaceRouteNarrowsToExactLibrary()
    {
        var store = await LibraryStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = SelectedPackage,
            AssemblyName = "support.library.dll",
            Select = ["Library Info"],
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"# support.library.dll ({Framework})",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"# {SelectedPackage}.dll ({Framework})",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryWorkspaceExactDiscoveryPreservesDetails()
    {
        var store = await LibraryStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = SelectedPackage,
            AssemblyName = "support.library.dll",
            Discover = ["Library Info"],
            DiscoverDetails = true,
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "| Name | Kind | Formats |",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryWorkspaceRouteNarrowsToNamesakeLibrary()
    {
        const string namesakePackage =
            "ILInspector.Metadata";
        var store = await LibraryStoreAsync(namesakePackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(namesakePackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = namesakePackage,
            NamesakeLibrary = true,
            Select = ["Library Info"],
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"# {namesakePackage}.dll ({Framework})",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Name | ILInspector.Metadata |",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"lib/{Framework}/support.library.dll",
            result.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryWorkspaceCompatibleAssetSupportsNarrowing(
        bool namesake)
    {
        const string packageId = "ILInspector.Metadata";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            packageId,
            Archive(
                ($"lib/{CompatibleAssetFramework}/{packageId}.dll", assembly),
                ($"{packageId}.nuspec", Nuspec(packageId))));
        string packet = EncodePacket(
            format: 4,
            tabs: [(packageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new LibraryOptions
        {
            WorkspacePacket = packet,
            PackagePath = packageId,
            AssemblyName = namesake ? null : $"{packageId}.dll",
            NamesakeLibrary = namesake,
            Select = ["Library Info"],
            Verbosity = Verbosity.Minimal,
        };

        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(
                options,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"# {packageId}.dll ({CompatibleAssetFramework})",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageHelpAdvertisesWorkspaceAndShare()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var result = await ConsoleCapture.RunAsync(
            () => Task.FromResult(
                root.Parse(["package", "--help"]).InvokeAsync().Result));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace", result.Output, StringComparison.Ordinal);
        Assert.Contains("--share", result.Output, StringComparison.Ordinal);
#if DEBUG
        Assert.Contains(
            "--evidence-envelope",
            result.Output,
            StringComparison.Ordinal);
#else
        Assert.DoesNotContain(
            "--evidence-envelope",
            result.Output,
            StringComparison.Ordinal);
#endif
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task WorkspaceRouteUsesSelectedContextAndDerivesPackageShare(
        int format)
    {
        var store = await StoreAsync(
            SelectedPackage,
            FocusedPackage);
        string packet = EncodePacket(
            format,
            tabs:
            [
                (SelectedPackage, Version, Framework),
                (FocusedPackage, Version, Framework),
            ],
            contexts: [[0, 1]],
            focusedTab: 1,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var ordinaryOptions = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };
        InspectionOptions options = ordinaryOptions with
        {
            ShareFormat = WorkspaceShareFormat.Packet,
        };

        var ordinary = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                ordinaryOptions,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));
        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    options,
                    new CommandContext(verbose: false),
                    LoadOptions(client, store)));

        Assert.True(exitCode == 0, error);
        Assert.Equal(0, ordinary.ExitCode);
        Assert.Empty(ordinary.Error);
        Assert.Equal(ordinary.Output, output);
        Assert.Contains(
            $"# {SelectedPackage}",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FocusedPackage,
            output,
            StringComparison.Ordinal);
        string derivedPacket = Assert.Single(
            error.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        WorkspaceSharePacket derived =
            WorkspaceSharePacketCodec.Decode(
                derivedPacket,
                TestContext.Current.CancellationToken);
        Assert.Equal(format, derived.FormatVersion);
        Assert.Equal(0, derived.FocusedTabIndex);
        Assert.Equal(0, derived.SelectedContextIndex);
        Assert.Single(derived.Contexts);
        Assert.Equal([0, 1], derived.Contexts[0].TabIndexes);
        Assert.Equal(FocusedPackage, derived.Tabs[1].Source);
        Assert.IsType<PortableSubjectRequest.Package>(
            derived.ViewStates[1].Subject);
        Assert.Equal(
            "package.overview",
            derived.ViewStates[1].Facet);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            derived.ViewStates[2].Subject);
    }

    [Fact]
    public async Task WorkspaceLayoutReusesRestorationIssuedContent()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            ListLayout = true,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"{SelectedPackage}.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task DependencyHierarchyUsesAdmittedRootAndTraversesChildren()
    {
        const string dependencyPackage = "dependency.package";
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            SelectedPackage,
            await PackageAsync(
                SelectedPackage,
                typeof(ApiType).Assembly.Location,
                dependencyPackage));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var requests = new DependencyRequestLog();
        using var client = new HttpClient(
            new DependencyHandler(
                SelectedPackage,
                dependencyPackage,
                requests));
        var context = new CommandContext(
            verbose: false,
            client,
            createPackageSourceComposition: () =>
                new DesktopPackageSourceComposition(
                    TimeSpan.FromSeconds(5),
                    new NoCredentials(),
                    (_, _) => new DependencyHandler(
                        SelectedPackage,
                        dependencyPackage,
                        requests)));
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            IncludeSections = [PackageSections.DependencyHierarchy],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                context,
                LoadOptions(client, store)));

        Assert.True(
            result.ExitCode == 0,
            result.Error
                + Environment.NewLine
                + string.Join(Environment.NewLine, requests.Requests));
        Assert.Contains(
            dependencyPackage,
            result.Output,
            StringComparison.Ordinal);
        Assert.True(
            requests.DependencyRequests > 0,
            "The child dependency must be traversed through the configured source.");
        Assert.Equal(0, requests.SelectedPackageRequests);
    }

    [Fact]
    public async Task DependencyHierarchySelfCycleReusesAdmittedRoot()
    {
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            SelectedPackage,
            await PackageAsync(
                SelectedPackage,
                typeof(ApiType).Assembly.Location,
                SelectedPackage));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var requests = new DependencyRequestLog();
        using var client = new HttpClient(
            new DependencyHandler(
                SelectedPackage,
                SelectedPackage,
                requests));
        var context = new CommandContext(
            verbose: false,
            client,
            createPackageSourceComposition: () =>
                new DesktopPackageSourceComposition(
                    TimeSpan.FromSeconds(5),
                    new NoCredentials(),
                    (_, _) => new DependencyHandler(
                        SelectedPackage,
                        SelectedPackage,
                        requests)));
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            IncludeSections = [PackageSections.DependencyHierarchy],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                context,
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(requests.Requests);
        Assert.Contains(
            SelectedPackage,
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibleAssetKeepsRequestedContextForDependencies()
    {
        const string requestedDependency = "child.requested";
        const string assetDependency = "child.asset";
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        await CommitAsync(
            store,
            SelectedPackage,
            Archive(
                ($"lib/{CompatibleAssetFramework}/{SelectedPackage}.dll",
                    assembly),
                ($"{SelectedPackage}.nuspec",
                    NuspecWithDependencyGroups(
                        SelectedPackage,
                        (Framework, requestedDependency),
                        (CompatibleAssetFramework, assetDependency)))));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var requests = new DependencyRequestLog();
        using var client = new HttpClient(
            new DependencyHandler(
                SelectedPackage,
                requestedDependency,
                requests));
        var context = new CommandContext(
            verbose: false,
            client,
            createPackageSourceComposition: () =>
                new DesktopPackageSourceComposition(
                    TimeSpan.FromSeconds(5),
                    new NoCredentials(),
                    (_, _) => new DependencyHandler(
                        SelectedPackage,
                        requestedDependency,
                        requests)));
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            IncludeSections = [PackageSections.DependencyHierarchy],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                context,
                LoadOptions(client, store)));

        Assert.True(
            result.ExitCode == 0,
            result.Error
                + Environment.NewLine
                + string.Join(Environment.NewLine, requests.Requests));
        Assert.Contains(
            requestedDependency,
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            assetDependency,
            result.Output,
            StringComparison.Ordinal);
        Assert.Equal(0, requests.SelectedPackageRequests);
    }

    [Fact]
    public async Task DependencyHierarchyUsesCaseInsensitiveRootManifest()
    {
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        await CommitAsync(
            store,
            SelectedPackage,
            Archive(
                ($"lib/{Framework}/{SelectedPackage}.dll", assembly),
                ("ROOT.NUSPEC", Nuspec(SelectedPackage))));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            IncludeSections = [PackageSections.DependencyHierarchy],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "## Dependency Hierarchy",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PackageInfoShareUsesOverviewFacet()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            ShareFormat = WorkspaceShareFormat.Packet,
            Select = [PackageSections.PackageInfo],
            SelectExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Package Info", result.Output);
        string derivedPacket = Assert.Single(
            result.Error.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        WorkspaceSharePacket derived =
            WorkspaceSharePacketCodec.Decode(
                derivedPacket,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            "package.overview",
            derived.ViewStates[1].Facet);
    }

    [Fact]
    public async Task PackageInfoUsesRestorationSelectedMeasurements()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            Select = [PackageSections.PackageInfo],
            SelectExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"| Selected TFM | {Framework} |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Selected-TFM Library Count | 1 |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"| TFMs | {Framework} |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EcosystemDependenciesUseAdmittedRoot()
    {
        const string dependencyPackage =
            "Microsoft.Extensions.AI.Abstractions";
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            SelectedPackage,
            await PackageAsync(
                SelectedPackage,
                typeof(ApiType).Assembly.Location,
                dependencyPackage));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions loadOptions = LoadOptions(client, store);
        var infoOptions = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            Select = [PackageSections.PackageInfo],
            SelectExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var info = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                infoOptions,
                new CommandContext(verbose: false),
                loadOptions));

        Assert.Equal(0, info.ExitCode);
        Assert.Contains("Microsoft.Extensions", info.Output);
        Assert.Contains("AI", info.Output);
        Assert.DoesNotContain(
            "| Ecosystem Dependency Status |",
            info.Output,
            StringComparison.Ordinal);
        Assert.Empty(info.Error);

        InspectionOptions detailOptions = infoOptions with
        {
            Select = [PackageSections.EcosystemDependencies],
        };
        var details = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                detailOptions,
                new CommandContext(verbose: false),
                loadOptions));

        Assert.Equal(0, details.ExitCode);
        Assert.Contains(dependencyPackage, details.Output);
        Assert.Contains("Microsoft.Extensions", details.Output);
        Assert.Contains("Package declaration", details.Output);
        Assert.Empty(details.Error);
    }

    [Fact]
    public async Task DeclaredToolPackageInfoUsesAdmittedToolSlice()
    {
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = Archive(
            ($"lib/{Framework}/{SelectedPackage}.dll", assembly),
            ($"tools/{Framework}/any/Alpha.dll", new byte[11]),
            ($"tools/{Framework}/any/Beta.dll", new byte[17]),
            (
                $"{SelectedPackage}.nuspec",
                Nuspec(SelectedPackage, declaredTool: true)));
        await CommitAsync(
            store,
            SelectedPackage,
            archive);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            Select = [PackageSections.PackageInfo],
            SelectExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Type | Tool |", result.Output);
        Assert.Contains(
            $"| Selected TFM | {Framework} |",
            result.Output,
            StringComparison.Ordinal);
        string folders = Assert.Single(
            result.Output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries),
            static line => line.StartsWith(
                "| Selected-TFM Folders |",
                StringComparison.Ordinal));
        Assert.Contains("tools", folders, StringComparison.Ordinal);
        Assert.Contains(
            "| Selected-TFM Size | 28 B |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Selected-TFM Library Count | 2 |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task LayoutShareRefusalPreservesLayoutOutput()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            ShareFormat = WorkspaceShareFormat.Packet,
            ListLayout = true,
            ListLayoutExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            $"{SelectedPackage}.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable at package/lens",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceLayoutPreservesExplicitToolsScope()
    {
        const string ToolFile = "Workspace.Tool.dll";
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        await CommitAsync(
            store,
            SelectedPackage,
            Archive(
                ($"lib/{Framework}/{SelectedPackage}.dll", assembly),
                ($"tools/{Framework}/{ToolFile}", assembly),
                ($"{SelectedPackage}.nuspec", Nuspec(SelectedPackage))));
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            ListLayout = true,
            ListLayoutExplicitlySet = true,
            ScopeTools = true,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(ToolFile, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"{SelectedPackage}.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task UnsupportedShareSectionPreservesOrdinaryOutput()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            ShareFormat = WorkspaceShareFormat.Packet,
            IncludeSections = [PackageSections.Files],
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            $"{SelectedPackage}.dll",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "section selection",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMatchIsDistinctAndDoesNotFallBackToFeed()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = ["missing.package"],
            WorkspacePacket = packet,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "is not a direct occurrence",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactVersionMismatchDoesNotFallBackToFeed()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [$"{SelectedPackage}@2.0.0"],
            WorkspacePacket = packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    options,
                    new CommandContext(verbose: false),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "but not at version '2.0.0'",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactSelectorOverFloatingMemberRefusesOnlyShare()
    {
        var store = await StoreAsync(SelectedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs: [(SelectedPackage, null, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new VersionListingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [$"{SelectedPackage}@{Version}"],
            WorkspacePacket = packet,
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    options,
                    new CommandContext(verbose: false),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            $"# {SelectedPackage}",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "floating Workspace Package member",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSelectedContextIsDistinct()
    {
        var store = await StoreAsync(SelectedPackage);
        WorkspaceSharePacket workspace =
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":4,"t":[],"g":[],"r":[["p","Test."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
                """,
                TestContext.Current.CancellationToken);
        string packet = WorkspaceSharePacketCodec.Encode(workspace);
        using var client = new HttpClient(new FailingHandler());
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    options,
                    new CommandContext(verbose: false),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "no selected context",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceRouteRejectsUrlInput()
    {
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = "https://dotnet-inspect.net/?w=packet",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(
                    options,
                    new CommandContext(verbose: false),
                    new WorkspaceContextLoadOptions
                    {
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = new InMemoryPackageStore(),
                        HttpClient = new HttpClient(new FailingHandler()),
                    }));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "URLs are not supported",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceDiscoveryIsRejectedBeforeRestoration()
    {
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = "not-restored",
            Discover = [],
            Schema = true,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(
                    new HttpClient(new FailingHandler()),
                    new InMemoryPackageStore())));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot combine",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "discovery",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("selected.package.nupkg")]
    [InlineData("https://example.test/selected.package.nupkg")]
    public async Task WorkspaceRouteRejectsAlternatePackageLocations(
        string package)
    {
        var options = new InspectionOptions
        {
            PackageArgs = [package],
            WorkspacePacket = "not-restored",
            TipLevel = TipLevel.Quiet,
        };
        using var client = new HttpClient(new FailingHandler());

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(client, new InMemoryPackageStore())));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "canonical NuGet Package ID",
            result.Error,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceSidecarRejectsPrimaryOutputAliasBeforeRestoration(
        bool equivalentSpelling)
    {
        using var directory =
            new TemporaryTestDirectory("package-workspace-output-alias-");
        string evidencePath =
            Path.Combine(directory.FullName, "output.json");
        string outputPath = equivalentSpelling
            ? Path.Combine(directory.FullName, ".", "output.json")
            : evidencePath;
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = "not-restored",
            OutputPath = outputPath,
            EvidenceEnvelopePath = evidencePath,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(
                    new HttpClient(new FailingHandler()),
                    new InMemoryPackageStore())));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--out and --evidence-envelope must name distinct files.",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(evidencePath));
    }

    [Fact]
    public async Task
        EvidenceSidecarRejectsWindowsExtendedPrimaryAliasBeforeRestoration()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory =
            new TemporaryTestDirectory("package-workspace-output-alias-");
        string outputPath =
            Path.Combine(directory.FullName, "output.json");
        string evidencePath = @"\\?\" + outputPath;
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = "not-restored",
            OutputPath = outputPath,
            EvidenceEnvelopePath = evidencePath,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(
                    new HttpClient(new FailingHandler()),
                    new InMemoryPackageStore())));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--out and --evidence-envelope must name distinct files.",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task EvidenceSidecarPreservesOutputAndPublishesRoutingFrame()
    {
        var store = await StoreAsync(
            SelectedPackage,
            FocusedPackage);
        string packet = EncodePacket(
            format: 4,
            tabs:
            [
                (SelectedPackage, Version, Framework),
                (FocusedPackage, Version, Framework),
            ],
            contexts: [[0, 1]],
            focusedTab: 1,
            selectedContext: 0);
        using var directory =
            new TemporaryTestDirectory("package-workspace-evidence-");
        string sidecar = Path.Combine(directory.FullName, "evidence.json");
        using var client = new HttpClient(new FailingHandler());
        var ordinaryOptions = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = packet,
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Quiet,
        };
        InspectionOptions evidenceOptions = ordinaryOptions with
        {
            EvidenceEnvelopePath = sidecar,
        };

        var ordinary = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                ordinaryOptions,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));
        var withEvidence = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                evidenceOptions,
                new CommandContext(verbose: false),
                LoadOptions(client, store)));

        Assert.Equal(0, ordinary.ExitCode);
        Assert.Equal(0, withEvidence.ExitCode);
        Assert.Equal(ordinary.Output, withEvidence.Output);
        Assert.Empty(ordinary.Error);
        Assert.Equal(
            $"Evidence envelope: {sidecar}{Environment.NewLine}",
            withEvidence.Error);

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                sidecar,
                TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "package-workspace",
            root.GetProperty("result_kind").GetString());
        Assert.True(
            root.GetProperty("content").TryGetProperty(
                "inspection",
                out _));
        Assert.True(root.TryGetProperty("portable_projection", out _));
        Assert.True(root.TryGetProperty("diagnostics", out _));
        JsonElement evidence = root.GetProperty("evidence");
        Assert.Equal(
            2,
            evidence.GetProperty(
                "considered_occurrence_count").GetInt32());
        JsonElement candidate = Assert.Single(
            evidence.GetProperty("exact_candidates").EnumerateArray());
        Assert.Equal(
            SelectedPackage,
            candidate.GetProperty("package_id").GetString());
        Assert.Equal(
            Version,
            candidate.GetProperty("declared_version").GetString());
        Assert.Equal(
            Version,
            candidate.GetProperty("effective_version").GetString());
        Assert.Equal(
            0,
            candidate.GetProperty("context_order").GetInt32());
        Assert.Equal(
            0,
            candidate.GetProperty("member_order").GetInt32());
        Assert.Equal(
            Framework,
            candidate.GetProperty("target_framework").GetString());
        Assert.True(
            JsonElement.DeepEquals(
                candidate,
                evidence.GetProperty("selected")));
    }

    [Fact]
    public async Task EvidenceSidecarRejectsNonInspectionLensBeforeRestoration()
    {
        var options = new InspectionOptions
        {
            PackageArgs = [SelectedPackage],
            WorkspacePacket = "not-restored",
            EvidenceEnvelopePath = Path.Combine(
                Path.GetTempPath(),
                "package-workspace-evidence.json"),
            ListLayout = true,
            TipLevel = TipLevel.Quiet,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                LoadOptions(
                    new HttpClient(new FailingHandler()),
                    new InMemoryPackageStore())));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires section-based Package inspection",
            result.Error,
            StringComparison.Ordinal);
    }
#endif

    static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    static async Task<InMemoryPackageStore> StoreAsync(
        params string[] packageIds)
    {
        var store = new InMemoryPackageStore();
        string[] assemblyPaths =
        [
            typeof(ApiType).Assembly.Location,
            typeof(PackageCommand).Assembly.Location,
        ];
        for (int index = 0; index < packageIds.Length; index++)
        {
            string packageId = packageIds[index];
            await CommitAsync(
                store,
                packageId,
                await PackageAsync(
                    packageId,
                    assemblyPaths[index % assemblyPaths.Length]));
        }

        return store;
    }

    static async Task<InMemoryPackageStore> LibraryStoreAsync(
        string packageId = SelectedPackage)
    {
        byte[] namesake = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] support = await File.ReadAllBytesAsync(
            typeof(PackageCommand).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            packageId,
            Archive(
                ($"lib/{Framework}/{packageId}.dll", namesake),
                ($"lib/{Framework}/support.library.dll", support),
                ($"tools/{Framework}/any/tool.library.dll", support),
                ($"{packageId}.nuspec", Nuspec(packageId))));
        return store;
    }

    static async Task<byte[]> PackageAsync(
        string packageId,
        string assemblyPath,
        string? dependencyPackage = null)
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            assemblyPath,
            TestContext.Current.CancellationToken);
        return Archive(
            ($"lib/{Framework}/{packageId}.dll", assembly),
            ($"{packageId}.nuspec", Nuspec(packageId, dependencyPackage)));
    }

    static async Task CommitAsync(
        InMemoryPackageStore store,
        string packageId,
        byte[] package) =>
        await store.CommitAsync(
            packageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);

    static string EncodePacket(
        int format,
        (string Package, string? Version, string Framework)[] tabs,
        int[][] contexts,
        int focusedTab,
        int? selectedContext)
    {
        string tabJson = string.Join(
            ',',
            tabs.Select(tab =>
                $"[\"{tab.Package}\","
                    + (tab.Version is null
                        ? "null"
                        : $"\"{tab.Version}\"")
                    + $",\"{tab.Framework}\",null]"));
        string contextJson = string.Join(
            ',',
            contexts.Select(context =>
                $"[{string.Join(',', context)}]"));
        string viewJson = string.Join(
            ',',
            Enumerable.Range(0, tabs.Length + 1).Select(index =>
                index == 0
                    ? """{"t":null,"u":{"k":"workspace"}}"""
                    : "{\"t\":" + (index - 1)
                        + ",\"u\":{\"k\":\"workspace\"}}"));
        string json =
            "{\"f\":" + format
                + ",\"t\":[" + tabJson
                + "],\"g\":[" + contextJson
                + "],\"r\":[],\"a\":" + focusedTab
                + ",\"x\":"
                + (selectedContext?.ToString() ?? "null")
                + ",\"v\":[" + viewJson + "]}";
        return WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken));
    }

    static byte[] Nuspec(
        string packageId,
        string? dependencyPackage = null,
        bool declaredTool = false) =>
        System.Text.Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>PACKAGE_ID</id>
                <version>1.0.0</version>
                <authors>dotnet-inspect tests</authors>
                <description>Workspace Package route fixture.</description>
                PACKAGE_TYPES
                DEPENDENCIES
              </metadata>
            </package>
            """.Replace(
                "PACKAGE_ID",
                packageId,
                StringComparison.Ordinal)
            .Replace(
                "DEPENDENCIES",
                dependencyPackage is null
                    ? ""
                    : $"""
                      <dependencies>
                        <group targetFramework="{Framework}">
                          <dependency id="{dependencyPackage}" version="[{Version}]" />
                        </group>
                      </dependencies>
                      """,
                StringComparison.Ordinal)
            .Replace(
                "PACKAGE_TYPES",
                declaredTool
                    ? """
                      <packageTypes>
                        <packageType name="DotnetTool" />
                      </packageTypes>
                      """
                    : "",
                StringComparison.Ordinal));

    static byte[] NuspecWithDependencyGroups(
        string packageId,
        params (string Framework, string Dependency)[] groups)
    {
        string dependencies = string.Join(
            Environment.NewLine,
            groups.Select(group =>
                $"""
                <group targetFramework="{group.Framework}">
                  <dependency id="{group.Dependency}" version="[{Version}]" />
                </group>
                """));
        return System.Text.Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{Version}</version>
                <authors>dotnet-inspect tests</authors>
                <description>Workspace Package route fixture.</description>
                <dependencies>
                  {dependencies}
                </dependencies>
              </metadata>
            </package>
            """);
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The Package Workspace route attempted a feed request: "
                    + request.RequestUri);
    }

    sealed class VersionListingHandler : HttpMessageHandler
    {
        const string FlatContainer = "https://example.test/flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            string? body = url switch
            {
                SourceUrl =>
                    $$"""
                    {"resources":[{"@id":"{{FlatContainer}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """,
                $"{FlatContainer}selected.package/index.json" =>
                    $$"""{"versions":["{{Version}}"]}""",
                _ => null,
            };
            if (body is null)
            {
                throw new InvalidOperationException(
                    "The Package Workspace route attempted an unexpected "
                        + "feed request: "
                        + request.RequestUri);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                    RequestMessage = request,
                });
        }
    }

    sealed class DependencyHandler(
        string selectedPackage,
        string dependencyPackage,
        DependencyRequestLog requests) : HttpMessageHandler
    {
        const string FlatContainer = "https://example.test/flat/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Requests.Add(url);
            string selectedPath =
                $"/{selectedPackage.ToLowerInvariant()}/";
            if (url.Contains(selectedPath, StringComparison.Ordinal))
            {
                requests.SelectedPackageRequests++;
                throw new InvalidOperationException(
                    "The admitted Workspace Package root was reacquired.");
            }

            string dependency = dependencyPackage.ToLowerInvariant();
            string? body;
            if (url.Equals(SourceUrl, StringComparison.Ordinal))
            {
                body =
                    $$"""
                    {"resources":[{"@id":"{{FlatContainer}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """;
            }
            else if (url.Equals(
                $"{FlatContainer}{dependency}/index.json",
                StringComparison.Ordinal))
            {
                body = $$"""{"versions":["{{Version}}"]}""";
            }
            else if (url.Equals(
                $"{FlatContainer}{dependency}/{Version}/{dependency}.nuspec",
                StringComparison.Ordinal))
            {
                body = System.Text.Encoding.UTF8.GetString(
                    Nuspec(dependencyPackage));
            }
            else if (url.EndsWith(
                $"/{dependency}/{Version}/{dependency}.nuspec",
                StringComparison.Ordinal))
            {
                body = System.Text.Encoding.UTF8.GetString(
                    Nuspec(dependencyPackage));
            }
            else
            {
                body = null;
            }
            if (body is null)
            {
                throw new InvalidOperationException(
                    "The Package dependency traversal attempted an unexpected "
                        + "feed request: "
                        + request.RequestUri);
            }

            if (!url.Equals(SourceUrl, StringComparison.Ordinal))
                requests.DependencyRequests++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                    RequestMessage = request,
                });
        }
    }

    sealed class DependencyRequestLog
    {
        public int DependencyRequests { get; set; }
        public int SelectedPackageRequests { get; set; }
        public List<string> Requests { get; } = [];
    }

    sealed class NoCredentials : NuGetFetch.Plugins.ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The test feed does not request credentials.");
    }
}
