using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExternalCallGraphCommandTests
{
    const string RootPackageId =
        "ILInspector.Analysis.CallerGraphCaller";
    const string TargetPackageId =
        "ILInspector.Analysis.CallerGraphTarget";
    const string Version = "1.0.0";
    const string Framework = "net10.0";

    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    public ExternalCallGraphCommandTests() =>
        PersistentCache.Initialize("dotnet-inspect-test");

    [Fact]
    public void Command_ExposesExactRootAndAutomaticTraversal()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "calls",
                "Shared.Entry",
                "RunOuter",
                "--root-package",
                $"{RootPackageId}@{Version}",
                "--root-tfm",
                Framework,
                "--tfm",
                Framework,
            ]);

        Assert.Empty(result.Errors);
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--root-package");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--root-tfm");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--tfm");
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name == "--package");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--depth");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--max-nodes");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--baseline");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--first-party-prefix");
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name == "--direction");
    }

    [Fact]
    public async Task Command_HelpDoesNotRequireFocusArguments()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(["graph", "calls", "--help"])
                .InvokeAsync());

        Assert.True(
            captured.ExitCode == 0,
            captured.Error);
        Assert.Contains(
            "Show supply-chain package exits",
            captured.Output);
        Assert.Equal("", captured.Error);
    }

    [Theory]
    [InlineData(
        new[]
        {
            "--baseline",
            "everything",
        },
        "--baseline must be nothing, self, or self+registered-ecosystems.")]
    [InlineData(
        new[]
        {
            "--first-party-prefix",
            "*invalid",
        },
        "--first-party-prefix '*invalid' is not a valid package prefix.")]
    [InlineData(
        new[]
        {
            "--baseline",
            "nothing",
            "--first-party-prefix",
            "Contoso.",
        },
        "--first-party-prefix requires the self or self+registered-ecosystems baseline.")]
    public async Task Command_RejectsInvalidSupplyChainPolicy(
        string[] policyArguments,
        string expected)
    {
        string[] arguments =
        [
            "graph",
            "calls",
            "Shared.Entry",
            "RunOuter",
            "--root-package",
            $"{RootPackageId}@{Version}",
            "--root-tfm",
            Framework,
            .. policyArguments,
        ];
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(arguments)
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(expected, captured.Error);
    }

    [Fact]
    public void DefaultSupplyChainPlanIncludesPlatformAndFirstPartyScope()
    {
        var options = new ExternalCallGraphOptions
        {
            TypeName = "Shared.Entry",
            Member = "RunOuter",
            RootPackage = $"{RootPackageId}@{Version}",
            RootTfm = Framework,
            FirstPartyPackagePrefixes = ["Contoso."],
        };

        WorkspacePlan plan =
            ExternalCallGraphCommand.CreateWorkspacePlan(options);

        Assert.Contains(
            plan.Registrations,
            registration =>
                registration is WorkspaceRegistration.PackagePrefix prefix
                && prefix.Prefix.Prefix == "Contoso.");
        Assert.Contains(
            plan.Registrations,
            registration =>
                registration is WorkspaceRegistration.Ecosystem ecosystem
                && ecosystem.Declaration.Id.Value
                    == "ecosystem.runtime");
        Assert.Contains(
            plan.Registrations,
            registration =>
                registration is WorkspaceRegistration.Ecosystem ecosystem
                && ecosystem.Declaration.Id.Value
                    == "ecosystem.microsoft-extensions");
    }

    [Theory]
    [InlineData("--depth", "-1", "--depth must be non-negative.")]
    [InlineData("--max-nodes", "0", "--max-nodes must be positive.")]
    public async Task Command_RejectsInvalidBounds(
        string option,
        string value,
        string expected)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "calls",
                        "Shared.Entry",
                        "RunOuter",
                        "--root-package",
                        $"{RootPackageId}@{Version}",
                        "--root-tfm",
                        Framework,
                        option,
                        value,
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(expected, captured.Error);
        Assert.DoesNotContain("Exception", captured.Error);
    }

    [Theory]
    [InlineData(
        new[]
        {
            "graph",
            "calls",
            "Shared.Entry",
            "RunOuter",
            "--root-tfm",
            Framework,
        },
        "--root-package is required.")]
    [InlineData(
        new[]
        {
            "graph",
            "calls",
            "Shared.Entry",
            "RunOuter",
            "--root-package",
            RootPackageId,
        },
        "--root-tfm is required.")]
    public async Task Command_RequiresRootPackageAndRootFramework(
        string[] arguments,
        string expected)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(arguments)
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(expected, captured.Error);
    }

    [Fact]
    public async Task
        RunOuter_RendersConnectorBoundaryAndPhysicalReceipts()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Jsonl);

        Assert.True(
            execution.ExitCode == 0,
            execution.Error);
        Assert.Equal("", execution.Error);
        string[] lines = execution.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        JsonElement[] rows =
        [
            .. lines.Select(line =>
            {
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }),
        ];
        Assert.Contains(
            rows,
            row =>
                row.GetProperty("source").GetString()
                    == "Shared.Entry::RunOuter"
                && row.GetProperty("role").GetString() == "connector"
                && row.GetProperty("target").GetString()
                    == "Shared.Entry::Run");
        JsonElement boundary = Assert.Single(
            rows,
            row =>
                row.GetProperty("role").GetString()
                    == "boundary");
        Assert.Equal(
            "Shared.Entry::Run",
            boundary.GetProperty("source").GetString());
        Assert.Equal(
            "Target.Api::Ping",
            boundary.GetProperty("target").GetString());
        Assert.Equal(
            "ILInspector.Analysis.CallerGraphTarget",
            boundary.GetProperty("target_assembly").GetString());
        Assert.Equal(1, boundary.GetProperty("call_sites").GetInt32());
        string evidence =
            boundary.GetProperty("evidence").GetString()!;
        Assert.Contains("method=0x", evidence);
        Assert.Contains("il=IL_", evidence);
        Assert.Contains("operand=0x", evidence);
        Assert.Contains("call=Call", evidence);
        Assert.Contains("dispatch=", evidence);
        Assert.Contains("loop=false", evidence);
    }

    [Fact]
    public async Task ExactGenerationBoundary_RendersAsBoundary()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference[] assemblies =
        [
            ResolvedAssemblyReference.CreateFromPath(
                callerPath,
                AssemblyResolutionProvenance.Local(
                    "external call graph CLI test")),
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "external call graph CLI test")),
        ];
        var policy =
            new SourceRelativeAssemblyGroupBindingPolicy(
                assemblies.Select(assembly => (
                    assembly,
                    Policy: (IAssemblyBindingPolicy)
                        new DotnetInspector.Services
                            .AssemblyDependencyResolver(
                                new(
                                    assembly.Path!)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                assemblies.Select(assembly =>
                    new AssemblyContextParticipant(
                        assembly,
                        policy)));
        int methodToken =
            Analysis.LibraryBodyIndex.Open(callerPath)
                .Methods.Single(method =>
                    method.DeclaringType.Name == "Entry"
                    && method.Name == "RunOuter")
                .MetadataToken;
        using var session = new MemberCallGraphSession(
            group,
            assemblies[0],
            methodToken);
        InspectionGraphDocument document =
            session.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 2,
                    maxNodes: 10));

        var captured = await ConsoleCapture.RunAsync(
            () => Task.FromResult(
                ExternalCallGraphOutputAdapter.Write(
                    document,
                    new ExternalCallGraphOptions
                    {
                        TypeName = "Shared.Entry",
                        Member = "RunOuter",
                        RootPackage = RootPackageId,
                        RootTfm = Framework,
                        Tfm = Framework,
                        Format = OutputFormat.Jsonl,
                    })));

        Assert.True(
            captured.ExitCode == 0,
            captured.Error);
        Assert.Contains(
            "\"role\":\"boundary\"",
            captured.Output);
        Assert.Contains(
            "\"role\":\"connector\"",
            captured.Output);
        Assert.DoesNotContain(
            "\"role\":\"unclassified-boundary\"",
            captured.Output);
    }

    [Fact]
    public async Task RunAcrossBoundary_OmitsExternalContinuation()
    {
        Execution execution = await ExecuteAsync(
            "RunAcrossBoundary",
            OutputFormat.Table);

        Assert.Equal(0, execution.ExitCode);
        Assert.Contains("boundary", execution.Output);
        Assert.Contains("Target.Api::Forward", execution.Output);
        Assert.DoesNotContain("Target.Api::Leaf", execution.Output);
    }

    [Fact]
    public async Task ZeroDepth_RetainsFocusAndExplainsEmptyGraph()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Markdown,
            depth: 0);

        Assert.Equal(0, execution.ExitCode);
        Assert.Contains("# Supply Chain Call Graph", execution.Output);
        Assert.Contains("Shared.Entry::RunOuter", execution.Output);
        Assert.Contains(
            "No out-of-baseline package calls were found",
            execution.Output);
        Assert.Contains(
            "call.traversal-incomplete",
            execution.Error);
    }

    [Fact]
    public async Task RowLimit_AppliesToLogicalEdges()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Jsonl,
            rowSelection: RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(1),
                ]));

        Assert.Equal(0, execution.ExitCode);
        Assert.Single(
            execution.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Json_RetainsRequestAndSelectedEdges()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Json);

        Assert.Equal(0, execution.ExitCode);
        using JsonDocument document =
            JsonDocument.Parse(execution.Output);
        Assert.Equal(
            "Shared.Entry::RunOuter",
            document.RootElement.GetProperty("focus").GetString());
        Assert.Equal(
            "ILInspector.Analysis.CallerGraphCaller",
            document.RootElement.GetProperty("focus_assembly")
                .GetString());
        Assert.Equal(
            3,
            document.RootElement.GetProperty("max_depth")
                .GetInt32());
        Assert.Equal(
            25,
            document.RootElement.GetProperty("max_nodes")
                .GetInt32());
        Assert.Equal(
            2,
            document.RootElement.GetProperty("edges")
                .GetArrayLength());
    }

    [Theory]
    [InlineData(OutputFormat.Mermaid, "graph TD")]
    [InlineData(OutputFormat.PlainText, "Shared.Entry::RunOuter")]
    [InlineData(
        OutputFormat.Tsv,
        "source\tsource_assembly\trole\ttarget")]
    public async Task GraphFormats_LowerTheSameEdges(
        OutputFormat format,
        string expected)
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            format);

        Assert.Equal(0, execution.ExitCode);
        Assert.Contains(expected, execution.Output);
        Assert.Contains("connector", execution.Output);
        Assert.Contains("boundary", execution.Output);
    }

    [Fact]
    public async Task Count_AppliesAfterLogicalEdgeSelection()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Markdown,
            rowSelection: RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(1),
                ]),
            count: true);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal($"1{Environment.NewLine}", execution.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Count_CommandInvocationHonorsProjectionAudit()
    {
        string[] arguments =
        [
            "graph",
            "calls",
            "Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions",
            "AddOpenTelemetrySharedProviderBuilderServices~4d95928639",
            "--root-package",
            "OpenTelemetry@1.18.0",
            "--root-tfm",
            Framework,
            "--tfm",
            Framework,
            "--all",
            "--count",
            "-n",
            "1",
        ];
        var captured = await ConsoleCapture.RunAsync(
            () =>
            {
                var parsed =
                    CommandLineBuilder.CreateRootCommand()
                        .Parse(arguments);
                Assert.Empty(parsed.Errors);
                return CommandLineBuilder.InvokeAsync(
                    parsed,
                    arguments);
            });

        Assert.True(
            captured.ExitCode == 0,
            captured.Error);
        Assert.Equal($"1{Environment.NewLine}", captured.Output);
        Assert.DoesNotContain(
            "unprojected output",
            captured.Error);
    }

    [Fact]
    public async Task MissingDependencyParticipant_RemainsUnclassified()
    {
        Execution execution = await ExecuteAsync(
            "RunOuter",
            OutputFormat.Jsonl,
            includeTarget: false);

        Assert.Equal(0, execution.ExitCode);
        Assert.Contains(
            "\"role\":\"unclassified-boundary\"",
            execution.Output);
        Assert.Contains(
            "external-boundary-classification-incomplete",
            execution.Error);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        OpenTelemetry_DefaultBaselineHighlightsOneKnownDependency()
    {
        var options = new ExternalCallGraphOptions
        {
            TypeName =
                "Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions",
            Member =
                "AddOpenTelemetrySharedProviderBuilderServices~4d95928639",
            RootPackage = "OpenTelemetry@1.18.0",
            RootTfm = Framework,
            Tfm = Framework,
            IncludeAll = true,
            Format = OutputFormat.Jsonl,
        };

        var captured = await ConsoleCapture.RunAsync(
            () => ExternalCallGraphCommand.ExecuteAsync(
                options,
                TestContext.Current.CancellationToken));

        Assert.True(
            captured.ExitCode == 0,
            captured.Error);
        string[] edges = captured.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, edges.Length);
        Assert.Equal(
            5,
            edges.Count(edge =>
                edge.Contains(
                    "\"role\":\"connector\"",
                    StringComparison.Ordinal)));
        Assert.Single(
            edges,
            edge =>
                edge.Contains(
                    "\"role\":\"boundary\"",
                    StringComparison.Ordinal));
        Assert.Contains(
            edges,
            edge =>
                edge.Contains(
                    "\"role\":\"boundary\"",
                    StringComparison.Ordinal)
                && edge.Contains(
                    "RuntimeContextSlot",
                    StringComparison.Ordinal)
                && edge.Contains(
                    "\"target_assembly\":\"OpenTelemetry.Api\"",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            edges,
            edge =>
                edge.Contains(
                    "\"role\":\"boundary\"",
                    StringComparison.Ordinal)
                && edge.Contains(
                    "\"target_assembly\":\"Microsoft.Extensions.",
                    StringComparison.Ordinal));
        Assert.Contains(
            "external-boundary-classification-incomplete",
            captured.Error);

        var mermaidOptions = new ExternalCallGraphOptions
        {
            TypeName =
                "Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions",
            Member =
                "AddOpenTelemetrySharedProviderBuilderServices~4d95928639",
            RootPackage = "OpenTelemetry@1.18.0",
            RootTfm = Framework,
            Tfm = Framework,
            IncludeAll = true,
            Format = OutputFormat.Mermaid,
        };
        var mermaid = await ConsoleCapture.RunAsync(
            () => ExternalCallGraphCommand.ExecuteAsync(
                mermaidOptions,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, mermaid.ExitCode);
        Assert.Contains(
            "[\"OpenTelemetry.Api\"]",
            mermaid.Output);
        Assert.Contains(
            "RuntimeContextSlot",
            mermaid.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Polly_AutomaticallyLoadsDependencyBoundary()
    {
        var options = new ExternalCallGraphOptions
        {
            TypeName =
                "Microsoft.Extensions.DependencyInjection.PollyHttpClientBuilderExtensions",
            Member = "AddTransientHttpErrorPolicy",
            RootPackage =
                "Microsoft.Extensions.Http.Polly@11.0.0-rc.1.26425.128",
            RootTfm = "netstandard2.0",
            IncludeAll = true,
            Depth = 3,
            MaxNodes = 50,
            Format = OutputFormat.Jsonl,
        };
        WorkspaceContextLoadOptions loadOptions =
            ExternalCallGraphCommand.CreateLoadOptions(options);
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(
                loadOptions.HttpClient.Timeout);
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>? envelope =
            null;

        var captured = await ConsoleCapture.RunAsync(
            () => ExternalCallGraphCommand.ExecuteAsync(
                options,
                loadOptions,
                TestContext.Current.CancellationToken,
                async (request, cancellationToken) =>
                {
                    envelope =
                        await ExternalCallGraphCommand
                            .ExecuteInspectionAsync(
                                request,
                                options,
                                fetchOptions,
                                cancellationToken);
                    return envelope;
                }));

        Assert.True(
            captured.ExitCode == 0,
            captured.Error);
        Assert.Contains(
            "\"target_assembly\":\"Polly.Extensions.Http\"",
            captured.Output);
        Assert.Contains(
            "\"role\":\"boundary\",\"target\":\"Polly.Extensions.Http.HttpPolicyExtensions::HandleTransientHttpError\"",
            captured.Output);
        Assert.DoesNotContain(
            "same assembly identity",
            captured.Error);
        var available =
            Assert.IsType<
                PackageDependencyMemberCallGraphInspectionOutcome.Available>(
                envelope?.Content);
        InspectionGraphNode target = Assert.Single(
            available.Document.Graph.Nodes,
            node =>
                node.Subject
                    is InspectionGraphSubject.MemberSubject
                    {
                        Identity:
                            InspectionGraphMemberIdentity.CallGraph
                            {
                                Member.Name:
                                    "HandleTransientHttpError",
                            },
                    });
        PackageDependencyMemberCallGraphPackageSubject package =
            Assert.Single(
                available.Document.PackageSubjects,
                subject => subject.NodeId == target.Id);
        Assert.Equal(
            "Polly.Extensions.Http",
            package.PackageId,
            ignoreCase: true);
        Assert.Equal("3.0.0", package.PackageVersion);
        Assert.Equal("netstandard2.0", package.TargetFramework);
        Assert.Equal(
            PackageSupplyChainBaseline
                .SelfAndRegisteredEcosystems,
            available.Document.Baseline.Kind);
        Assert.Contains(
            "ecosystem.runtime",
            available.Document.Baseline.RegisteredEcosystems);
        Assert.Contains(
            "ecosystem.microsoft-extensions",
            available.Document.Baseline.RegisteredEcosystems);
        Assert.DoesNotContain(
            "\"target_assembly\":\"System.Text.Json\"",
            captured.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OpenTelemetry_AbstractFocusFailsAsMissingBody()
    {
        var options = new ExternalCallGraphOptions
        {
            TypeName =
                "OpenTelemetry.Context.RuntimeContextSlot`1",
            Member = "Get",
            RootPackage = "OpenTelemetry.Api@1.18.0",
            RootTfm = Framework,
            Tfm = Framework,
            IncludeAll = true,
            Format = OutputFormat.Json,
        };

        var captured = await ConsoleCapture.RunAsync(
            () => ExternalCallGraphCommand.ExecuteAsync(
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Equal("", captured.Output);
        Assert.Contains(
            "without a managed implementation body",
            captured.Error);
        Assert.Contains(
            "Select a non-abstract managed method",
            captured.Error);
    }

    static async Task<Execution> ExecuteAsync(
        string member,
        OutputFormat format,
        int depth = 3,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeTarget = true,
        bool count = false)
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        await CommitPackageAsync(
            store,
            sourceKey,
            RootPackageId,
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            includeTarget ? TargetPackageId : null);
        if (includeTarget)
        {
            await CommitPackageAsync(
                store,
                sourceKey,
                TargetPackageId,
                FixtureCatalog.AnalysisCallerGraphTarget
                    .AssemblyPath());
        }

        using var client = new HttpClient(new FailingHandler());
        var loadOptions = new WorkspaceContextLoadOptions
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
            IncludePackageRootBindings = true,
        };
        var options = new ExternalCallGraphOptions
        {
            TypeName = "Shared.Entry",
            Member = member,
            RootPackage = $"{RootPackageId}@{Version}",
            RootTfm = Framework,
            Tfm = Framework,
            Depth = depth,
            MaxNodes = 25,
            Format = format,
            RowSelection = rowSelection,
            Count = count,
        };

        var captured = await ConsoleCapture.RunAsync(
            () => ExternalCallGraphCommand.ExecuteAsync(
                options,
                loadOptions,
                TestContext.Current.CancellationToken,
                (request, _) =>
                    CreateFixtureEnvelopeAsync(
                        member,
                        request.Graph.MaxDepth,
                        request.Graph.MaxNodes,
                        includeTarget)));
        return new Execution(
            captured.ExitCode,
            captured.Output,
            captured.Error);
    }

    static async ValueTask<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>>
        CreateFixtureEnvelopeAsync(
            string member,
            int depth,
            int maxNodes,
            bool includeTarget)
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference[] assemblies =
            includeTarget
                ?
                [
                    ResolvedAssemblyReference.CreateFromPath(
                        callerPath,
                        AssemblyResolutionProvenance.Local(
                            "external call graph CLI test")),
                    ResolvedAssemblyReference.CreateFromPath(
                        targetPath,
                        AssemblyResolutionProvenance.Local(
                            "external call graph CLI test")),
                ]
                :
                [
                    ResolvedAssemblyReference.CreateFromPath(
                        callerPath,
                        AssemblyResolutionProvenance.Local(
                            "external call graph CLI test")),
                ];
        var policy =
            new SourceRelativeAssemblyGroupBindingPolicy(
                assemblies.Select(assembly => (
                    assembly,
                    Policy: (IAssemblyBindingPolicy)
                        new DotnetInspector.Services
                            .AssemblyDependencyResolver(
                                new(
                                    assembly.Path!)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                assemblies.Select(assembly =>
                    new AssemblyContextParticipant(
                        assembly,
                        policy)));
        int methodToken =
            Analysis.LibraryBodyIndex.Open(callerPath)
                .Methods.Single(methodInfo =>
                    methodInfo.DeclaringType.Name == "Entry"
                    && methodInfo.Name == member)
                .MetadataToken;
        using var session = new MemberCallGraphSession(
            group,
            assemblies[0],
            methodToken);
        InspectionGraphDocument graph =
            session.CrossLibraryCalleeNeighborhood(
                new MemberCallGraphCalleeNeighborhoodRequest(
                    depth,
                    maxNodes));
        var content =
            new PackageDependencyMemberCallGraphInspectionOutcome.Available(
                new PackageDependencyMemberCallGraphDocument(
                    TraversalTargetFrameworkPolicy.ProductDefault,
                    new PackageDependencyTraversalSummary(
                        CompleteRoots: 1,
                        DepthBoundedRoots: 0,
                        SourceBoundedRoots: 0,
                        PartialRoots: 0),
                    [],
                    new PackageSupplyChainBaselineEvidence(
                        PackageSupplyChainBaseline.Nothing,
                        ["test.root"],
                        [],
                        []),
                    [],
                    graph));
        return new InspectionEnvelope<
                PackageDependencyMemberCallGraphInspectionOutcome>(
                content,
                new InspectionShare.NonProjectable(
                    "package-dependency-member-call-graph/share",
                    "Test projection."));
    }

    static async Task CommitPackageAsync(
        InMemoryPackageStore store,
        string sourceKey,
        string packageId,
        string assemblyPath,
        string? dependency = null)
    {
        string dependencyXml = dependency is null
            ? ""
            : $"<dependencies><dependency id=\"{dependency}\" version=\"[{Version}]\" /></dependencies>";
        string nuspec =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">"
            + $"<metadata><id>{packageId}</id><version>{Version}</version>"
            + $"<authors>tests</authors><description>tests</description>{dependencyXml}"
            + "</metadata></package>";
        byte[] assembly = await File.ReadAllBytesAsync(
            assemblyPath,
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{packageId}.nuspec",
                System.Text.Encoding.UTF8.GetBytes(nuspec)),
            ($"lib/{Framework}/{Path.GetFileName(assemblyPath)}", assembly));
        using var stream = new MemoryStream(package);
        await store.CommitAsync(
            packageId,
            Version,
            sourceKey,
            stream,
            TestContext.Current.CancellationToken);
    }

    sealed record Execution(
        int ExitCode,
        string Output,
        string Error);

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
    }
}
