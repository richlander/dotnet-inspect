using System.Net;
using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
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
        Assert.Single(
            jsonLines.Output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
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
                    InspectionGraphIntegrationsCatalog.ProjectionFailure),
            ]);

        Execution execution = await ExecuteAsync(
            injectedDocument: incomplete);

        Assert.Equal(1, execution.ExitCode);
        Assert.Contains(PackageId, execution.Output, StringComparison.Ordinal);
        Assert.Contains(
            InspectionGraphIntegrationsCatalog.ProjectionFailure.Id,
            execution.Output,
            StringComparison.Ordinal);
        Assert.Contains("Integration graph is incomplete", execution.Error);
    }

    static async Task<Execution> ExecuteAsync(
        string[]? additionalArguments = null,
        string[]? relationships = null,
        InspectionGraphDocument? injectedDocument = null,
        Action<InspectionGraphInducedSetRequest>? captureRequest = null)
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(InspectionGraphCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{Framework}/dotnet-inspect.Tests.dll", assembly));
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
            Format = format,
            Count = arguments.Contains("--count"),
            Tree = arguments.Contains("--tree"),
            Rows = rowsIndex >= 0 ? RowWindow.Head(1) : null,
        };

        var captured = await ConsoleCapture.RunAsync(
            () => InspectionGraphCommand.ExecuteAsync(
                options,
                loadOptions,
                TestContext.Current.CancellationToken,
                queryExecutor: injectedDocument is null
                    ? null
                    : (_, request) =>
                    {
                        captureRequest?.Invoke(request);
                        return injectedDocument;
                    }));
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
                    []),
                new InspectionGraphNode(
                    1,
                    second,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    2,
                    third,
                    InspectionGraphNodeRole.Ordinary,
                    []),
            ],
            [],
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
