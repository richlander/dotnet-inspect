using System.Net;
using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class InspectionGraphCommandTests
{
    const string PackageId = "test.graph.fixture";
    const string OtherPackageId = "other.graph.fixture";
    const string ThirdPackageId = "third.graph.fixture";
    const string Version = "1.0.0";
    const string Framework = "net10.0";

    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    static InspectionGraphRelationshipDescriptor TestRelationship { get; } =
        new(
            "integration.test",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Synthetic,
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Package],
            [
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            InspectionGraphOccurrenceIdentityProjection.SyntheticNoOccurrence,
            []);

    static InspectionGraphRelationshipDescriptor TestTypeRelationship
        { get; } =
        new(
            "integration.type-test",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Synthetic,
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            InspectionGraphOccurrenceIdentityProjection.SyntheticNoOccurrence,
            []);

    [Fact]
    public void GraphCommand_IsReservedFromImplicitPackageRouting()
    {
        string[] arguments =
            CommandLineBuilder.PreprocessArgs(
                ["graph", "integrations", "--help"]);

        Assert.Equal("graph", arguments[0]);
        Assert.Contains("graph", CommandLineBuilder.KnownCommands);
    }

    [Fact]
    public void IntegrationsCommand_ExposesInducedSetInputsWithoutTraversal()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "integrations",
                "--package",
                "Package.A",
                "--package",
                "Package.B@1.0.0",
                "--tfm",
                "net10.0",
                "--relationship",
                InspectionGraphIntegrationsCatalog.IntegrationObserved.Id,
            ]);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name is "--depth" or "--direction");
    }

    [Fact]
    public void IntegrationsCommand_RejectsUnknownRelationshipAtParseTime()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "integrations",
                "--package",
                "Package.A",
                "--tfm",
                "net10.0",
                "--relationship",
                "call",
            ]);

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "call",
                StringComparison.Ordinal));
    }

    [Fact]
    public void IntegrationsCommand_RejectsOptionAsMissingRelationshipValue()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "integrations",
                "--package",
                "Package.A",
                "--tfm",
                "net10.0",
                "--relationship",
                "--count",
            ]);

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "--relationship requires a relationship id",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntegrationsCommand_RejectsBareRelationshipWithoutStack()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "integrations",
                        "--package",
                        "Package.A",
                        "--tfm",
                        "net10.0",
                        "--relationship",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("--relationship", captured.Error);
        Assert.DoesNotContain("Exception", captured.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnknownRelationshipWithoutStack()
    {
        Execution execution = await ExecuteAsync(
            relationships: ["--count"],
            injectedDocument: GraphWithTwoEdges());

        Assert.Equal(1, execution.ExitCode);
        Assert.Contains(
            "Unknown Integration graph relationship",
            execution.Error);
        Assert.DoesNotContain("Exception", execution.Error);
    }

    [Fact]
    public async Task IntegrationsCommand_RequiresPackageAndSharedTfm()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var missingPackage = await ConsoleCapture.RunAsync(
            () => root.Parse(
                    ["graph", "integrations", "--tfm", Framework])
                .InvokeAsync());
        var missingTfm = await ConsoleCapture.RunAsync(
            () => root.Parse(
                    [
                        "graph",
                        "integrations",
                        "--package",
                        PackageId,
                    ])
                .InvokeAsync());

        Assert.Equal(1, missingPackage.ExitCode);
        Assert.Empty(missingPackage.Output);
        Assert.Contains(
            "At least one --package is required.",
            missingPackage.Error);
        Assert.Contains(
            "dotnet-inspect graph integrations --help",
            missingPackage.Error);
        Assert.Equal(1, missingTfm.ExitCode);
        Assert.Empty(missingTfm.Output);
        Assert.Contains(
            "A shared --tfm is required.",
            missingTfm.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UsesExactPackageSetAndStructuredRequest()
    {
        Execution captured = await ExecuteAsync(
            ["--json"],
            relationships:
            [
                InspectionGraphIntegrationsCatalog.IntegrationObserved.Id,
            ]);

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument json =
            JsonDocument.Parse(captured.Output);
        JsonElement root = json.RootElement;
        Assert.Equal(
            "induced-set",
            root.GetProperty("mode").GetString());
        Assert.Equal(
            [$"{PackageId}@{Version}"],
            root.GetProperty("subjects")
                .EnumerateArray()
                .Select(static value => value.GetString()));
        Assert.Equal(
            [
                InspectionGraphIntegrationsCatalog
                    .IntegrationObserved.Id,
            ],
            root.GetProperty("relationships")
                .EnumerateArray()
                .Select(static value => value.GetString()));
    }

    [Fact]
    public async Task ExecuteAsync_RealQueryReportsIntrinsicBindingUnavailableAndOmitsAbsentExtensionEndpoint()
    {
        bool observedMissingPeer = false;
        Execution execution = await ExecuteAsync(
            ["--json"],
            relationships:
            [
                InspectionGraphIntegrationsCatalog.Extension.Id,
            ],
            documentFactory: (context, request) =>
            {
                InspectionGraphDocument workspace =
                    InspectionGraphIntegrationsQuery.Execute(context);
                InspectionGraphFailure missing = Assert.Single(
                    workspace.Failures,
                    failure =>
                    {
                        if (failure.Target is not
                            {
                                Kind: InspectionGraphTargetKind.Node,
                            } target)
                        {
                            return false;
                        }

                        return workspace.Nodes[target.Id].Subject is
                        InspectionGraphSubject.MemberSubject
                        {
                            Identity:
                                InspectionGraphMemberIdentity.AcquiredApi
                                acquired,
                        }
                        && acquired.Member.StableSelector.Contains(
                            nameof(
                                InspectionGraphMissingPeerExtensions
                                    .MissingPeer),
                            StringComparison.Ordinal);
                    });
                var evidence = Assert.IsType<
                    InspectionGraphIntegrationFailureEvidence>(
                        missing.Evidence);
                Assert.Contains(
                    evidence.Details,
                    detail =>
                        detail.Producer == "extensions"
                        && detail.Kind
                            == InspectionGraphIntegrationFailureKind
                                .BindingMissing
                        && detail.Reference?.Name == "System.Net.Http");
                observedMissingPeer = true;
                return InspectionGraphIntegrationsQuery.Execute(
                    context,
                    request);
            });

        Assert.True(observedMissingPeer);
        Assert.Equal(1, execution.ExitCode);
        Assert.Contains(
            "extensions: BindingUnavailable (2 graph targets)",
            execution.Error,
            StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(execution.Output);
        JsonElement root = json.RootElement;
        Assert.Equal(
            2,
            root.GetProperty("failures").GetArrayLength());
        Assert.All(
            root.GetProperty("failures").EnumerateArray(),
            static failure => Assert.Equal(
                "BindingUnavailable",
                Assert.Single(
                    failure.GetProperty("details")
                        .EnumerateArray())
                    .GetProperty("kind")
                    .GetString()));
        Assert.DoesNotContain(
            root.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("label").GetString()!
                .Contains(
                    nameof(
                        InspectionGraphMissingPeerExtensions.MissingPeer),
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutputModes_UseTheSameWindowedLogicalEdges()
    {
        InspectionGraphDocument document = GraphWithTwoEdges();

        Execution markdown = await ExecuteAsync(
            ["--rows", "1"],
            injectedDocument: document);
        Execution table = await ExecuteAsync(
            ["--table", "--rows", "1"],
            injectedDocument: document);
        Execution json = await ExecuteAsync(
            ["--json", "--rows", "1"],
            injectedDocument: document);
        Execution jsonLines = await ExecuteAsync(
            ["--jsonl", "--rows", "1"],
            injectedDocument: document);
        Execution count = await ExecuteAsync(
            ["--count", "--rows", "1"],
            injectedDocument: document);

        Assert.All(
            [markdown, table, json, jsonLines, count],
            static execution => Assert.Equal(0, execution.ExitCode));
        Assert.Contains(PackageId, markdown.Output, StringComparison.Ordinal);
        Assert.Contains(OtherPackageId, markdown.Output, StringComparison.Ordinal);
        Assert.Equal(
            1,
            MarkdownTableTestOracle.CountRows(markdown.Output));
        Assert.Contains(PackageId, table.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ThirdPackageId, table.Output, StringComparison.Ordinal);
        Assert.Equal("1", count.Output.Trim());

        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement edge = Assert.Single(
            parsed.RootElement.GetProperty("edges").EnumerateArray());
        Assert.DoesNotContain(
            ThirdPackageId,
            edge.GetProperty("target").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            3,
            parsed.RootElement.GetProperty("groups").GetArrayLength());
        Assert.Single(
            jsonLines.Output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        using JsonDocument jsonLine = JsonDocument.Parse(jsonLines.Output);
        Assert.Equal(
            JsonValueKind.Number,
            jsonLine.RootElement.GetProperty("occurrences").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            jsonLine.RootElement.GetProperty("source_assembly").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            jsonLine.RootElement.GetProperty("evidence").ValueKind);
        Assert.False(
            jsonLine.RootElement.TryGetProperty("edge_id", out _));
    }

    [Fact]
    public async Task EmptyRowWindow_DoesNotRepopulatePackageContext()
    {
        InspectionGraphDocument document = GraphWithTwoEdges();
        RowWindow empty = RowWindow.Range(9, 10);

        Execution markdown = await ExecuteAsync(
            injectedDocument: document,
            rows: empty);
        Execution mermaid = await ExecuteAsync(
            ["--mermaid"],
            injectedDocument: document,
            rows: empty);
        Execution json = await ExecuteAsync(
            ["--json"],
            injectedDocument: document,
            rows: empty);

        Assert.Contains(
            "No Integration relationships are selected by the row window.",
            markdown.Output);
        Assert.DoesNotContain(PackageId, mermaid.Output, StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        Assert.Empty(parsed.RootElement.GetProperty("nodes").EnumerateArray());
        Assert.Empty(parsed.RootElement.GetProperty("groups").EnumerateArray());
        Assert.Empty(parsed.RootElement.GetProperty("edges").EnumerateArray());
    }

    [Fact]
    public async Task EdgeFreeMarkdown_RetainsExplicitPackageContext()
    {
        Execution execution = await ExecuteAsync(
            injectedDocument: EdgeFreeGraph());

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal(
            2,
            execution.Output.Split(
                PackageId,
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            execution.Output.Split(
                OtherPackageId,
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToTheIntegrationRelationshipFamily()
    {
        InspectionGraphInducedSetRequest? capturedRequest = null;

        Execution execution = await ExecuteAsync(
                injectedDocument: GraphWithTwoEdges(),
                captureRequest: request => capturedRequest = request);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal(
                [
                    InspectionGraphIntegrationsCatalog.Extension.Id,
                    InspectionGraphIntegrationsCatalog.IntegrationObserved.Id,
                    InspectionGraphIntegrationsCatalog.IntegrationOpportunity.Id,
                ],
                capturedRequest!.Relationships.Select(
                    static relationship => relationship.Id));
    }

    [Fact]
    public async Task GraphRenderers_UseTypedNodesAndRelationshipLabels()
    {
        InspectionGraphDocument document = GraphWithTwoEdges();

        Execution tree = await ExecuteAsync(
            ["--tree"],
            injectedDocument: document);
        Execution mermaid = await ExecuteAsync(
            ["--mermaid"],
            injectedDocument: document);

        Assert.Equal(0, tree.ExitCode);
        Assert.Equal(0, mermaid.ExitCode);
        Assert.Contains(PackageId, tree.Output, StringComparison.Ordinal);
        Assert.Contains(OtherPackageId, tree.Output, StringComparison.Ordinal);
        Assert.Contains(TestRelationship.Id, tree.Output, StringComparison.Ordinal);
        Assert.StartsWith("graph TD", mermaid.Output);
        Assert.Contains(TestRelationship.Id, mermaid.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionShapedEndpoints_RetainPackageOwnership()
    {
        InspectionGraphDocument document =
            GraphWithDuplicateTypeLabels();

        Execution table = await ExecuteAsync(
            ["--table"],
            injectedDocument: document);
        Execution jsonLines = await ExecuteAsync(
            ["--jsonl"],
            injectedDocument: document);
        Execution tree = await ExecuteAsync(
            ["--tree"],
            injectedDocument: document);
        Execution json = await ExecuteAsync(
            ["--json"],
            injectedDocument: document);

        Assert.All(
            [table, jsonLines, tree, json],
            static execution => Assert.Equal(0, execution.ExitCode));
        Assert.Contains("Source Group", table.Output);
        Assert.Contains("source_group", jsonLines.Output);
        Assert.Contains(PackageId, table.Output, StringComparison.Ordinal);
        Assert.Contains(
            OtherPackageId,
            table.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"[{PackageId}@{Version}]",
            tree.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"[{OtherPackageId}@{Version}]",
            tree.Output,
            StringComparison.Ordinal);

        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement edge = Assert.Single(
            parsed.RootElement.GetProperty("edges").EnumerateArray());
        Assert.Equal(
            $"{PackageId}@{Version}",
            edge.GetProperty("source_group").GetString());
        Assert.Equal(
            $"{OtherPackageId}@{Version}",
            edge.GetProperty("target_group").GetString());
        Assert.Equal(0, edge.GetProperty("occurrences").GetInt32());
        Assert.False(edge.TryGetProperty("occurrence_ids", out _));
    }

    [Fact]
    public async Task AcquiredEndpoints_RetainAssemblyWithinOnePackage()
    {
        Execution table = await ExecuteAsync(
            ["--table"],
            documentFactory:
                GraphWithDuplicateAcquiredTypeLabels);
        Execution tree = await ExecuteAsync(
            ["--tree"],
            documentFactory:
                GraphWithDuplicateAcquiredTypeLabels);
        Execution json = await ExecuteAsync(
            ["--json"],
            documentFactory:
                GraphWithDuplicateAcquiredTypeLabels);

        Assert.All(
            [table, tree, json],
            static execution => Assert.Equal(0, execution.ExitCode));
        Assert.Contains("Source Assembly", table.Output);
        Assert.Contains("dotnet-inspect.Tests", table.Output);
        Assert.Contains("dotnet-inspect, Version=", table.Output);
        Assert.Contains("dotnet-inspect.Tests", tree.Output);
        Assert.Contains("dotnet-inspect, Version=", tree.Output);

        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement edge = Assert.Single(
            parsed.RootElement.GetProperty("edges").EnumerateArray());
        string? sourceAssembly =
            edge.GetProperty("source_assembly").GetString();
        string? targetAssembly =
            edge.GetProperty("target_assembly").GetString();
        Assert.NotNull(sourceAssembly);
        Assert.NotNull(targetAssembly);
        Assert.NotEqual(sourceAssembly, targetAssembly);
        Assert.All(
            parsed.RootElement.GetProperty("nodes").EnumerateArray(),
            static node => Assert.Equal(
                JsonValueKind.String,
                node.GetProperty("assembly").ValueKind));
    }

    [Fact]
    public async Task AcquiredFailureTargets_RetainAssemblyWithinOnePackage()
    {
        Execution markdown = await ExecuteAsync(
            documentFactory:
                GraphWithDuplicateAcquiredTypeLabelFailures);
        Execution json = await ExecuteAsync(
            ["--json"],
            documentFactory:
                GraphWithDuplicateAcquiredTypeLabelFailures);

        Assert.All(
            [markdown, json],
            static execution => Assert.Equal(1, execution.ExitCode));
        string failures = markdown.Output[
            markdown.Output.IndexOf(
                "## Failures",
                StringComparison.Ordinal)..];
        Assert.Contains("dotnet-inspect.Tests, Version=", failures);
        Assert.Contains("dotnet-inspect, Version=", failures);

        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        string?[] targets =
        [
            .. parsed.RootElement.GetProperty("failures")
                .EnumerateArray()
                .Select(static failure =>
                    failure.GetProperty("target").GetString()),
        ];
        Assert.Equal(2, targets.Length);
        Assert.All(targets, static target => Assert.NotNull(target));
        Assert.NotEqual(targets[0], targets[1]);
        Assert.Contains(
            targets,
            static target => target!.Contains(
                "dotnet-inspect.Tests, Version=",
                StringComparison.Ordinal));
        Assert.Contains(
            targets,
            static target => target!.Contains(
                "dotnet-inspect, Version=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RowWindow_DoesNotReidentifyEdgesFromDuplicateLabels()
    {
        InspectionGraphDocument document = GraphWithDuplicateLabels();

        Execution json = await ExecuteAsync(
            ["--json", "--rows", "1"],
            injectedDocument: document);

        Assert.Equal(0, json.ExitCode);
        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement edge = Assert.Single(
            parsed.RootElement.GetProperty("edges").EnumerateArray());
        Assert.Equal(0, edge.GetProperty("from_node_id").GetInt32());
        Assert.DoesNotContain(
            parsed.RootElement.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("id").GetInt32() == 2);
    }

    [Fact]
    public async Task TreeRendering_OverridesAResolvedEnvironmentFormat()
    {
        Execution execution = await ExecuteAsync(
            ["--tree"],
            injectedDocument: GraphWithTwoEdges(),
            formatOverride: OutputFormat.Json);

        Assert.Equal(0, execution.ExitCode);
        Assert.Contains(PackageId, execution.Output, StringComparison.Ordinal);
        Assert.False(
            execution.Output.StartsWith(
                "{",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task VisibleGraphFailure_PreservesOutputAndNonzeroExit()
    {
        InspectionGraphDocument graph = GraphWithTwoEdges();
        var incomplete = new InspectionGraphDocument(
            graph.Scope,
            graph.InducedSetRequest!,
            graph.Nodes,
            graph.Groups,
            graph.Edges,
            graph.Occurrences,
            graph.Characteristics,
            graph.Seeds,
            graph.Limits,
            [
                new InspectionGraphFailure(
                    InspectionGraphIntegrationsCatalog.ProjectionFailure,
                    InspectionGraphTarget.Node(2)),
            ]);

        Execution execution = await ExecuteAsync(
            injectedDocument: incomplete);
        Execution json = await ExecuteAsync(
            ["--json"],
            injectedDocument: incomplete,
            rows: RowWindow.Head(1));

        Assert.Equal(1, execution.ExitCode);
        Assert.Equal(1, json.ExitCode);
        Assert.Contains(PackageId, execution.Output, StringComparison.Ordinal);
        Assert.Contains(
            InspectionGraphIntegrationsCatalog.ProjectionFailure.Id,
            execution.Output,
            StringComparison.Ordinal);
        Assert.Contains("Integration graph is incomplete", execution.Error);
        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement failure = Assert.Single(
            parsed.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Equal("Node", failure.GetProperty("target_kind").GetString());
        Assert.Equal(2, failure.GetProperty("target_id").GetInt32());
        Assert.Contains(
            parsed.RootElement.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("id").GetInt32() == 2);
    }

    [Fact]
    public async Task StructuredFailureText_IsInertAfterJsonParsing()
    {
        const string bidi = "\u202e";
        Execution json = await ExecuteAsync(
            ["--json"],
            documentFactory: (context, _) =>
            {
                InspectionGraphDocument graph = GraphWithTwoEdges();
                var detail = new InspectionGraphIntegrationFailureDetail(
                    "integrations",
                    context.Group.Participants[0]
                        .Assembly.Registration,
                    InspectionGraphIntegrationFailureKind.BindingMissing,
                    new CandidateOpenFailure(
                        CandidateOpenFailureKind.Unreadable,
                        $"acquisition{bidi}detail"),
                    new InvalidOperationException(
                        $"failure{bidi}message"),
                    new AssemblyReferenceIdentity(
                        $"assembly{bidi}name",
                        new Version(1, 0),
                        $"culture{bidi}name",
                        $"token{bidi}value"));
                return new InspectionGraphDocument(
                    graph.Scope,
                    graph.InducedSetRequest!,
                    graph.Nodes,
                    graph.Groups,
                    graph.Edges,
                    graph.Occurrences,
                    graph.Characteristics,
                    graph.Seeds,
                    graph.Limits,
                    [
                        new InspectionGraphFailure(
                            InspectionGraphIntegrationsCatalog
                                .ProjectionFailure,
                            InspectionGraphTarget.Node(2),
                            new InspectionGraphIntegrationFailureEvidence(
                                [detail])),
                    ]);
            });

        Assert.Equal(1, json.ExitCode);
        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement detail = Assert.Single(
            Assert.Single(
                parsed.RootElement.GetProperty("failures")
                    .EnumerateArray())
                .GetProperty("details")
                .EnumerateArray());
        Assert.DoesNotContain(
            bidi,
            detail.GetProperty("reference")
                .GetProperty("name").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            bidi,
            detail.GetProperty("acquisition_failure_detail")
                .GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            bidi,
            detail.GetProperty("error_message").GetString(),
            StringComparison.Ordinal);
    }

    static async Task<Execution> ExecuteAsync(
        string[]? additionalArguments = null,
        string[]? relationships = null,
        InspectionGraphDocument? injectedDocument = null,
        Action<InspectionGraphInducedSetRequest>? captureRequest = null,
        RowWindow? rows = null,
        OutputFormat? formatOverride = null,
        Func<
            WorkspaceContextLoadOutcome.Loaded,
            InspectionGraphInducedSetRequest,
            InspectionGraphDocument>? documentFactory = null)
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(InspectionGraphCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] productAssembly =
            await File.ReadAllBytesAsync(
                typeof(InspectionGraphCommand).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{Framework}/dotnet-inspect.Tests.dll", assembly),
            ($"lib/{Framework}/dotnet-inspect.dll", productAssembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions loadOptions = new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };
        string[] arguments = additionalArguments ?? [];
        OutputFormat format = arguments.Contains("--json")
            ? OutputFormat.Json
            : arguments.Contains("--jsonl")
                ? OutputFormat.Jsonl
                : arguments.Contains("--table")
                    ? OutputFormat.Table
                    : arguments.Contains("--mermaid")
                        ? OutputFormat.Mermaid
                        : OutputFormat.Markdown;
        int rowsIndex = Array.IndexOf(arguments, "--rows");
        InspectionGraphOptions options = new()
        {
            Packages = [$"{PackageId}@{Version}"],
            Tfm = Framework,
            Relationships = relationships ?? [],
            Format = formatOverride ?? format,
            Count = arguments.Contains("--count"),
            Tree = arguments.Contains("--tree"),
            Rows = rows ?? (rowsIndex >= 0 ? RowWindow.Head(1) : null),
        };

        Func<
            WorkspaceContextLoadOutcome.Loaded,
            InspectionGraphInducedSetRequest,
            InspectionGraphDocument>? queryExecutor = null;
        if (documentFactory is not null)
        {
            queryExecutor = (context, request) =>
            {
                captureRequest?.Invoke(request);
                return documentFactory(context, request);
            };
        }
        else if (injectedDocument is not null)
        {
            queryExecutor = (_, request) =>
            {
                captureRequest?.Invoke(request);
                return injectedDocument;
            };
        }

        var captured = await ConsoleCapture.RunAsync(
            () => InspectionGraphCommand.ExecuteAsync(
                options,
                loadOptions,
                TestContext.Current.CancellationToken,
                queryExecutor));
        return new Execution(
            captured.ExitCode,
            captured.Output,
            captured.Error);
    }

    static InspectionGraphDocument GraphWithTwoEdges()
    {
        InspectionGraphSubject first = PackageSubject(PackageId);
        InspectionGraphSubject second = PackageSubject(OtherPackageId);
        InspectionGraphSubject third = PackageSubject(ThirdPackageId);
        var request = new InspectionGraphInducedSetRequest(
            [first, second, third],
            [TestRelationship],
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);

        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            request,
            [
                new InspectionGraphNode(
                    0,
                    first,
                    InspectionGraphNodeRole.Ordinary,
                    [0]),
                new InspectionGraphNode(
                    1,
                    second,
                    InspectionGraphNodeRole.Ordinary,
                    [1]),
                new InspectionGraphNode(
                    2,
                    third,
                    InspectionGraphNodeRole.Ordinary,
                    [2]),
            ],
            [
                new InspectionGraphGroup(0, first, parentId: null),
                new InspectionGraphGroup(1, second, parentId: null),
                new InspectionGraphGroup(2, third, parentId: null),
            ],
            [
                new InspectionGraphEdge(0, 0, 1, TestRelationship, []),
                new InspectionGraphEdge(1, 1, 2, TestRelationship, []),
            ],
            [],
            [],
            [],
            [
                new InspectionGraphLimit(
                    InspectionGraphInducedSetCatalog.SubjectBound,
                    Evidence:
                        new InspectionGraphInducedSubjectBoundEvidence(3)),
            ],
            []);
    }

    static InspectionGraphDocument GraphWithDuplicateLabels()
    {
        InspectionGraphSubject first = PackageSubject(PackageId, "feed-a");
        InspectionGraphSubject target = PackageSubject(OtherPackageId);
        InspectionGraphSubject duplicate = PackageSubject(PackageId, "feed-b");
        var request = new InspectionGraphInducedSetRequest(
            [first, target, duplicate],
            [TestRelationship],
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);

        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            request,
            [
                new InspectionGraphNode(
                    0,
                    first,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    1,
                    target,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    2,
                    duplicate,
                    InspectionGraphNodeRole.Ordinary,
                    []),
            ],
            [],
            [
                new InspectionGraphEdge(0, 0, 1, TestRelationship, []),
                new InspectionGraphEdge(1, 2, 1, TestRelationship, []),
            ],
            [],
            [],
            [],
            [
                new InspectionGraphLimit(
                    InspectionGraphInducedSetCatalog.SubjectBound,
                    Evidence:
                        new InspectionGraphInducedSubjectBoundEvidence(3)),
            ],
            []);
    }

    static InspectionGraphDocument GraphWithDuplicateTypeLabels()
    {
        InspectionGraphSubject firstPackage = PackageSubject(PackageId);
        InspectionGraphSubject secondPackage =
            PackageSubject(OtherPackageId);
        InspectionGraphSubject firstType =
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition(
                    "Assembly.One",
                    "Sample",
                    "Shared"));
        InspectionGraphSubject secondType =
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition(
                    "Assembly.Two",
                    "Sample",
                    "Shared"));
        var request = new InspectionGraphInducedSetRequest(
            [firstPackage, secondPackage],
            [TestTypeRelationship],
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);

        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            request,
            [
                new InspectionGraphNode(
                    0,
                    firstType,
                    InspectionGraphNodeRole.Ordinary,
                    [0]),
                new InspectionGraphNode(
                    1,
                    secondType,
                    InspectionGraphNodeRole.Ordinary,
                    [1]),
            ],
            [
                new InspectionGraphGroup(
                    0,
                    firstPackage,
                    parentId: null),
                new InspectionGraphGroup(
                    1,
                    secondPackage,
                    parentId: null),
            ],
            [
                new InspectionGraphEdge(
                    0,
                    0,
                    1,
                    TestTypeRelationship,
                    []),
            ],
            [],
            [],
            [],
            [
                new InspectionGraphLimit(
                    InspectionGraphInducedSetCatalog.SubjectBound,
                    Evidence:
                        new InspectionGraphInducedSubjectBoundEvidence(2)),
            ],
            []);
    }

    static InspectionGraphDocument GraphWithDuplicateAcquiredTypeLabels(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphInducedSetRequest _)
    {
        Assert.Equal(2, context.Group.Participants.Length);
        var name = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Sample", ["Shared"]))
            .Name;
        InspectionGraphSubject package = PackageSubject(PackageId);
        InspectionGraphSubject firstType =
            InspectionGraphSubject.ForAcquiredType(
                context.Group.Participants[0].Assembly.Registration,
                name);
        InspectionGraphSubject secondType =
            InspectionGraphSubject.ForAcquiredType(
                context.Group.Participants[1].Assembly.Registration,
                name);
        var request = new InspectionGraphInducedSetRequest(
            [package],
            [TestTypeRelationship],
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);

        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.SessionBound,
            request,
            [
                new InspectionGraphNode(
                    0,
                    firstType,
                    InspectionGraphNodeRole.Ordinary,
                    [0]),
                new InspectionGraphNode(
                    1,
                    secondType,
                    InspectionGraphNodeRole.Ordinary,
                    [0]),
            ],
            [
                new InspectionGraphGroup(0, package, parentId: null),
            ],
            [
                new InspectionGraphEdge(
                    0,
                    0,
                    1,
                    TestTypeRelationship,
                    []),
            ],
            [],
            [],
            [],
            [
                new InspectionGraphLimit(
                    InspectionGraphInducedSetCatalog.SubjectBound,
                    Evidence:
                        new InspectionGraphInducedSubjectBoundEvidence(1)),
            ],
            []);
    }

    static InspectionGraphDocument GraphWithDuplicateAcquiredTypeLabelFailures(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphInducedSetRequest request)
    {
        InspectionGraphDocument graph =
            GraphWithDuplicateAcquiredTypeLabels(context, request);
        return new InspectionGraphDocument(
            graph.Scope,
            graph.InducedSetRequest!,
            graph.Nodes,
            graph.Groups,
            graph.Edges,
            graph.Occurrences,
            graph.Characteristics,
            graph.Seeds,
            graph.Limits,
            [
                new InspectionGraphFailure(
                    InspectionGraphIntegrationsCatalog.ProjectionFailure,
                    InspectionGraphTarget.Node(0)),
                new InspectionGraphFailure(
                    InspectionGraphIntegrationsCatalog.ProjectionFailure,
                    InspectionGraphTarget.Node(1)),
            ]);
    }

    static InspectionGraphDocument EdgeFreeGraph()
    {
        InspectionGraphSubject first = PackageSubject(PackageId);
        InspectionGraphSubject second = PackageSubject(OtherPackageId);
        var request = new InspectionGraphInducedSetRequest(
            [first, second],
            [TestRelationship],
            InspectionGraphInducedSetAdmissionRule
                .BothEndpointsWithinSubjectClosure);

        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            request,
            [],
            [
                new InspectionGraphGroup(0, first, parentId: null),
                new InspectionGraphGroup(1, second, parentId: null),
            ],
            [],
            [],
            [],
            [],
            [
                new InspectionGraphLimit(
                    InspectionGraphInducedSetCatalog.SubjectBound,
                    Evidence:
                        new InspectionGraphInducedSubjectBoundEvidence(2)),
            ],
            []);
    }

    static InspectionGraphSubject PackageSubject(
        string packageId,
        string producer = "fixture") =>
        InspectionGraphSubject.ForRealizedPackage(
            new RealizedMemberCoordinate.Package(
                packageId,
                Version,
                producer,
                Framework,
                runtimeIdentifier: null));

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed record Execution(int ExitCode, string Output, string Error);
}

public static class InspectionGraphMissingPeerExtensions
{
    public static void MissingPeer(this HttpClient client)
    {
    }
}
