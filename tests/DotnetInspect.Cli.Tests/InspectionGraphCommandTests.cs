using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

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
    public void LibrariesCommand_ExposesExactPairWithoutTraversal()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "libraries",
                "--library",
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                "--library",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            ]);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name == "--where");
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name is "--depth"
                or "--direction"
                or "--relationship"
                or "--cluster");
    }

    [Fact]
    public void ClusterCommand_ExposesFocusedPairWithoutTraversal()
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [
                "graph",
                "cluster",
                "3",
                "--library",
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                "--library",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            ]);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name == "--where");
        Assert.DoesNotContain(
            result.CommandResult.Command.Options,
            option => option.Name is "--depth"
                or "--direction"
                or "--relationship");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--json");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--columns");
        Assert.Contains(
            result.CommandResult.Command.Options,
            option => option.Name == "--fields");
        Assert.Single(result.CommandResult.Command.Arguments);
    }

    [Fact]
    public void ClusterCommand_LowersCanonicalGraphLibrariesIntent()
    {
        GraphLibrariesQueryPlan plan =
            GraphLibrariesQuery.CreatePlan(3);

        Assert.Equal(3, plan.Cluster);
        Assert.Equal(
            PortableQueryPayloadCodec.Encode(
                GraphLibrariesQuery.CreateIntent(3),
                TestContext.Current.CancellationToken),
            PortableQueryPayloadCodec.Encode(
                plan.Intent,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LibrariesCommand_RequiresExactlyTwoLibraries()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Exactly two --library values are required.",
            captured.Error);
        Assert.DoesNotContain("Exception", captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_RequiresExactlyTwoLibraries()
    {
        var captured = await RunCliAsync(
            "graph",
            "cluster",
            "1",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Exactly two --library values are required.",
            captured.Error);
        Assert.DoesNotContain("Exception", captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_DiscoversProjectionSchemasWithoutLibraries()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "-D",
                        "--schema",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("Consumer Use Sites", captured.Output);
        Assert.Contains("Provider API Types", captured.Output);
        Assert.Contains("Direct Use Clusters", captured.Output);
        Assert.Contains("Call Sites", captured.Output);
        Assert.Contains("Public Root Paths", captured.Output);
        Assert.Contains(SectionCategoryNames.Libraries, captured.Output);
        Assert.DoesNotContain("@All", captured.Output);
        Assert.DoesNotContain("@Default", captured.Output);
        Assert.DoesNotContain("@Hidden", captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_DiscoversProjectionSchemasWithoutOrdinal()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "-D",
                        "--schema",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("Direct Use Clusters", captured.Output);
        Assert.Contains("Call Sites", captured.Output);
        Assert.Contains("Public Root Paths", captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_DiscoversAuthoredCatalogInAlphabeticalOrder()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    CommandLineBuilder.PreprocessArgs(
                        [
                            "graph",
                            "libraries",
                            "-D",
                        ]))
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        int category =
            captured.Output.IndexOf(
                SectionCategoryNames.Libraries,
                StringComparison.Ordinal);
        int callSites =
            captured.Output.IndexOf(
                LibraryCallUseSections.CallSites,
                StringComparison.Ordinal);
        int consumerUseSites =
            captured.Output.IndexOf(
                LibraryCallUseSections.ConsumerUseSites,
                StringComparison.Ordinal);
        int directUseClusters =
            captured.Output.IndexOf(
                LibraryCallUseSections.DirectUseClusters,
                StringComparison.Ordinal);
        int providerApiTypes =
            captured.Output.IndexOf(
                LibraryCallUseSections.ProviderApiTypes,
                StringComparison.Ordinal);

        Assert.True(category >= 0);
        Assert.True(category < callSites);
        Assert.True(callSites < consumerUseSites);
        Assert.True(consumerUseSites < directUseClusters);
        Assert.True(directUseClusters < providerApiTypes);
        Assert.DoesNotContain(
            LibraryCallUseSections.PublicRootPaths,
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_RequiresClusterForPublicRootPathsBeforeAcquisition()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        "missing-consumer.dll",
                        "--library",
                        "missing-provider.dll",
                        "-S",
                        "Public Root Paths",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "'Public Root Paths' requires "
                + "'dotnet-inspect graph cluster <ordinal>'.",
            captured.Error);
        Assert.DoesNotContain("Library not found", captured.Error);
    }

    [Theory]
    [InlineData("Public*", "No sections match 'Public*'.")]
    [InlineData("Public*,Bogus", "Select value 'Bogus' not found.")]
    public async Task ClusterCommand_WildcardDoesNotSelectPublicRootPaths(
        string selector,
        string expectedError)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "14",
                        "--library",
                        "missing-consumer.dll",
                        "--library",
                        "missing-provider.dll",
                        "-S",
                        selector,
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(expectedError, captured.Error);
        Assert.DoesNotContain("Warning:", captured.Error);
        Assert.DoesNotContain("Library not found", captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_ProjectsPublicRootPaths()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "14",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "Public Root Paths",
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document =
            JsonDocument.Parse(captured.Output);
        JsonElement paths =
            document.RootElement.GetProperty(
                "public_root_paths");
        Assert.Equal(3, paths.GetArrayLength());
        Assert.Equal(
            ["Create", "CreateAlternative", "CreateOuter"],
            paths.EnumerateArray()
                .Select(path =>
                    path.GetProperty("public_root")
                        .GetString()!
                        .Split('(')[0]
                        .Split('.')[^1]));
        Assert.Equal(
            ["2", "2", "3"],
            paths.EnumerateArray()
                .Select(path =>
                    path.GetProperty("depth").GetString()));
        Assert.All(
            paths.EnumerateArray(),
            path =>
            {
                Assert.Contains(
                    "AddChange",
                    path.GetProperty("direct_use_destination")
                        .GetString());
                Assert.Contains(
                    " -> ",
                    path.GetProperty("method_path").GetString());
                Assert.Contains(
                    "+0x",
                    path.GetProperty("physical_receipts")
                        .GetString());
            });
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_PublicRootPathsDefaultOutputShowsPathEvidence()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "14",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "Public Root Paths",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(
            "| Source Library | Cluster | Public Root | Public Root Token "
                + "| Direct Use Destination | Destination Token | Depth "
                + "| Method Path | Physical Receipts |",
            captured.Output);
        Assert.Contains(
            "Shared.RootPathEntry.Create()",
            captured.Output);
        Assert.Contains(
            "Shared.RootPathEntry.AddChange()",
            captured.Output);
        Assert.Contains(
            "Shared.RootPathEntry.CreateMappedTextDiff()",
            captured.Output);
        Assert.Contains(
            "1:0x06000011+0x0000-&gt;0x06000014",
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_PublicRootPathsRespectAccessorVisibility()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "15",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "Public Root Paths",
                        "--jsonl",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("get_Value", captured.Output);
        Assert.Contains("AssignValue", captured.Output);
        Assert.Contains("set_Value", captured.Output);
        Assert.DoesNotContain(
            "\"public_root\":\"Shared.RootPathEntry.set_Value",
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_PublicRootPathsReportCompleteEmptyResult()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "16",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "Public Root Paths",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(
            "No selected public root has a local static path "
                + "to this cluster's consumer use sites.",
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public void LibrariesCommand_ProjectionSchemaComesFromGeneratedViewContext()
    {
        DocumentSchema schema = LibraryCallUseViewContext.Default
            .GetSchemaInfo<LibraryCallUseSelectedView>()!
            .ToDocumentSchema();

        Assert.Equal(
            [
                LibraryCallUseCommand.ConsumerUseSitesSection,
                LibraryCallUseCommand.ProviderApiTypesSection,
                LibraryCallUseCommand.DirectUseClustersSection,
                LibraryCallUseCommand.CallSitesSection,
                LibraryCallUseCommand.PublicRootPathsSection,
            ],
            schema.SectionNames);
        Assert.Equal(
            [
                "source_library",
                "source_mvid",
                "source_member",
                "source_token",
                "target_library",
                "target_mvid",
                "provider_types",
                "target_members",
                "call_sites",
                "call_site_rows",
            ],
            schema.GetSection(
                    LibraryCallUseCommand.ConsumerUseSitesSection)!
                .Items
                .Select(item => item.Key));
        Assert.Equal(
            [
                "source_library",
                "source_mvid",
                "target_library",
                "target_mvid",
                "cluster",
                "derivation",
                "anchor_source_token",
                "anchor_target_token",
                "source_members",
                "provider_types",
                "target_members",
                "extension_methods",
                "call_sites",
                "call_site_rows",
            ],
            schema.GetSection(
                    LibraryCallUseCommand.DirectUseClustersSection)!
                .Items
                .Select(item => item.Key));
        Assert.Equal(
            "IL Offset",
            schema.GetSection(LibraryCallUseCommand.CallSitesSection)!
                .Items
                .Single(item => item.Key == "il_offset")
                .Name);
        Assert.Equal(
            [
                "source_library",
                "cluster",
                "public_root",
                "public_root_token",
                "direct_use_destination",
                "destination_token",
                "depth",
                "method_path",
                "physical_receipts",
            ],
            schema.GetSection(
                    LibraryCallUseCommand.PublicRootPathsSection)!
                .Items
                .Select(item => item.Key));
    }

    [Fact]
    public async Task LibrariesCommand_BareSelectProjectsBothSummarySections()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document =
            JsonDocument.Parse(captured.Output);
        JsonElement consumerUseSites =
            document.RootElement.GetProperty("consumer_use_sites");
        JsonElement providerApiTypes =
            document.RootElement.GetProperty("provider_api_types");
        Assert.False(
            document.RootElement.TryGetProperty(
                "direct_use_clusters",
                out _));
        Assert.False(
            document.RootElement.TryGetProperty(
                "public_root_paths",
                out _));
        Assert.Equal(19, consumerUseSites.GetArrayLength());
        Assert.Equal(10, providerApiTypes.GetArrayLength());
        Assert.Equal(
            "Shared.Entry.Run()",
            consumerUseSites[0]
                .GetProperty("source_member")
                .GetString());
        Assert.Equal(
            "Target.Api",
            providerApiTypes[0]
                .GetProperty("target_type")
                .GetString());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_LibrariesCategoryComposesAlphabetically()
    {
        string[] arguments = CommandLineBuilder.PreprocessArgs(
            [
                "graph",
                "libraries",
                "--library",
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                "--library",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
                "-S",
                SectionCategoryNames.Libraries,
                "--json",
            ]);
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(arguments)
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            [
                "call_sites",
                "consumer_use_sites",
                "direct_use_clusters",
                "provider_api_types",
            ],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.False(
            document.RootElement.TryGetProperty(
                "public_root_paths",
                out _));
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_DefaultsToDirectUseClusters()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "--jsonl",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        JsonElement[] clusters =
        [
            .. captured.Output
                .ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        JsonElement echo = Assert.Single(
            clusters,
            cluster =>
                cluster.GetProperty("source_members").GetString() == "2"
                && cluster.GetProperty("target_members").GetString() == "1"
                && cluster.GetProperty("extension_methods").GetString() == "1"
                && cluster.GetProperty("call_sites").GetString() == "3");
        Assert.Equal(
            "exact-bipartite-connected-component",
            echo.GetProperty("derivation").GetString());
        Assert.False(
            string.IsNullOrEmpty(
                echo.GetProperty("call_site_rows").GetString()));
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_DefaultsToExactCalls()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "3",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "--jsonl",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        JsonElement[] calls =
        [
            .. captured.Output
                .ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        Assert.Equal(3, calls.Length);
        Assert.Equal(
            ["RunTwice", "RunTwice", "UseEcho"],
            calls.Select(call =>
                call.GetProperty("source_member")
                    .GetString()!
                    .Split('(')[0]
                    .Split('.')[^1]));
        Assert.All(
            calls,
            call =>
            {
                Assert.Contains(
                    "Echo",
                    call.GetProperty("target_member").GetString());
                Assert.StartsWith(
                    "0x0600",
                    call.GetProperty("source_token").GetString());
                Assert.Equal(
                    "0x0600000B",
                    call.GetProperty("target_token").GetString());
            });
        Assert.Empty(captured.Error);

        var human = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "3",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                    ])
                .InvokeAsync());
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("# Direct Use Cluster 3", human.Output);
        Assert.Contains(
            "Footprint: 2 source members, 1 provider type, "
                + "1 target member, 1 extension method, "
                + "and 3 physical call sites.",
            human.Output);
        Assert.Contains(
            "| Source Member | Source Token | Target Member | Target Token | Call | Evidence Method | Evidence Token | IL Offset |",
            human.Output);
        Assert.Contains(
            "| Shared.Entry.RunTwice() | 0x06000004 | Target.GenericApi.Echo(T) | 0x0600000B | call | Shared.Entry.RunTwice() | 0x06000004 | 0x0001 |",
            human.Output);
        Assert.Empty(human.Error);
    }

    [Fact]
    public async Task ClusterCommand_UsesEvidenceTokenForIlHandoff()
    {
        string consumer =
            FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        string provider = Path.Combine(
            Path.GetDirectoryName(consumer)!,
            "ILInspector.Analysis.AsyncSiblingFriendBaseFixtures.dll");
        var cluster = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "1",
                        "--library",
                        consumer,
                        "--library",
                        provider,
                        "--jsonl",
                    ])
                .InvokeAsync());

        Assert.Equal(1, cluster.ExitCode);
        using JsonDocument document = JsonDocument.Parse(
            cluster.Output.Trim());
        JsonElement call = document.RootElement;
        string sourceToken =
            call.GetProperty("source_token").GetString()!;
        string evidenceToken =
            call.GetProperty("evidence_token").GetString()!;
        string ilOffset =
            call.GetProperty("il_offset").GetString()!;
        Assert.NotEqual(sourceToken, evidenceToken);
        Assert.Contains(
            "MoveNext",
            call.GetProperty("evidence_method").GetString());
        Assert.Contains(
            "Pairwise call-use evidence is incomplete.",
            cluster.Error);

        var location = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "library",
                        "coordinate",
                        $"{evidenceToken}+{ilOffset}",
                        "--library",
                        consumer,
                    ])
                .InvokeAsync());

        Assert.Equal(0, location.ExitCode);
        Assert.Contains("## Context: Callsite", location.Output);
        Assert.Contains("MoveNext", location.Output);
        Assert.Contains("callvirt", location.Output);
        Assert.Empty(location.Error);
    }

    [Fact]
    public async Task ClusterCommand_PublicRootPathsRetainPositiveIncompleteEvidence()
    {
        string consumer =
            FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        string provider = Path.Combine(
            Path.GetDirectoryName(consumer)!,
            "ILInspector.Analysis.AsyncSiblingFriendBaseFixtures.dll");
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "1",
                        "--library",
                        consumer,
                        "--library",
                        provider,
                        "-S",
                        "Public Root Paths",
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        using JsonDocument document =
            JsonDocument.Parse(captured.Output);
        JsonElement path = Assert.Single(
            document.RootElement
                .GetProperty("public_root_paths")
                .EnumerateArray());
        Assert.Equal("0", path.GetProperty("depth").GetString());
        Assert.Contains(
            "AnalyzeAsync",
            path.GetProperty("public_root").GetString());
        Assert.Contains(
            "Public root-path evidence is incomplete.",
            captured.Error);
        Assert.Contains(
            "Pairwise call-use evidence is incomplete",
            captured.Error);
        Assert.Contains(
            "Analysis reported 1 body diagnostics",
            captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_ScopesEverySelectedSection()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "3",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "*",
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document =
            JsonDocument.Parse(captured.Output);
        Assert.Equal(
            2,
            document.RootElement
                .GetProperty("consumer_use_sites")
                .GetArrayLength());
        Assert.Single(
            document.RootElement
                .GetProperty("provider_api_types")
                .EnumerateArray());
        JsonElement cluster = Assert.Single(
            document.RootElement
                .GetProperty("direct_use_clusters")
                .EnumerateArray());
        Assert.Equal(
            "3",
            cluster.GetProperty("cluster").GetString());
        Assert.Equal(
            "1,2,3",
            cluster.GetProperty("call_site_rows").GetString());
        Assert.Equal(
            3,
            document.RootElement
                .GetProperty("call_sites")
                .GetArrayLength());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_RejectsUnavailableCluster()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        "99",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Direct Use Cluster 99 does not exist.",
            captured.Error);
        Assert.Contains(
            "Observed pair-wide cluster ordinals: 1..17.",
            captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_RequiresOrdinalForExecution()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "A positive Direct-Use Cluster ordinal is required.",
            captured.Error);
        Assert.Contains(
            "dotnet-inspect graph cluster --help",
            captured.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task ClusterCommand_RejectsNonPositiveOrdinal(
        string ordinal)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "cluster",
                        ordinal,
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "The Direct-Use Cluster ordinal must be positive.",
            captured.Error);
    }

    [Fact]
    public void ClusterCommand_RejectsInvalidOrdinal()
    {
        var result =
            CommandLineBuilder.CreateRootCommand()
                .Parse(["graph", "cluster", "not-an-ordinal"]);

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "not-an-ordinal",
                StringComparison.Ordinal));
    }

    [Fact]
    public void LibrariesCommand_RejectsRetiredWhereOption()
    {
        var result =
            CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--where",
                        "Cluster=3",
                    ]);

        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "--where",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibrariesCommand_SummaryRowsRetainCompleteGroupCounts()
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            "Consumer Use Sites",
            "--jsonl",
            "--rows",
            "2..3");

        Assert.Equal(0, captured.ExitCode);
        string[] lines = captured.Output.ReplaceLineEndings("\n").Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using JsonDocument first = JsonDocument.Parse(lines[0]);
        using JsonDocument second = JsonDocument.Parse(lines[1]);
        Assert.Equal(
            "Shared.Entry.RunAcrossBoundary()",
            first.RootElement
                .GetProperty("source_member")
                .GetString());
        Assert.Equal(
            "Shared.Entry.RunTwice()",
            second.RootElement
                .GetProperty("source_member")
                .GetString());
        Assert.Equal(
            "2",
            second.RootElement
                .GetProperty("call_sites")
                .GetString());
        Assert.Equal(
            "3,4",
            second.RootElement
                .GetProperty("call_site_rows")
                .GetString());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_ProviderTypeSpellingPreservesGenericArity()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "-S",
                        "Provider API Types",
                        "--jsonl",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        string[] targetTypes = captured.Output
            .ReplaceLineEndings("\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using JsonDocument row =
                    JsonDocument.Parse(line);
                return row.RootElement
                    .GetProperty("target_type")
                    .GetString()!;
            })
            .ToArray();
        Assert.Contains("Target.Box`1", targetTypes);
        Assert.Contains("Target.Box`2", targetTypes);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_RejectsRawMultiSectionBeforeAcquisition()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "-S",
                        "--table",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "--table requires exactly one selected table",
            captured.Error);
        Assert.DoesNotContain(
            "Exactly two --library values are required.",
            captured.Error);
        Assert.Empty(captured.Output);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    public async Task LibrariesCommand_RejectsSeparatorOnlySelection(string selection)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "-S",
                        selection,
                        "--json",
                    ])
                .InvokeAsync());

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "--select requires at least one name.",
            captured.Error);
        Assert.DoesNotContain(
            "Exactly two --library values are required.",
            captured.Error);
        Assert.DoesNotContain(
            "Exception",
            captured.Error);
        Assert.Empty(captured.Output);
    }

    [Fact]
    public async Task LibrariesCommand_CountsSelectedSummaryRows()
    {
        async Task<(int ExitCode, string Output, string Error)> Execute(
            params string[] projection) =>
            await RunCliAsync(
                [
                    "graph",
                    "libraries",
                    "--library",
                    FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                    "--library",
                    FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
                    .. projection,
                    "--count",
                ]);

        var single = await Execute(
            "-S",
            "Consumer Use Sites",
            "--rows",
            "1..2");
        var multiple = await Execute("-S", "--json");

        Assert.Equal(0, single.ExitCode);
        Assert.Equal("2", single.Output.Trim());
        Assert.Empty(single.Error);
        Assert.Equal(0, multiple.ExitCode);
        using JsonDocument counts =
            JsonDocument.Parse(multiple.Output);
        Assert.Equal(
            19,
            counts.RootElement[0]
                .GetProperty("count")
                .GetInt32());
        Assert.Equal(
            10,
            counts.RootElement[1]
                .GetProperty("count")
                .GetInt32());
        Assert.Empty(multiple.Error);
    }

    [Fact]
    public async Task LibrariesCommand_DogfoodsProductMarkoutUse()
    {
        string directory = Path.GetDirectoryName(
            typeof(InspectionGraphCommandTests)
                .Assembly.Location)!;
        string markout = Path.Combine(
            directory,
            "Markout.dll");

        async Task<(
            int UseSites,
            int ProviderTypes,
            int DirectUseClusters,
            int CallSites)> Counts(
            string consumer)
        {
            var captured = await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.CreateRootCommand()
                    .Parse(
                        [
                            "graph",
                            "libraries",
                            "--library",
                            Path.Combine(directory, consumer),
                            "--library",
                            markout,
                            "-S",
                            "*",
                            "--count",
                            "--json",
                        ])
                    .InvokeAsync());

            Assert.Equal(0, captured.ExitCode);
            Assert.Empty(captured.Error);
            using JsonDocument document =
                JsonDocument.Parse(captured.Output);
            Dictionary<string, int> counts = document.RootElement
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("section").GetString()!,
                    row => row.GetProperty("count").GetInt32(),
                    StringComparer.Ordinal);
            return (
                counts[LibraryCallUseSections.ConsumerUseSites],
                counts[LibraryCallUseSections.ProviderApiTypes],
                counts[LibraryCallUseSections.DirectUseClusters],
                counts[LibraryCallUseSections.CallSites]);
        }

        var presentation =
            await Counts("DotnetInspector.Presentation.dll");
        Assert.Equal(
            (38, 12, 8, 1258),
            presentation);

        var metadataRendering =
            await Counts("DotnetInspector.MetadataRendering.dll");
        Assert.Equal(
            (13, 4, 1, 72),
            metadataRendering);
    }

    [Fact]
    public async Task LibrariesCommand_CountsDirectUseClustersByDefault()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.CreateRootCommand()
                .Parse(
                    [
                        "graph",
                        "libraries",
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath(),
                        "--library",
                        FixtureCatalog.AnalysisCallerGraphTarget
                            .AssemblyPath(),
                        "--count",
                    ])
                .InvokeAsync());

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal("17", captured.Output.Trim());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_CountsExactCallSitesByDefault()
    {
        var captured = await RunCliAsync(
            "graph",
            "cluster",
            "3",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "--count");

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal("3", captured.Output.Trim());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_AppliesOccurrenceRowWindowsToJsonLines()
    {
        var captured = await RunCliAsync(
            "graph",
            "cluster",
            "3",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "--jsonl",
            "--rows",
            "1..2");

        Assert.Equal(0, captured.ExitCode);
        string[] lines = captured.Output
            .ReplaceLineEndings("\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(
            lines,
            line =>
            {
                using JsonDocument row = JsonDocument.Parse(line);
                Assert.True(
                    row.RootElement.TryGetProperty(
                        "source_member",
                        out _));
                Assert.True(
                    row.RootElement.TryGetProperty(
                        "target_member",
                        out _));
                Assert.Contains(
                    "Version=",
                    row.RootElement
                        .GetProperty("source_library")
                        .GetString(),
                    StringComparison.Ordinal);
                Assert.NotEqual(
                    Guid.Empty,
                    Guid.Parse(
                        row.RootElement
                            .GetProperty("source_mvid")
                            .GetString()!));
                Assert.StartsWith(
                    "0x06",
                    row.RootElement
                        .GetProperty("source_token")
                        .GetString(),
                    StringComparison.Ordinal);
                Assert.NotEqual(
                    Guid.Empty,
                    Guid.Parse(
                        row.RootElement
                            .GetProperty("target_mvid")
                            .GetString()!));
                Assert.StartsWith(
                    "0x06",
                    row.RootElement
                        .GetProperty("target_token")
                        .GetString(),
                    StringComparison.Ordinal);
                Assert.NotEqual(
                    Guid.Empty,
                    Guid.Parse(
                        row.RootElement
                            .GetProperty("evidence_mvid")
                            .GetString()!));
                Assert.StartsWith(
                    "0x06",
                    row.RootElement
                        .GetProperty("evidence_token")
                        .GetString(),
                    StringComparison.Ordinal);
                Assert.Equal(
                    "yes",
                    row.RootElement
                        .GetProperty("exact_target")
                        .GetString());
            });
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task ClusterCommand_SemanticTailSelectsTheSameCallSiteAcrossFormats()
    {
        string[] pair =
        [
            "graph",
            "cluster",
            "3",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
        ];
        const string columns =
            "Source Member;Target Member;Evidence Token;IL Offset";
        var complete = await RunCliAsync(
            [.. pair, "--jsonl", "--columns", columns]);
        JsonElement expected;
        using (var document = JsonDocument.Parse(
            complete.Output
                .ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Last()))
        {
            expected = document.RootElement.Clone();
        }
        string evidenceToken =
            expected.GetProperty("evidence_token").GetString()!;
        string ilOffset =
            expected.GetProperty("il_offset").GetString()!;

        string[] selection =
        [
            .. pair,
            "-n",
            "1",
            "--tail",
            "--columns",
            columns,
        ];
        var markdown = await RunCliAsync(selection);
        var table = await RunCliAsync([.. selection, "--table"]);
        var tsv = await RunCliAsync(
            [.. selection, "--tsv", "--no-headers"]);
        var jsonl = await RunCliAsync([.. selection, "--jsonl"]);
        var selectedJsonl = await RunCliAsync(
            [
                .. selection,
                "-S",
                LibraryCallUseSections.CallSites,
                "--jsonl",
            ]);
        var json = await RunCliAsync([.. selection, "--json"]);
        var count = await RunCliAsync([.. selection, "--count"]);
        var jsonCount = await RunCliAsync(
            [.. selection, "--count", "--json"]);

        foreach (var result in new[]
        {
            complete,
            markdown,
            table,
            tsv,
            jsonl,
            selectedJsonl,
            json,
            count,
            jsonCount,
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
        }

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            selectedJsonl,
            json,
        })
        {
            Assert.Contains(
                evidenceToken,
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                ilOffset,
                result.Output,
                StringComparison.Ordinal);
        }

        Assert.Single(
            tsv.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            selectedJsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using var jsonDocument = JsonDocument.Parse(json.Output);
        JsonProperty section =
            Assert.Single(jsonDocument.RootElement.EnumerateObject());
        Assert.Single(section.Value.EnumerateArray());
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal("1", jsonCount.Output.Trim());
    }

    [Fact]
    public void LibrariesCommand_SemanticSectionsResolveOneTypedDeclaration()
    {
        (string Section, string RowSet)[] expected =
        [
            (
                LibraryCallUseSections.ConsumerUseSites,
                GraphLibrariesQuery.ConsumerUseSitesRowSet),
            (
                LibraryCallUseSections.ProviderApiTypes,
                GraphLibrariesQuery.ProviderApiTypesRowSet),
            (
                LibraryCallUseSections.DirectUseClusters,
                GraphLibrariesQuery.DirectUseClustersRowSet),
            (
                LibraryCallUseSections.CallSites,
                GraphLibrariesQuery.CallSitesRowSet),
        ];

        Assert.Equal(
            expected.Length,
            LibraryCallUseSections.SemanticRowDeclarations.Count);
        foreach ((string section, string rowSet) in expected)
        {
            Assert.True(
                LibraryCallUseSections.TryGetSemanticRows(
                    [section],
                    out LibraryCallUseSections.SemanticRowDeclaration?
                        declaration));
            Assert.NotNull(declaration);
            Assert.Equal(section, declaration.Section);
            Assert.Equal(rowSet, declaration.Rows.RowSet);
        }

        Assert.True(
            LibraryCallUseSections.TryGetSemanticRows(
                selectedSections: null,
                out LibraryCallUseSections.SemanticRowDeclaration?
                    defaultDeclaration));
        Assert.Same(
            GraphLibrariesSectionRows.DirectUseClusters,
            defaultDeclaration!.Rows);
        Assert.False(
            LibraryCallUseSections.TryGetSemanticRows(
                [LibraryCallUseSections.PublicRootPaths],
                out _));
        Assert.False(
            LibraryCallUseSections.TryGetSemanticRows(
                [
                    LibraryCallUseSections.CallSites,
                    LibraryCallUseSections.ConsumerUseSites,
                ],
                out _));
    }

    [Fact]
    public async Task LibrariesCommand_SemanticTailSelectsTheSameClusterAcrossFormats()
    {
        string[] pair =
        [
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            LibraryCallUseSections.DirectUseClusters,
        ];
        const string columns =
            "Cluster;Anchor Source Token;Anchor Target Token";
        var complete = await RunCliAsync(
            [.. pair, "--jsonl", "--columns", columns]);
        JsonElement expected;
        using (var document = JsonDocument.Parse(
            complete.Output
                .ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Last()))
        {
            expected = document.RootElement.Clone();
        }
        string sourceToken =
            expected.GetProperty("anchor_source_token").GetString()!;
        string targetToken =
            expected.GetProperty("anchor_target_token").GetString()!;

        string[] selection =
        [
            .. pair,
            "-n",
            "1",
            "--tail",
            "--columns",
            columns,
        ];
        var markdown = await RunCliAsync(selection);
        var table = await RunCliAsync([.. selection, "--table"]);
        var tsv = await RunCliAsync(
            [.. selection, "--tsv", "--no-headers"]);
        var jsonl = await RunCliAsync([.. selection, "--jsonl"]);
        var json = await RunCliAsync([.. selection, "--json"]);
        var count = await RunCliAsync([.. selection, "--count"]);
        var jsonCount = await RunCliAsync(
            [.. selection, "--count", "--json"]);

        foreach (var result in new[]
        {
            complete,
            markdown,
            table,
            tsv,
            jsonl,
            json,
            count,
            jsonCount,
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
        }

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
        })
        {
            Assert.Contains(
                sourceToken,
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                targetToken,
                result.Output,
                StringComparison.Ordinal);
        }

        Assert.Single(
            tsv.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using var jsonDocument = JsonDocument.Parse(json.Output);
        JsonProperty section =
            Assert.Single(jsonDocument.RootElement.EnumerateObject());
        Assert.Single(section.Value.EnumerateArray());
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal("1", jsonCount.Output.Trim());
    }

    [Theory]
    [InlineData("Consumer Use Sites", "Source Member", "source_member")]
    [InlineData("Provider API Types", "Target Type", "target_type")]
    public async Task LibrariesCommand_SemanticTailSelectsTheSameSummaryAcrossFormats(
        string section,
        string identityColumn,
        string identityProperty)
    {
        string[] pair =
        [
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            section,
        ];
        string columns = $"{identityColumn};Call Site Rows";
        var complete = await RunCliAsync(
            [.. pair, "--jsonl", "--columns", columns]);
        JsonElement expected;
        using (var document = JsonDocument.Parse(
            complete.Output
                .ReplaceLineEndings("\n")
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Last()))
        {
            expected = document.RootElement.Clone();
        }
        string identity =
            expected.GetProperty(identityProperty).GetString()!;
        string callSiteRows =
            expected.GetProperty("call_site_rows").GetString()!;

        string[] selection =
        [
            .. pair,
            "-n",
            "1",
            "--tail",
            "--columns",
            columns,
        ];
        var markdown = await RunCliAsync(selection);
        var table = await RunCliAsync([.. selection, "--table"]);
        var tsv = await RunCliAsync(
            [.. selection, "--tsv", "--no-headers"]);
        var jsonl = await RunCliAsync([.. selection, "--jsonl"]);
        var json = await RunCliAsync([.. selection, "--json"]);
        var count = await RunCliAsync([.. selection, "--count"]);
        var jsonCount = await RunCliAsync(
            [.. selection, "--count", "--json"]);

        foreach (var result in new[]
        {
            complete,
            markdown,
            table,
            tsv,
            jsonl,
            json,
            count,
            jsonCount,
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
        }

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
        })
        {
            Assert.Contains(
                identity,
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                callSiteRows,
                result.Output,
                StringComparison.Ordinal);
        }

        Assert.Single(
            tsv.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using var jsonDocument = JsonDocument.Parse(json.Output);
        JsonProperty selectedSection =
            Assert.Single(jsonDocument.RootElement.EnumerateObject());
        Assert.Single(selectedSection.Value.EnumerateArray());
        Assert.Equal("1", count.Output.Trim());
        Assert.Equal("1", jsonCount.Output.Trim());
    }

    [Fact]
    public async Task LibrariesCommand_StrictUnavailableDefaultWindowWithholdsOutput()
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "--rows",
            "999..1000",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Library direct-use cluster row selection stage 1 requires "
                + "cluster 1000, but only ",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Direct Use Clusters")]
    [InlineData("Direct Use Clusters;Direct Use Clusters")]
    public async Task LibrariesCommand_StrictUnavailableClusterWindowWithholdsOutput(
        string sectionSelection)
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            sectionSelection,
            "--rows",
            "999..1000",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Library direct-use cluster row selection stage 1 requires cluster "
                + "1000, but only ",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Consumer Use Sites",
        "Library consumer-use-site row selection stage 1 requires use site "
            + "1000, but only ")]
    [InlineData(
        "Provider API Types",
        "Library provider-API-type row selection stage 1 requires type "
            + "1000, but only ")]
    public async Task LibrariesCommand_StrictUnavailableSummaryWindowWithholdsOutput(
        string section,
        string expectedError)
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            section,
            "--rows",
            "999..1000",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            expectedError,
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Consumer Use Sites")]
    [InlineData("Provider API Types")]
    public async Task LibrariesCommand_SemanticSummarySelectionPreservesIncompleteEvidence(
        string section)
    {
        string consumer =
            FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        string provider = Path.Combine(
            Path.GetDirectoryName(consumer)!,
            "ILInspector.Analysis.AsyncSiblingFriendBaseFixtures.dll");
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            consumer,
            "--library",
            provider,
            "-S",
            section,
            "-n",
            "1",
            "--jsonl");

        Assert.Equal(1, captured.ExitCode);
        Assert.Single(
            captured.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            "Pairwise call-use evidence is incomplete.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibrariesCommand_SemanticClusterSelectionPreservesIncompleteEvidence()
    {
        string consumer =
            FixtureCatalog.AnalysisAsyncSiblingFriend.AssemblyPath();
        string provider = Path.Combine(
            Path.GetDirectoryName(consumer)!,
            "ILInspector.Analysis.AsyncSiblingFriendBaseFixtures.dll");
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            consumer,
            "--library",
            provider,
            "-S",
            LibraryCallUseSections.DirectUseClusters,
            "-n",
            "1",
            "--jsonl");

        Assert.Equal(1, captured.ExitCode);
        Assert.Single(
            captured.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            "Pairwise call-use evidence is incomplete.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClusterCommand_ClusterScopePrecedesSemanticWindow()
    {
        var captured = await RunCliAsync(
            "graph",
            "cluster",
            "3",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            LibraryCallUseSections.DirectUseClusters,
            "--rows",
            "2..2",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Library direct-use cluster row selection stage 1 requires cluster "
                + "2, but only 1 direct-use clusters are available.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClusterCommand_SummaryGroupingAndClusterScopePrecedeSemanticWindow()
    {
        var captured = await RunCliAsync(
            "graph",
            "cluster",
            "3",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            "-S",
            LibraryCallUseSections.ConsumerUseSites,
            "-n",
            "1",
            "--jsonl");

        Assert.Equal(0, captured.ExitCode);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        Assert.Equal(
            "Shared.Entry.RunTwice()",
            document.RootElement
                .GetProperty("source_member")
                .GetString());
        Assert.Equal(
            "2",
            document.RootElement
                .GetProperty("call_sites")
                .GetString());
        Assert.Equal(
            "1,2",
            document.RootElement
                .GetProperty("call_site_rows")
                .GetString());
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task LibrariesCommand_ContainsAssemblyNamesBeforeComposingDescriptions()
    {
        const string injectedHeading = "# INJECTED HEADING";
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-graph-libraries-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string hostilePath = Path.Combine(directory, "Hostile.dll");
            string safePath = Path.Combine(directory, "Safe.dll");
            WriteNamedAssembly(
                hostilePath,
                $"Hostile\n{injectedHeading}");
            WriteNamedAssembly(safePath, "Safe");

            async Task<(int ExitCode, string Output, string Error)> Execute(
                params string[] selection) =>
                await ConsoleCapture.RunAsync(
                    () => CommandLineBuilder.CreateRootCommand()
                        .Parse(
                            [
                                "graph",
                                "libraries",
                                "--library",
                                hostilePath,
                                "--library",
                                safePath,
                                .. selection,
                            ])
                        .InvokeAsync());

            var defaultView = await Execute();
            var selectedView = await Execute(
                "-S",
                LibraryCallUseSections.DirectUseClusters);

            Assert.Equal(0, defaultView.ExitCode);
            Assert.Equal(0, selectedView.ExitCode);
            Assert.DoesNotContain(
                $"\n{injectedHeading}",
                defaultView.Output.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"\n{injectedHeading}",
                selectedView.Output.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
            Assert.Contains(
                "Hostile # INJECTED HEADING@",
                defaultView.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Hostile # INJECTED HEADING@",
                selectedView.Output,
                StringComparison.Ordinal);
            Assert.Empty(defaultView.Error);
            Assert.Empty(selectedView.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LibrariesCommand_ReportsPairRelevantVersionSkew()
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "--library",
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            "--library",
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath(),
            "-n",
            "1");

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains(
            "Pairwise call-use evidence is incomplete.",
            captured.Error);
        Assert.Contains(
            "call sites name the other library",
            captured.Error);
        Assert.DoesNotContain("Exception", captured.Error);
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

    [Theory]
    [InlineData("..1")]
    [InlineData("2..")]
    public async Task IntegrationsCommand_AcceptsSemanticOpenWindows(
        string window)
    {
        var captured = await RunCliAsync(
            "graph",
            "integrations",
            "--rows",
            window);

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "At least one --package is required.",
            captured.Error);
        Assert.DoesNotContain(
            "has no start row",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntegrationsCommand_RejectsLegacyCountRows()
    {
        var captured = await RunCliAsync(
            "graph",
            "integrations",
            "--rows",
            "1");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "--rows requires N..M, N.., or ..M with positive positions.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "At least one --package is required.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Consumer Use Sites")]
    [InlineData("Provider API Types")]
    [InlineData("Direct Use Clusters")]
    public async Task LibrariesCommand_RejectsLegacyCountRows(string? section)
    {
        var args = new List<string>
        {
            "graph",
            "libraries",
        };
        if (section is not null)
        {
            args.Add("-S");
            args.Add(section);
        }
        args.Add("--rows");
        args.Add("1");

        var captured = await RunCliAsync([.. args]);

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "--rows requires N..M, N.., or ..M with positive positions.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Exactly two --library values are required.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntegrationsCommand_LinesRejectJsonBeforeRequiredInputs()
    {
        var captured = await RunCliAsync(
            "graph",
            "integrations",
            "-n",
            "1",
            "--lines",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "At least one --package is required.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-n", "1")]
    [InlineData("-1", null)]
    public async Task IntegrationsCommand_HeadAllowsCompleteJsonBeforeRequiredInputs(
        string gesture,
        string? value)
    {
        var args = new List<string>
        {
            "graph",
            "integrations",
            gesture,
        };
        if (value is not null)
            args.Add(value);
        args.Add("--json");

        var captured = await RunCliAsync([.. args]);

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "At least one --package is required.",
            captured.Error);
        Assert.DoesNotContain(
            "Rendered-line selection cannot be combined with JSON output.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Call Sites")]
    [InlineData("Consumer Use Sites")]
    [InlineData("Direct Use Clusters")]
    [InlineData("Provider API Types")]
    public async Task LibrariesCommand_HeadAllowsCompleteJsonBeforeRequiredInputs(
        string? section)
    {
        var args = new List<string>
        {
            "graph",
            "libraries",
            "-n",
            "1",
            "--json",
        };
        if (section is not null)
        {
            args.Add("-S");
            args.Add(section);
        }

        var captured = await RunCliAsync([.. args]);

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Exactly two --library values are required.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Rendered-line selection cannot be combined with JSON output.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibrariesCommand_ClusterMultiSectionRetainsRenderedLineFallback()
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "-S",
            "Call Sites,Direct Use Clusters",
            "-n",
            "1",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Exactly two --library values are required.",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibrariesCommand_BareSummaryViewRetainsRenderedLineFallback()
    {
        var captured = await RunCliAsync(
            "graph",
            "libraries",
            "-S",
            "-n",
            "1",
            "--json");

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            captured.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Exactly two --library values are required.",
            captured.Error,
            StringComparison.Ordinal);
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
    public async Task SemanticTail_SelectsTheSameLogicalEdgeAcrossFormats()
    {
        InspectionGraphDocument document = GraphWithTwoEdges();
        RowSelectionIntent<string> tail = Select(
            RowSelectionIntentOperation<string>.Tail(1));

        Execution markdown = await ExecuteAsync(
            injectedDocument: document,
            rowSelection: tail);
        Execution table = await ExecuteAsync(
            ["--table"],
            injectedDocument: document,
            rowSelection: tail);
        Execution json = await ExecuteAsync(
            ["--json"],
            injectedDocument: document,
            rowSelection: tail);
        Execution jsonLines = await ExecuteAsync(
            ["--jsonl"],
            injectedDocument: document,
            rowSelection: tail);
        Execution count = await ExecuteAsync(
            ["--count"],
            injectedDocument: document,
            rowSelection: tail);

        Assert.All(
            [markdown, table, json, jsonLines, count],
            static execution => Assert.Equal(0, execution.ExitCode));
        Assert.Equal(
            1,
            MarkdownTableTestOracle.CountRows(markdown.Output));
        Assert.Contains(ThirdPackageId, markdown.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(PackageId, table.Output, StringComparison.Ordinal);
        Assert.Contains(OtherPackageId, table.Output, StringComparison.Ordinal);
        Assert.Contains(ThirdPackageId, table.Output, StringComparison.Ordinal);
        Assert.Equal("1", count.Output.Trim());

        using JsonDocument parsed = JsonDocument.Parse(json.Output);
        JsonElement edge = Assert.Single(
            parsed.RootElement.GetProperty("edges").EnumerateArray());
        Assert.Equal(
            $"{OtherPackageId}@{Version}",
            edge.GetProperty("source").GetString());
        Assert.Equal(
            $"{ThirdPackageId}@{Version}",
            edge.GetProperty("target").GetString());

        string jsonLineText = Assert.Single(
            jsonLines.Output.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        using JsonDocument jsonLine = JsonDocument.Parse(jsonLineText);
        Assert.Equal(
            $"{OtherPackageId}@{Version}",
            jsonLine.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            $"{ThirdPackageId}@{Version}",
            jsonLine.RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task SemanticUnavailableWindow_WithholdsGraph()
    {
        Execution execution = await ExecuteAsync(
            injectedDocument: GraphWithTwoEdges(),
            rowSelection: Select(
                RowSelectionIntentOperation<string>.Window(2, 3)));

        Assert.Equal(1, execution.ExitCode);
        Assert.Empty(execution.Output);
        Assert.Contains(
            "Integration graph row selection stage 1 requires edge 3, "
                + "but only 2 edges are available.",
            execution.Error,
            StringComparison.Ordinal);
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
        Assert.Contains("DotnetInspect.Cli.Tests", table.Output);
        Assert.Contains("dotnet-inspect, Version=", table.Output);
        Assert.Contains("DotnetInspect.Cli.Tests", tree.Output);
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
        Assert.Contains("DotnetInspect.Cli.Tests, Version=", failures);
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
                "DotnetInspect.Cli.Tests, Version=",
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
            rowSelection: Select(
                RowSelectionIntentOperation<string>.Head(1)));

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
        RowSelectionIntent<string>? rowSelection = null,
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
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
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
            RowSelection = rowSelection,
            Rows = rowSelection is null
                ? rows ?? (rowsIndex >= 0 ? RowWindow.Head(1) : null)
                : null,
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

    static Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });

    static RowSelectionIntent<string> Select(
        params RowSelectionIntentOperation<string>[] operations) =>
        RowSelectionIntent<string>.Create(operations);

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

    static void WriteNamedAssembly(
        string path,
        string name)
    {
        var assemblyName = new System.Reflection.AssemblyName(name)
        {
            Version = new Version(1, 0, 0, 0),
        };
        var assembly = new System.Reflection.Emit.PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GraphLibraries");
        module.DefineType(
                "GraphLibraries.Sample",
                System.Reflection.TypeAttributes.Public
                    | System.Reflection.TypeAttributes.Class)
            .CreateType();
        assembly.Save(path);
    }

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
