using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Tests for FindCommand output formatting via the shared table pipeline.
/// </summary>
public class FindCommandTests
{
    private static readonly PackageSourceResultFactory TestResults =
        CreateResultFactory();

    [Fact]
    public void TableFormatter_MultiPattern_OutputsCanonicalTsvRows()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Pattern1", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Zebra", Namespace = "Animals", Kind = "class", Library = "Zoo", Source = "runtime" },
            new() { Pattern = "Pattern1", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Alpha", Namespace = "Greek", Kind = "struct", Library = "Letters", Source = "runtime" },
            new() { Pattern = "Pattern2", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Beta", Namespace = "Greek", Kind = "interface", Library = "Letters", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        // The table pipeline pins LF (see StringBuilderLineExtensions), so rows are
        // LF-separated on every platform rather than the ambient Environment.NewLine.
        var lines = RenderFindTable(view, tsv: true, showHeader: false).TrimEnd().Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal("Pattern1\tZebra\tAnimals\tclass\tZoo\truntime", lines[0]);
        Assert.Equal("Pattern1\tAlpha\tGreek\tstruct\tLetters\truntime", lines[1]);
        Assert.Equal("Pattern2\tBeta\tGreek\tinterface\tLetters\truntime", lines[2]);
    }

    [Fact]
    public void TableFormatter_VisiblyEncodesTabsAndNewlinesInTsvCells()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Pattern\t1", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Line\nBreak", Namespace = "Ns\r\nValue", Kind = "class", Library = "Tab\tLib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var fields = RenderFindTable(view, tsv: true, showHeader: false).TrimEnd().Split('\t');

        Assert.Equal(
            [@"Line\^JBreak", @"Ns\^M\^JValue", "class", @"Tab\^ILib", "runtime"],
            fields);
    }

    [Fact]
    public void Views_CarryConcernProvenanceAcrossMarkoutFormats()
    {
        const string hostile = "Name\u202E\n";
        TextConcern expectedConcerns = TextConcern.Format | TextConcern.Control;
        var view = FindOutputFormatter.BuildView(
            [
                new TypeFindResult
                {
                    Pattern = hostile,
                    Match = MatchKind.Exact,
                    Similarity = 1.0,
                    Type = hostile,
                    Namespace = hostile,
                    Kind = hostile,
                    Library = hostile,
                    Source = hostile,
                    SourceVersion = hostile,
                },
            ],
            hostile);
        var memberView = FindOutputFormatter.BuildMemberView(
            [
                new MemberFindResult
                {
                    Pattern = hostile,
                    Match = MatchKind.Exact,
                    Member = hostile,
                    Kind = hostile,
                    DeclaringType = hostile,
                    Signature = hostile,
                    Library = hostile,
                    Source = hostile,
                    SourceVersion = hostile,
                },
            ],
            hostile);

        FindRow row = Assert.Single(view.Results!);
        FindMemberRow memberRow = Assert.Single(memberView.Results!);
        Assert.Equal(expectedConcerns, view.TitleText.Concerns);
        Assert.Equal(expectedConcerns, row.TypeText.Concerns);
        Assert.Equal(expectedConcerns, row.SourceText.Concerns);
        Assert.Equal(expectedConcerns, memberView.TitleText.Concerns);
        Assert.Equal(expectedConcerns, memberRow.SignatureText.Concerns);
        Assert.Equal(@"Name\u202E\^J", row.Type);

        string markdown = MarkoutSerializer.Serialize(view, SearchViewContext.Default);
        string tsv = RenderFindTable(view, tsv: true, showHeader: false);
        string jsonl = RenderFindTable(view, tsv: false, showHeader: false, jsonl: true);
        string json = OutputFormatter.RenderProjectedJson(
            columns: null,
            fields: null,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions));

        Assert.DoesNotContain("\u202E", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", tsv, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", jsonl, StringComparison.Ordinal);
        Assert.Contains(@"\u202E", markdown, StringComparison.Ordinal);
        Assert.Contains(@"\^J", markdown, StringComparison.Ordinal);
        Assert.Contains(@"\u202E", tsv, StringComparison.Ordinal);
        Assert.Contains(@"\^J", tsv, StringComparison.Ordinal);

        using var jsonlDocument = System.Text.Json.JsonDocument.Parse(jsonl);
        Assert.Equal(
            @"Name\u202E\^J",
            jsonlDocument.RootElement.GetProperty("type").GetString());

        using var document = System.Text.Json.JsonDocument.Parse(json);
        string? jsonType = document.RootElement
            .GetProperty("results")[0]
            .GetProperty("type")
            .GetString();
        Assert.Equal(@"Name\u202E\^J", jsonType);
    }

    [Fact]
    public void TableFormatter_EmptyResults_NoOutput()
    {
        var view = FindOutputFormatter.BuildView([]);
        var output = RenderFindTable(view, tsv: true, showHeader: false);

        // TableFormatter doesn't support paragraphs (no IBlockFormatter),
        // so the description is not rendered
        Assert.Equal("", output.TrimEnd());
    }

    [Fact]
    public void BuildView_WithNotFoundPatterns_IncludesNotFoundRows()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Found*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "FoundType", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" },
            new() { Pattern = "Missing1", Match = MatchKind.NotFound },
            new() { Pattern = "Missing2", Match = MatchKind.NotFound }
        };

        var view = FindOutputFormatter.BuildView(results);

        Assert.NotNull(view.Results);
        Assert.Equal(3, view.Results.Count);
        Assert.Equal("notfound", view.Results[1].Match);
        Assert.Equal("notfound", view.Results[2].Match);
        Assert.Equal("-", view.Results[1].Type);
    }

    [Fact]
    public void BuildView_AllPatternsNotFound_NullsResultsWithDescription()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Bad1", Match = MatchKind.NotFound },
            new() { Pattern = "Bad2", Match = MatchKind.NotFound },
            new() { Pattern = "Bad3", Match = MatchKind.NotFound }
        };

        var view = FindOutputFormatter.BuildView(results);

        Assert.Null(view.Results);
        Assert.Equal(0, view.Matches);
        Assert.Equal("No types found matching the pattern.", view.Description);
    }

    [Fact]
    public void TableFormatter_WithHeader_IncludesColumnHeaders()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Test*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "TestA", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var output = RenderFindTable(view, tsv: true, showHeader: true);

        Assert.Contains("type\tnamespace\tkind\tlibrary\tsource", output);
        Assert.Contains("TestA", output);
    }

    [Fact]
    public void TableFormatter_NoHeader_OmitsColumnHeaders()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Test*", Match = MatchKind.Glob, Similarity = 1.0,
                     Type = "TestA", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var output = RenderFindTable(view, tsv: true, showHeader: false);

        Assert.DoesNotContain("Type\tNamespace", output);
        Assert.Contains("TestA", output);
    }

    [Fact]
    public void PrettyTableFormatter_AlignsCanonicalTsvProjection()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "A", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "Short", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" },
            new() { Pattern = "A", Match = MatchKind.Exact, Similarity = 1.0,
                     Type = "LongerType", Namespace = "Ns", Kind = "class", Library = "Lib", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var output = RenderFindTable(view, tsv: false, showHeader: true);

        Assert.DoesNotContain('\t', output);
        Assert.Contains("Type        Namespace", output);
        Assert.Contains("Short", output);
        Assert.Contains("LongerType", output);
    }

    [Fact]
    public void TableFormatter_ColumnsAcceptStableTsvHeaderKeys()
    {
        var results = new List<TypeFindResult>
        {
            new() { Pattern = "Json", Match = MatchKind.Partial, Similarity = 0.50,
                     Type = "IsLong", Namespace = "System.Runtime.CompilerServices", Kind = "class", Library = "VisualC", Source = "runtime" }
        };

        var view = FindOutputFormatter.BuildView(results);
        var output = RenderFindTable(view, tsv: true, showHeader: true, columns: ["type", "similarity"]);

        Assert.Equal("type\tsimilarity\nIsLong\t0.50\n", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void PackageProfileSection_BindsProfileQueryAndProjectsOnePackageRow()
    {
        PackageProfileSectionCatalog catalog =
            PackageProfileSections.CreateCatalog();
        SectionPipeline<PackageProfileView> pipeline =
            catalog.Pipeline;
        (string Name, InspectionQueryDefinition Query) binding =
            Assert.Single(pipeline.QueryBoundSections);
        Assert.Equal(PackageProfileSections.Packages, binding.Name);
        Assert.Same(PackageProfileQuery.Definition, binding.Query);
        Assert.Same(
            PackageProfileQuery.Definition,
            Assert.Single(catalog.QueryCatalog.RegisteredQueries));

        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            [
                new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        "Contoso.Package",
                        "1.2.3",
                        ["Contoso"],
                        42,
                        true,
                        TestResults.Source,
                        ManifestFacts(
                            "Contoso.Package",
                            "1.2.3",
                            "Contoso",
                            [
                                new DeclaredPackageDependencyGroup(
                                    "net8.0",
                                    [
                                        new DeclaredPackageDependency(
                                            "Third.Party",
                                            "[2.0.0, 3.0.0)"),
                                    ]),
                            ]))),
                new PackageProfileEvent.Completed(
                    new PackageProfileSummary(
                        "Contoso.",
                        TestResults.Source,
                        Candidates: 1,
                        Matches: 1,
                        Failures: 0,
                        PackageSearchTruncationReason.None)),
            ]);

        string tsv = RenderPackageProfileTable(
            view,
            tsv: true,
            showHeader: true);
        string[] tsvLines = tsv.ReplaceLineEndings("\n")
            .TrimEnd()
            .Split('\n');
        Assert.StartsWith("package\tversion\t", tsvLines[0]);
        Assert.StartsWith(
            "Contoso.Package\t1.2.3\t",
            tsvLines[1]);

        string jsonl = RenderPackageProfileTable(
            view,
            tsv: false,
            showHeader: false,
            jsonl: true);
        using JsonDocument jsonlDocument = JsonDocument.Parse(jsonl);
        Assert.Equal(
            "Contoso.Package",
            jsonlDocument.RootElement.GetProperty("package").GetString());

        string json = OutputFormatter.RenderProjectedJson(
            columns: null,
            fields: null,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions));
        using JsonDocument jsonDocument = JsonDocument.Parse(json);
        Assert.Equal(
            "Contoso.Package",
            jsonDocument.RootElement
                .GetProperty("packages")[0]
                .GetProperty("package")
                .GetString());

        string markdown = MarkoutSerializer.Serialize(
            view,
            SearchViewContext.Default);
        Assert.Contains("| Contoso.Package | 1.2.3 |", markdown);
    }

    [Fact]
    public void PackageProfileSection_KeepsFailuresAndTruncationAsContext()
    {
        PackageProfileEvent[] events =
        [
            new PackageProfileEvent.Failure(
                new PackageProfileFailure(
                    "Contoso.Broken",
                    "1.0.0",
                    TestResults.Source,
                    PackageProfileFailureKind.InvalidManifest,
                    "The manifest was invalid.",
                    PackageManifestFailureReason.IdentityMismatch)),
            new PackageProfileEvent.Completed(
                new PackageProfileSummary(
                    "Contoso.",
                    TestResults.Source,
                    Candidates: 1,
                    Matches: 0,
                    Failures: 1,
                    PackageSearchTruncationReason.SourcePageLimit)),
        ];
        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            events);

        Assert.Null(view.Results);
        Assert.Equal(1, view.Failures);
        Assert.True(view.Truncated);
        Assert.Equal(0, PackageProfileSections.CountRows(view));
    }

    [Fact]
    public void PackageProfileSelection_ComposesOverPackagesAndPreservesContext()
    {
        PackageProfileEvent.Match Match(int index, int dependencyCount) =>
            new(
                new PackageProfileMatch(
                    $"Contoso.{index}",
                    "1.0.0",
                    ["Contoso"],
                    index,
                    true,
                    TestResults.Source,
                    ManifestFacts(
                        $"Contoso.{index}",
                        "1.0.0",
                        "Contoso",
                        [
                            new DeclaredPackageDependencyGroup(
                                "net8.0",
                                [
                                    .. Enumerable.Range(0, dependencyCount)
                                        .Select(dependency =>
                                            new DeclaredPackageDependency(
                                                $"Dependency.{index}.{dependency}",
                                                "1.0.0")),
                                ]),
                        ])));

        PackageProfileEvent.Failure failure =
            new(
                new PackageProfileFailure(
                    "Contoso.Broken",
                    "1.0.0",
                    TestResults.Source,
                    PackageProfileFailureKind.InvalidManifest,
                    "The manifest was invalid.",
                    PackageManifestFailureReason.IdentityMismatch));
        PackageProfileEvent.Completed completed =
            new(
                new PackageProfileSummary(
                    "Contoso.",
                    TestResults.Source,
                    Candidates: 5,
                    Matches: 4,
                    Failures: 1,
                    PackageSearchTruncationReason.RequestedLimit));
        PackageProfileEvent[] events =
        [
            Match(1, 1),
            failure,
            Match(2, 100),
            Match(3, 0),
            Match(4, 10),
            completed,
        ];
        var options = new FindOptions
        {
            RowSelection = RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Window(2, 4),
                    RowSelectionIntentOperation<string>.Head(2),
                ]),
        };

        Assert.True(
            FindCommand.TrySelectRowsPreservingContext(
                options.RowSelection,
                events,
                static profileEvent =>
                    profileEvent is PackageProfileEvent.Match,
                "package",
                out IReadOnlyList<PackageProfileEvent> selectedEvents,
                out int availableRows));

        Assert.Equal(4, availableRows);
        Assert.Equal(
            ["Contoso.2", "Contoso.3"],
            selectedEvents
                .OfType<PackageProfileEvent.Match>()
                .Select(match => match.Value.PackageId));
        Assert.Contains(failure, selectedEvents);
        Assert.Contains(completed, selectedEvents);
        PackageProfileView view =
            PackageProfileSections.CreateDocument(
                "Contoso.",
                selectedEvents);
        Assert.Equal(
            ["Contoso.2", "Contoso.3"],
            view.Results!.Select(row => row.Package));
    }

    [Fact]
    public async Task
        PackageProfileCatalog_MaterializesOnceAndForwardsOperationContext()
    {
        var source = new CountingPackageSource();
        using var operationContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4),
            TestContext.Current.CancellationToken);
        PackageProfileSectionCatalog catalog =
            PackageProfileSections.CreateCatalog();

        InspectionQueryResults results =
            await catalog.Lens
                .Plan(
                    Verbosity.Normal,
                    [PackageProfileSections.Packages])
                .RunAsync(
                    new PackageProfileQueryContext(
                        source,
                        new PackagePrefixProfileRequest("Contoso."),
                        operationContext),
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        Assert.Equal(1, source.SearchRequests);
        Assert.Same(operationContext, source.SearchOperationContext);
        ImmutableArray<PackageProfileEvent> first =
            results.Get(PackageProfileQuery.Definition);
        ImmutableArray<PackageProfileEvent> second =
            results.Get(PackageProfileQuery.Definition);
        Assert.Equal(first, second);
        Assert.Equal(1, source.SearchRequests);
        Assert.IsType<PackageProfileEvent.Failure>(first[0]);
        Assert.IsType<PackageProfileEvent.Completed>(first[1]);
    }

    [Fact]
    public async Task
        PackageProfileDefaultScale_AcquiresEachManifestOnceAndBoundsProjectedRows()
    {
        const int candidateCount = 100;
        const int dependenciesPerManifest = 64;
        const int projectedRowLimit = 25;
        var source = new DefaultScalePackageSource(
            candidateCount,
            dependenciesPerManifest);
        PackageProfileSectionCatalog catalog =
            PackageProfileSections.CreateCatalog();

        InspectionQueryResults results =
            await catalog.Lens
                .Plan(
                    Verbosity.Normal,
                    [PackageProfileSections.Packages])
                .RunAsync(
                new PackageProfileQueryContext(
                    source,
                    new PackagePrefixProfileRequest("Contoso.")),
                cancellationToken:
                    TestContext.Current.CancellationToken);
        ImmutableArray<PackageProfileEvent> events =
            results.Get(PackageProfileQuery.Definition);
        PackageProfileView view =
            PackageProfileSections.CreateDocument(
                "Contoso.",
                events,
                RowWindow.Head(projectedRowLimit));
        ImmutableArray<PackageProfileEvent> secondRead =
            results.Get(PackageProfileQuery.Definition);

        Assert.Equal(
            [
                (
                    Prefix: "Contoso.",
                    Take: candidateCount,
                    Prerelease: false),
            ],
            source.SearchRequests);
        Assert.Equal(
            source.CandidateCoordinates,
            source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);
        Assert.Equal(events, secondRead);
        Assert.Equal(
            candidateCount,
            events.Count(profileEvent =>
                profileEvent is PackageProfileEvent.Match));
        Assert.Equal(
            projectedRowLimit,
            PackageProfileSections.CountRows(view));
        Assert.Equal(projectedRowLimit, view.Results!.Count);
    }

    [Fact]
    public void PackageProfileSection_ReusesContainedPackageCells()
    {
        const int dependencyCount = 1000;
        string authors = new('\u202e', 25_000);
        PackageProfileEvent[] events =
        [
            new PackageProfileEvent.Match(
                new PackageProfileMatch(
                    "Contoso.Package",
                    "1.0.0",
                    ["Contoso"],
                    42,
                    true,
                    TestResults.Source,
                    ManifestFacts(
                        "Contoso.Package",
                        "1.0.0",
                        authors,
                        [
                            new DeclaredPackageDependencyGroup(
                                "net8.0",
                                [
                                    .. Enumerable.Range(
                                        0,
                                        dependencyCount)
                                        .Select(i =>
                                            new DeclaredPackageDependency(
                                                $"Dependency.{i}",
                                                "1.0.0")),
                                ]),
                        ]))),
        ];

        _ = PackageProfileSections.CreateDocument(
            "Warmup.",
            events,
            RowWindow.Head(1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        PackageProfileView view =
            PackageProfileSections.CreateDocument("Contoso.", events);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        PackageProfileRow row = Assert.Single(view.Results!);
        Assert.NotEmpty(row.Authors);
        Assert.True(
            allocated < 5_000_000,
            $"Expected shared package cells; allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void PackageProfileSection_AppliesRowWindowBeforeProjection()
    {
        const int dependencyCount = 1000;
        PackageProfileEvent[] events =
        [
            new PackageProfileEvent.Match(
                new PackageProfileMatch(
                    "Contoso.Package",
                    "1.0.0",
                    ["Contoso"],
                    42,
                    true,
                    TestResults.Source,
                    ManifestFacts(
                        "Contoso.Package",
                        "1.0.0",
                        new string('\u202e', 25_000),
                        [
                            new DeclaredPackageDependencyGroup(
                                "net8.0",
                                [
                                    .. Enumerable.Range(
                                        0,
                                        dependencyCount)
                                        .Select(i =>
                                            new DeclaredPackageDependency(
                                                $"Dependency.{i}",
                                                "1.0.0")),
                                ]),
                        ]))),
        ];

        _ = PackageProfileSections.CreateDocument(
            "Warmup.",
            events,
            RowWindow.Head(1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        PackageProfileView view =
            PackageProfileSections.CreateDocument(
                "Contoso.",
                events,
                RowWindow.Head(5));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        PackageProfileRow row = Assert.Single(view.Results!);
        Assert.Equal("Contoso.Package", row.Package);
        Assert.True(
            allocated < 2_000_000,
            $"Expected windowed projection; allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void PackageProfileSection_WindowedRowsMatchEveryFormat()
    {
        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            [
                new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        "Contoso.Package",
                        "1.0.0",
                        ["Contoso"],
                        42,
                        true,
                        TestResults.Source,
                        ManifestFacts(
                            "Contoso.Package",
                            "1.0.0",
                            "Contoso",
                            [
                                new DeclaredPackageDependencyGroup(
                                    "net8.0",
                                    [
                                        new DeclaredPackageDependency(
                                            "Dependency.Zero",
                                            "1.0.0"),
                                        new DeclaredPackageDependency(
                                            "Dependency.One",
                                            "1.0.0"),
                                        new DeclaredPackageDependency(
                                            "Dependency.Two",
                                            "1.0.0"),
                                    ]),
                            ]))),
            ],
            RowWindow.Head(2));

        string markdown = MarkoutSerializer.Serialize(
            view,
            SearchViewContext.Default);
        string tsv = RenderPackageProfileTable(
            view,
            tsv: true,
            showHeader: true);
        string json = OutputFormatter.RenderProjectedJson(
            columns: null,
            fields: null,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions));

        foreach (string output in new[] { markdown, tsv, json })
        {
            Assert.Contains("Contoso.Package", output);
            Assert.DoesNotContain("Dependency.Zero", output);
            Assert.DoesNotContain("Dependency.Two", output);
        }
    }

    [Fact]
    public void PackageProfileSection_EmptyWindowIsNotAnEmptyProfile()
    {
        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            [
                new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        "Contoso.Package",
                        "1.0.0",
                        ["Contoso"],
                        42,
                        true,
                        TestResults.Source,
                        ManifestFacts(
                            "Contoso.Package",
                            "1.0.0",
                            "Contoso",
                            []))),
            ],
            RowWindow.Range(10, end: null));

        Assert.NotNull(view.Results);
        Assert.Empty(view.Results);
        Assert.Null(view.Description);
        string markdown = MarkoutSerializer.Serialize(
            view,
            SearchViewContext.Default);
        Assert.Equal(
            "# Find packages: Contoso.",
            markdown.ReplaceLineEndings("\n").TrimEnd());
        Assert.DoesNotContain("No packages found.", markdown);
    }

    [Theory]
    [InlineData(
        0,
        PackageSearchTruncationReason.None,
        0)]
    [InlineData(
        0,
        PackageSearchTruncationReason.RequestedLimit,
        0)]
    [InlineData(
        0,
        PackageSearchTruncationReason.SourcePageLimit,
        1)]
    [InlineData(
        1,
        PackageSearchTruncationReason.None,
        1)]
    public void PackageProfileExitCode_DistinguishesExpectedAndIncompleteLimits(
        int failures,
        PackageSearchTruncationReason truncationReason,
        int expected)
    {
        var summary = new PackageProfileSummary(
            "Contoso.",
            TestResults.Source,
            Candidates: 1,
            Matches: failures == 0 ? 1 : 0,
            failures,
            truncationReason);

        Assert.Equal(
            expected,
            FindCommand.PackageProfileExitCode(summary));
    }

    [Fact]
    public void PackageProfileSection_ContainsHostileCellsAcrossFormats()
    {
        const string hostileOwner = "Own\u202E\nINJECTEDOWNER";
        const string hostileAuthor = "Auth\tINJECTEDAUTHOR";
        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            [
                new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        "Contoso.Package",
                        "1.0.0",
                        [hostileOwner],
                        0,
                        false,
                        TestResults.Source,
                        ManifestFacts(
                            "Contoso.Package",
                            "1.0.0",
                            hostileAuthor,
                            []))),
                new PackageProfileEvent.Completed(
                    new PackageProfileSummary(
                        "Contoso.",
                        TestResults.Source,
                        Candidates: 1,
                        Matches: 1,
                        Failures: 0,
                        PackageSearchTruncationReason.None)),
            ]);

        string markdown = MarkoutSerializer.Serialize(
            view,
            SearchViewContext.Default);
        string tsv = RenderPackageProfileTable(
            view,
            tsv: true,
            showHeader: false);
        string jsonl = RenderPackageProfileTable(
            view,
            tsv: false,
            showHeader: false,
            jsonl: true);
        string json = OutputFormatter.RenderProjectedJson(
            columns: null,
            fields: null,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions));

        foreach ((string channel, string output) in new[]
        {
            ("package-profile-markdown", markdown),
            ("package-profile-tsv", tsv),
            ("package-profile-jsonl", jsonl),
            ("package-profile-json", json),
        })
        {
            HostileOutputAssert.MarkersRendered(
                output,
                channel,
                "INJECTEDOWNER",
                "INJECTEDAUTHOR");
            HostileOutputAssert.NoRenderingHazard(output, channel);
            HostileOutputAssert.NoLineSplit(
                output,
                "INJECTEDOWNER",
                "INJECTEDAUTHOR");
        }
    }

    private static PackageManifestFacts ManifestFacts(
        string packageId,
        string version,
        string? authors,
        DeclaredPackageDependencyGroup[] dependencyGroups) =>
        new(
            PackageSourceCoordinate.Create(packageId, version),
            ManifestVersion: "nuspec",
            Description: null,
            authors,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: [],
            IsToolPackage: false,
            ReadmeFile: null,
            dependencyGroups.ToImmutableArray());

    private static string RenderFindTable(
        FindResultView view,
        bool tsv,
        bool showHeader,
        string[]? columns = null,
        bool jsonl = false) =>
        OutputFormatter.RenderTable(showHeader,
            (writer, formatter) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                SearchViewContext.Default,
                OutputFormatter.ConfigureTableWriterOptions(
                    new MarkoutWriterOptions
                    {
                        Projection = OutputFormatter.BuildProjection(columns)
                    },
                    tsv,
                    jsonl)));

    private static string RenderPackageProfileTable(
        PackageProfileView view,
        bool tsv,
        bool showHeader,
        bool jsonl = false) =>
        OutputFormatter.RenderTable(
            showHeader,
            (writer, formatter) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                SearchViewContext.Default,
                OutputFormatter.ConfigureTableWriterOptions(
                    new MarkoutWriterOptions(),
                    tsv,
                    jsonl)));

    private static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
        return captured
            ?? throw new InvalidOperationException(
                "The test result factory was not supplied.");
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class CountingPackageSource : IPackageSourceClient
    {
        public int SearchRequests { get; private set; }
        public NuGetOperationContext? SearchOperationContext
        {
            get;
            private set;
        }
        public PackageSourceResultIdentity Source => TestResults.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchRequests++;
            SearchOperationContext = operationContext;
            return Task.FromResult<
                PackageSourceOperationResult<PackageSearchResult>>(
                    TestResults.FailedSearch(
                        PackageSourceFailureKind.Transport));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class DefaultScalePackageSource(
        int candidateCount,
        int dependenciesPerManifest)
        : IPackageSourceClient
    {
        public PackageSourceCoordinate[] CandidateCoordinates { get; } =
        [
            .. Enumerable.Range(0, candidateCount)
                .Select(index =>
                    PackageSourceCoordinate.Create(
                        $"Contoso.Package{index:D3}",
                        "1.0.0")),
        ];
        public List<(string Prefix, int Take, bool Prerelease)>
            SearchRequests
        { get; } = [];
        public List<PackageSourceCoordinate> ManifestRequests { get; } = [];
        public int PackageRequests { get; private set; }
        public PackageSourceResultIdentity Source => TestResults.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchRequests.Add((prefix, take, prerelease));
            SearchResult[] matches =
            [
                .. CandidateCoordinates.Select(coordinate =>
                    new SearchResult(
                        coordinate.PackageId,
                        coordinate.Version)),
            ];
            return Task.FromResult<
                PackageSourceOperationResult<PackageSearchResult>>(
                    TestResults.SucceededSearch(
                        TestResults.Search(
                            matches,
                            PackageSearchTruncationReason.None)));
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            if (!CandidateCoordinates.Contains(coordinate))
            {
                throw new InvalidOperationException(
                    "The query requested a coordinate outside the search result.");
            }
            if (ManifestRequests.Contains(coordinate))
            {
                throw new InvalidOperationException(
                    "The query requested one manifest more than once.");
            }

            ManifestRequests.Add(coordinate);
            string dependencies = string.Concat(
                Enumerable.Range(0, dependenciesPerManifest)
                    .Select(index =>
                        $"""<dependency id="Dependency.{index:D3}" version="1.0.0" />"""));
            byte[] content = Encoding.UTF8.GetBytes(
                $$"""
                <package>
                  <metadata>
                    <id>{{coordinate.PackageId}}</id>
                    <version>{{coordinate.Version}}</version>
                    <dependencies>{{dependencies}}</dependencies>
                  </metadata>
                </package>
                """);
            return Task.FromResult<
                PackageSourceOperationResult<PackageSourceManifest>>(
                    TestResults.SucceededManifest(
                        coordinate,
                        TestResults.Manifest(coordinate, content)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PackageRequests++;
            throw new NotSupportedException();
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Integration tests for FindCommand across platform frameworks.
/// Tests FQN/UQN matching, framework coverage, and type resolution.
/// </summary>
[Collection("Console")]
public class FindCommandIntegrationTests
{
    public FindCommandIntegrationTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Theory]
    [InlineData("Azure", "0", "--take requires a positive whole number.")]
    [InlineData("Azure", "1001", "--take must be between 1 and 1000.")]
    [InlineData("Azure ", "500", "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.")]
    public void PackageProfileInvalidInput_UsesComposedDiagnostic(
        string prefix,
        string take,
        string expected)
    {
        var (exit, output, error) = RunCli(
            ["find", "--package-prefix", prefix, "--take", take]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
        Assert.DoesNotContain("Arg_", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ArgumentOutOfRange_",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageProfileLimits_UseMeasuredDefaultAndMaximum()
    {
        Assert.Equal(500, FindCommand.PackageProfileDefaultLimit);
        Assert.Equal(1_000, FindCommand.PackageProfileMaximumLimit);
    }

    [Fact]
    public void PackageProfileAllFlag_FailsBeforeNetwork()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure.",
                "--all",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot be combined with API search scopes, --all, or --tfm",
            error);
        Assert.DoesNotContain("Attempted:", error);
    }

    [Fact]
    public void PackageProfileTypeFilter_FailsBeforeNetwork()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure.",
                "--type",
                "*Json*",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Package Profile does not support --type",
            error);
        Assert.DoesNotContain("Attempted:", error);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("2147483648")]
    public void PackageProfileInvalidRawLimit_FailsBeforeNetwork(
        string limit)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                "--take",
                limit,
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--take requires a positive whole number", error);
        Assert.DoesNotContain("Attempted:", error);
        Assert.DoesNotContain("Arg_", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ArgumentOutOfRange_",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageProfileSeparatedNegativeTake_RemainsProfileInput()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                "--take",
                "-5",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--take requires a positive whole number",
            error);
        Assert.DoesNotContain("Attempted:", error);
    }

    [Theory]
    [InlineData(
        "--rows", "bad", "--take", "nope",
        "--rows requires N..M, N.., or ..M with positive positions.")]
    [InlineData(
        "--take", "nope", "--rows", "bad",
        "--take requires a positive whole number.")]
    public void PackageProfileMalformedSelectionAndTake_ReportFirstAuthoredValue(
        string firstOption,
        string firstValue,
        string secondOption,
        string secondValue,
        string expected)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                firstOption,
                firstValue,
                secondOption,
                secondValue,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void PackageProfileBareHead_PreservesExecutionBoundDiagnosticPosition()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                "-1",
                "--take=nope",
                "--rows=bad",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--take requires a positive whole number.",
            error);
        Assert.DoesNotContain(
            "--rows requires",
            error);
    }

    [Fact]
    public void PackageProfileRepeatedTake_UsesComposedConflictDiagnostic()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                "--take",
                "1",
                "--take",
                "2",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--take may only be specified once.", error);
        Assert.DoesNotContain(
            "expects a single argument",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageProfileRepeatedTake_PrecedesMissingCountRegardlessOfOrder()
    {
        string[][] arguments =
        [
            [
                "find",
                "--package-prefix",
                "Azure",
                "--head",
                "--take",
                "1",
                "--take",
                "2",
            ],
            [
                "find",
                "--package-prefix",
                "Azure",
                "--take",
                "1",
                "--take",
                "2",
                "--head",
            ],
        ];

        foreach (string[] args in arguments)
        {
            var (exit, output, error) = RunCli(args);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--take may only be specified once.", error);
            Assert.DoesNotContain("--head requires -n.", error);
        }
    }

    [Fact]
    public void PackageProfileMissingRepeatedTakeValue_PrecedesMalformedRows()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--rows",
                "bad",
                "--take",
                "--take",
                "1",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--take requires a value.", error);
        Assert.DoesNotContain("--rows requires", error);
    }

    [Theory]
    [InlineData("--head=true")]
    [InlineData("--unknown")]
    public void PackageProfileMissingTakeValue_PrecedesLaterTokenArityFailure(
        string laterFailure)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--take",
                "--take",
                "1",
                laterFailure,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--take requires a value.", error);
    }

    [Fact]
    public void PackageProfileMissingTakeValue_PrecedesDuplicatedPositionalFailure()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--take",
                "--take",
                "1",
                "Contoso.",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--take requires a value.", error);
    }

    [Fact]
    public void PackageProfileEarlierTokenArityFailure_PrecedesMissingTakeValue()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--head=true",
                "--take",
                "--take",
                "1",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--head does not accept a value.", error);
        Assert.DoesNotContain("--take requires a value.", error);
    }

    [Fact]
    public void EarlierUnknownOption_PrecedesLaterModifierArityFailure()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "JsonDocument",
                "--unknown",
                "--take",
                "1",
                "--head=true",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Unrecognized command or argument '--unknown'.",
            error);
        Assert.DoesNotContain(
            "--head does not accept a value.",
            error);
    }

    [Fact]
    public void EarlierUnknownOption_PrecedesDuplicatedAttachedModifierValue()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "dup",
                "--unknown",
                "--head=dup",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Unrecognized command or argument '--unknown'.",
            error);
        Assert.DoesNotContain(
            "--head does not accept a value.",
            error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedParserValueDoesNotBorrowEarlierDuplicateValuePosition(
        bool useOptionValue)
    {
        var arguments = new List<string>
        {
            "find",
        };
        if (useOptionValue)
        {
            arguments.AddRange(
                [
                    "JsonDocument",
                    "--type",
                    "nope",
                ]);
        }
        else
        {
            arguments.Add("nope");
        }
        arguments.AddRange(
            [
                "--take",
                "--take",
                "1",
                "-v:nope",
                "--offline",
            ]);

        var (exit, output, error) =
            RunCli([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--take requires a value.",
            error);
        Assert.DoesNotContain(
            "Argument 'nope' not recognized.",
            error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierParserValuePrecedesDuplicatedAttachedPresenceValue(
        bool includeMissingTake)
    {
        var arguments = new List<string>
        {
            "find",
            "JsonDocument",
            "-v:nope",
        };
        if (includeMissingTake)
        {
            arguments.AddRange(
                [
                    "--take",
                    "--take",
                    "1",
                ]);
        }
        arguments.AddRange(
            [
                "--head=nope",
                "--offline",
            ]);

        var (exit, output, error) =
            RunCli([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Argument 'nope' not recognized.",
            error);
        Assert.DoesNotContain(
            "--take requires a value.",
            error);
        Assert.DoesNotContain(
            "--head does not accept a value.",
            error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedParserValuesPrecedeDuplicatedAttachedPresenceValue(
        bool attached)
    {
        var arguments = new List<string>
        {
            "find",
            "Json",
        };
        if (attached)
        {
            arguments.Add("-v:nope");
        }
        else
        {
            arguments.AddRange(
                [
                    "-v",
                    "nope",
                ]);
        }
        arguments.Add("--head=nope");
        if (attached)
        {
            arguments.Add("-v:nope");
        }
        else
        {
            arguments.AddRange(
                [
                    "-v",
                    "nope",
                ]);
        }
        arguments.Add("--offline");

        var (exit, output, error) =
            RunCli([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Option '-v' expects a single argument but 2 were provided.",
            error);
        Assert.DoesNotContain(
            "--head does not accept a value.",
            error);
    }

    [Fact]
    public void MissingTakeValue_PrecedesLaterPositionalDuplicatingAttachedValue()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "Json",
                "--package=dup",
                "--take",
                "--take",
                "1",
                "dup",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--take requires a value.",
            error);
    }

    [Fact]
    public void EarlierUnmatchedPositional_PrecedesDuplicatedAttachedModifierValue()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "Json",
                "dup",
                "--take",
                "1",
                "--head=dup",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Unrecognized command or argument 'dup'.",
            error);
        Assert.DoesNotContain(
            "--head does not accept a value.",
            error);
    }

    [Fact]
    public void ValidAttachedValueDoesNotCaptureLaterPositionalFailure()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "Json",
                "--package=dup",
                "--head=true",
                "dup",
                "--take",
                "1",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--head does not accept a value.",
            error);
        Assert.DoesNotContain(
            "Unrecognized command or argument 'dup'.",
            error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparatedOptionValueDoesNotBecomeAnUnmatchedOccurrence(
        bool addLaterDuplicate)
    {
        var arguments = new List<string>
        {
            "find",
            "Json",
            "--package",
            "dup",
            "--head=dup",
        };
        if (addLaterDuplicate)
            arguments.Add("dup");
        arguments.AddRange(
            [
                "--take",
                "1",
            ]);

        var (exit, output, error) =
            RunCli([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--head does not accept a value.",
            error);
        Assert.DoesNotContain(
            "Unrecognized command or argument 'dup'.",
            error);
    }

    [Fact]
    public void MissingRowsValue_PrecedesLaterParserArityFailure()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "JsonDocument",
                "--rows",
                "--take",
                "1",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--rows requires a value.",
            error);
        Assert.DoesNotContain(
            "Unrecognized command or argument '1'.",
            error);
    }

    [Fact]
    public void LiteralTakeCapability_PrecedesPackagePlanningResolution()
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--literal",
                "Json",
                "--take",
                "1",
                "--package",
                "UnpinnedName",
                "--tfm",
                "net10.0",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--take is available only with patternless find --package-prefix.",
            error);
        Assert.DoesNotContain(
            "exact ID@VERSION",
            error);
    }

    [Fact]
    public void PackageContentTakeMaximum_ParticipatesInComposedDiagnostics()
    {
        var takeBeforeRows = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--package-content",
                "--where",
                "facet=package.query.embedded-skill",
                "--take",
                "21",
                "--rows",
                "bad",
            ]);
        var rowsBeforeTake = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--package-content",
                "--where",
                "facet=package.query.embedded-skill",
                "--rows",
                "bad",
                "--take",
                "21",
            ]);
        var takeWithMissingCount = RunCli(
            [
                "find",
                "--package-prefix",
                "Contoso.",
                "--package-content",
                "--where",
                "facet=package.query.embedded-skill",
                "--head",
                "--take",
                "21",
            ]);

        Assert.Equal(1, takeBeforeRows.Exit);
        Assert.Empty(takeBeforeRows.Output);
        Assert.Contains(
            "--take must be between 1 and 20.",
            takeBeforeRows.Error);
        Assert.DoesNotContain(
            "--rows requires",
            takeBeforeRows.Error);

        Assert.Equal(1, rowsBeforeTake.Exit);
        Assert.Empty(rowsBeforeTake.Output);
        Assert.Contains(
            "--rows requires N..M, N.., or ..M",
            rowsBeforeTake.Error);
        Assert.DoesNotContain(
            "--take must be between",
            rowsBeforeTake.Error);

        Assert.Equal(1, takeWithMissingCount.Exit);
        Assert.Empty(takeWithMissingCount.Output);
        Assert.Contains(
            "--take must be between 1 and 20.",
            takeWithMissingCount.Error);
        Assert.DoesNotContain(
            "--head requires -n.",
            takeWithMissingCount.Error);
    }

    [Fact]
    public void PackageProfileExplicitEmptyPrefix_UsesProfileDiagnostic()
    {
        var (exit, output, error) = RunCli(
            ["find", "--package-prefix", ""]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--package-prefix must be 1 to 100 characters",
            error);
        Assert.DoesNotContain("Search pattern required", error);
    }

    [Fact]
    public async Task PackageProfileMarkdown_HonorsColumnProjection()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageProfileView view = PackageProfileSections.CreateDocument(
            "Contoso.",
            [
                new PackageProfileEvent.Match(
                    new PackageProfileMatch(
                        "Contoso.Package",
                        "1.0.0",
                        ["Contoso"],
                        42,
                        true,
                        source.Source,
                        ManifestFacts(
                            "Contoso.Package",
                            "1.0.0",
                            "Contoso",
                            [
                                new DeclaredPackageDependencyGroup(
                                    "net8.0",
                                    [
                                        new DeclaredPackageDependency(
                                            "Third.Party",
                                            "2.0.0"),
                                    ]),
                            ]))),
            ]);
        var options = new FindOptions
        {
            Columns = ["Package", "Version"],
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () =>
            {
                FindCommand.WritePackageProfileOutput(view, options);
                return Task.FromResult(0);
            });

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Package | Version |", output);
        Assert.DoesNotContain("| Owners |", output);
    }

    private static PackageManifestFacts ManifestFacts(
        string packageId,
        string version,
        string? authors,
        DeclaredPackageDependencyGroup[] dependencyGroups) =>
        new(
            PackageSourceCoordinate.Create(packageId, version),
            ManifestVersion: "nuspec",
            Description: null,
            authors,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: [],
            IsToolPackage: false,
            ReadmeFile: null,
            dependencyGroups.ToImmutableArray());

    private static (int Exit, string Output, string Error) RunCli(
        string[] args)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows()
                ? "dotnet-inspect.exe"
                : "dotnet-inspect");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        using System.Diagnostics.Process process =
            System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {executable}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            OutOfProcessCliProcess.KillAndWaitForExit(
                process,
                TimeSpan.FromSeconds(10));
            throw new TimeoutException($"{executable} did not exit.");
        }

        Task.WaitAll([output, error], 10_000);
        return (process.ExitCode, output.Result, error.Result);
    }

    // ── Framework coverage tests ─────────────────────────────────────

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Find_UnmatchedTypeStrictWindow_IsFormatIndependent(
        bool table,
        bool jsonl,
        bool count)
    {
        List<string> args =
        [
            "find",
            "ZzzNoSuchApi6585*",
            "--platform-library",
            "System.Text.Json",
            "--rows",
            "1..1",
        ];
        if (table)
            args.Add("--table");
        if (jsonl)
            args.Add("--jsonl");
        if (count)
            args.Add("--count");

        var (exit, output, error) = RunCli([.. args]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "only 0 type rows are available.",
            error);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--count")]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    public void Find_MixedMultiPatternStrictWindow_ExcludesUnmatchedContext(
        string format)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "JsonDocument,ZzzNoSuchApi6585*",
                "--platform-library",
                "System.Text.Json",
                "--rows",
                "2..2",
                format,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "only 1 type rows are available.",
            error);
        Assert.Contains(
            "1 search pattern matched no types.",
            error);
    }

    [Theory]
    [InlineData("--take", "bad", "--take requires a positive whole number.")]
    [InlineData("--rows", "bad", "--rows requires N..M, N.., or ..M")]
    public void LiteralPlanningFailure_FollowsSharedSelectionFailure(
        string option,
        string value,
        string expected)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--literal",
                "Json",
                "--package",
                "UnpinnedName",
                "--tfm",
                "net10.0",
                option,
                value,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
        Assert.DoesNotContain(
            "exact ID@VERSION coordinate",
            error);
    }

    [Fact]
    public void LiteralValueNamedTake_IsNotExecutionBound()
    {
        string[][] literalForms =
        [
            ["--literal", "--take"],
            ["--literal=--take"],
        ];

        foreach (string[] literalForm in literalForms)
        {
            var arguments = new List<string>
            {
                "find",
            };
            arguments.AddRange(literalForm);
            arguments.AddRange(
            [
                "--package",
                "Example@1.0.0",
                "--tfm",
                "net10.0",
                "-D",
                "Matches",
                "-n",
                "1",
                "--json",
            ]);

            var (exit, output, error) =
                RunCli([.. arguments]);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                "\"name\":\"Package\"",
                output);
            Assert.DoesNotContain(
                "--take requires",
                output);
        }
    }

    [Fact]
    public async Task Find_RuntimeFramework_FindsJsonSerializer()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsTypedResults()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "TypedResults",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("TypedResults", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsServerSentEventsResult()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "ServerSentEventsResult*",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // ServerSentEventsResult<T> is the generic version
        Assert.Contains("ServerSentEventsResult", output);
    }

    [Fact]
    public async Task Find_AspNetCoreFramework_FindsSseItem()
    {
        // Skip if aspnetcore is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "SseItem*",
            PlatformFrameworks = ["aspnetcore"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // SseItem<T> is the generic version; if not found, verify friendly message
        Assert.True(
            output.Contains("SseItem") || error.Contains("No types found"),
            "Expected either SseItem in output or 'No types found' message");
    }

    [Fact]
    public async Task Find_NetstandardFramework_FindsIEnumerable()
    {
        // Skip if netstandard is not installed
        var (refPath, _, _) = PlatformResolver.ResolveFramework("netstandard");
        if (refPath == null)
            return;

        var options = new FindOptions
        {
            Pattern = "IEnumerable",
            PlatformFrameworks = ["netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("IEnumerable", output);
    }

    [Fact]
    public async Task Find_AllFrameworks_SearchesAllThree()
    {
        var options = new FindOptions
        {
            Pattern = "Stream",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Stream", output);
        // Should find Stream in at least runtime
        Assert.Contains("runtime", output);
    }

    // ── FQN vs UQN tests ─────────────────────────────────────────────

    [Fact]
    public async Task Find_UQN_MatchesWithoutNamespace()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_FQN_MatchesWithNamespace()
    {
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_PartialNamespace_MatchesWithWildcard()
    {
        var options = new FindOptions
        {
            Pattern = "System.Text.*Serializer",
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    // ── Guessable vs non-guessable library tests ─────────────────────

    [Fact]
    public async Task Find_TypeInGuessableLibrary_FindsType()
    {
        // JsonSerializer is in System.Text.Json - the library name matches the namespace prefix
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_TypeInNonGuessableLibrary_RequiresFrameworkSearch()
    {
        // SortedSet`1 is a generic type - need wildcard to match
        // Finding it requires framework search to discover the library
        var options = new FindOptions
        {
            Pattern = "SortedSet*",  // SortedSet`1 is defined in System.Collections
            PlatformFrameworks = ["runtime"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("SortedSet", output);
        Assert.Contains("System.Collections", output);
    }

    [Fact]
    public async Task Find_TypeDefinedNotForwarded_FoundInExpectedLibrary()
    {
        // SortedDictionary is defined in System.Collections (not forwarded)
        var options = new FindOptions
        {
            Pattern = "SortedDictionary",
            PlatformAssemblies = ["System.Collections"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("SortedDictionary", output);
    }

    // ── Wildcard pattern tests ───────────────────────────────────────

    [Fact]
    public async Task Find_WildcardSuffix_MatchesMultipleTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Json*",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
    }

    [Fact]
    public async Task Find_TypeFilter_RestrictsTypeMatches()
    {
        var options = new FindOptions
        {
            Pattern = "Json*",
            TypeFilter = "System.Text.Json.JsonDocument",
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true,
            CompactJson = true,
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        var result = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "System.Text.Json.JsonDocument",
            result.GetProperty("full_name").GetString());
    }

    [Fact]
    public async Task Find_WildcardPrefix_MatchesMultipleTypes()
    {
        var options = new FindOptions
        {
            Pattern = "*Exception",
            PlatformAssemblies = ["System.Runtime"],
            Limit = 10
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(
            output.Contains("Exception") || error.Contains("No types found"),
            "Expected either Exception types in output or 'No types found' message");
    }

    [Fact]
    public async Task Find_WildcardMiddle_MatchesTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Json*Options",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializerOptions", output);
    }

    [Fact]
    public async Task Find_QuestionMarkWildcard_MatchesSingleCharacter()
    {
        var options = new FindOptions
        {
            Pattern = "Int??",
            PlatformFrameworks = ["runtime"],
            Limit = 20
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Int32", output);
        Assert.Contains("Int64", output);
    }

    // ── Multi-pattern tests ──────────────────────────────────────────

    [Fact]
    public async Task Find_MultiplePatterns_ReturnsResultsForEach()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer,JsonDocument",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
    }

    [Fact]
    public async Task Find_MultipleWildcardPatterns_ExpandsAll()
    {
        var options = new FindOptions
        {
            Pattern = "Json*,Utf8*",
            PlatformAssemblies = ["System.Text.Json"],
            Limit = 20
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
        // Utf8JsonWriter is the class (Utf8JsonReader is a ref struct not in public API)
        Assert.Contains("Utf8JsonWriter", output);
    }

    // ── Generic type tests ───────────────────────────────────────────

    [Fact]
    public async Task Find_GenericType_WithArityNotation()
    {
        var options = new FindOptions
        {
            Pattern = "Dictionary`2",
            PlatformFrameworks = ["runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary", output);
    }

    [Fact]
    public async Task Find_GenericType_WithAngleBracketNotation()
    {
        var options = new FindOptions
        {
            Pattern = "List<T>",
            PlatformFrameworks = ["runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("List", output);
    }

    // ── Limit tests ──────────────────────────────────────────────────

    [Fact]
    public async Task Find_WithLimit_RespectsLimit()
    {
        var options = new FindOptions
        {
            Pattern = "*",
            PlatformAssemblies = ["System.Runtime"],
            Limit = 5
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Output includes headers and formatting, but results should be limited
        Assert.True(lines.Length <= 20, "Output should be limited");
    }

    // ── JSON output tests ────────────────────────────────────────────

    [Fact]
    public async Task Find_JsonOutput_ProducesIndentedJsonArray()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement[0].ValueKind);
        Assert.True(doc.RootElement[0].TryGetProperty("full_name", out _));
        Assert.Contains("\n  {", output);
    }

    [Fact]
    public async Task Find_CompactJsonOutput_ProducesSingleLineJsonArray()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true,
            CompactJson = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var payload = output.TrimEnd();
        Assert.DoesNotContain('\n', payload);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Find_MemberJsonOutput_WithNoMatches_ProducesEmptyArray()
    {
        var options = new FindOptions
        {
            Pattern = "ZzzNoSuchMemberName",
            Members = true,
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true,
            CompactJson = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Equal("[]", output.Trim());
    }

    [Fact]
    public async Task Find_TypeFilter_RestrictsMemberDeclaringTypes()
    {
        var options = new FindOptions
        {
            Pattern = "Serialize",
            Members = true,
            TypeFilter = "System.Text.Json.JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"],
            JsonOutput = true,
            CompactJson = true,
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        var results = document.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(results);
        Assert.All(
            results,
            result => Assert.Equal(
                "System.Text.Json.JsonSerializer",
                result.GetProperty("declaring_type").GetString()));
    }

    [Theory]
    [InlineData(false, "type")]
    [InlineData(true, "member")]
    public void FindCount_IncompleteSourceDoesNotPublishANumber(
        bool members,
        string rowKind)
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"missing-find-count-{Guid.NewGuid():N}");
        var arguments = new List<string>
        {
            "find",
            "*",
            "--bin",
            missingDirectory,
            "--count",
        };
        if (members)
            arguments.Add("--members");

        var (exit, output, error) = RunCli([.. arguments]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Cannot count {rowKind} rows because one or more "
            + "search sources were incomplete.",
            error);
        Assert.Contains("Directory not found", error);
    }

    [Theory]
    [MemberData(nameof(ExactCountCases))]
    public void FindCountSufficiency_FollowsOrderedSemanticSelection(
        RowSelectionIntent<string>? selection,
        int observedCount,
        bool sourceComplete,
        bool expected)
    {
        Assert.Equal(
            expected,
            CliSemanticRowSelection.ProvidesExactCount(
                selection,
                observedCount,
                sourceComplete));
    }

    public static TheoryData<
        RowSelectionIntent<string>?,
        int,
        bool,
        bool> ExactCountCases =>
        new()
        {
            { null, 3, false, false },
            { null, 3, true, true },
            {
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
                3,
                false,
                false
            },
            {
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
                5,
                false,
                true
            },
            {
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Tail(3)]),
                3,
                false,
                true
            },
            {
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Window(null, 4)]),
                4,
                false,
                true
            },
            {
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Window(2, null)]),
                5,
                false,
                false
            },
            {
                RowSelectionIntent<string>.Create(
                    [
                        RowSelectionIntentOperation<string>.Head(5),
                        RowSelectionIntentOperation<string>.Window(2, null),
                    ]),
                5,
                false,
                true
            },
        };

    // ── Error handling tests ─────────────────────────────────────────

    [Fact]
    public async Task Find_NoScope_AppliesPlatformDefault()
    {
        var options = new FindOptions
        {
            Pattern = "Stream"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Stream", output);
    }

    [Fact]
    public async Task Find_NonExistentFramework_ShowsWarning()
    {
        var options = new FindOptions
        {
            Pattern = "Test",
            PlatformFrameworks = ["nonexistent"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Contains("Warning", error);
        Assert.Contains("Unknown framework", error);
    }

    // ── Multi-pattern with partial matches tests ─────────────────────

    [Fact]
    public async Task Find_MultiPattern_AllPatternsHaveExactOrPartialMatches()
    {
        // Test scenario:
        // - Good FQN type (exact match)
        // - Good UQN type (exact match)
        // - Misspelled FQN (partial match)
        // - Misspelled UQN (partial match)
        // - Glob pattern (multiple exact matches)
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer,JsonDocument,System.Text.Json.JsonSeriali,TypedResul,Sorted*",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Results section contains all match types
        Assert.Contains("## Results", output);

        // Good FQN - exact match
        Assert.Contains("JsonSerializer", output);

        // Good UQN - exact match
        Assert.Contains("JsonDocument", output);

        // Glob - multiple exact matches
        Assert.Contains("SortedDictionary", output);
        Assert.Contains("SortedList", output);
        Assert.Contains("SortedSet", output);

        // Misspelled patterns appear in Match column as "partial"
        Assert.Contains("partial", output);

        // Misspelled UQN - should have partial match to TypedResults
        Assert.Contains("TypedResul", output);
        Assert.Contains("TypedResults", output);
    }

    [Fact]
    public async Task Find_MultiPattern_SomePatternsHaveNoMatches()
    {
        // Test scenario: same as above plus patterns with no matches at all
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer,JsonDocument,System.Text.Json.JsonSeriali,TypedResul,Sorted*,System.Nonexistent.FooBarXyz,XyzNonexistent123",
            PlatformFrameworks = ["runtime", "aspnetcore", "netstandard"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Results section contains all match types in one table
        Assert.Contains("## Results", output);

        // Good patterns should still match
        Assert.Contains("JsonSerializer", output);
        Assert.Contains("JsonDocument", output);
        Assert.Contains("SortedDictionary", output);

        // Partial matches appear as rows with "partial" match kind
        Assert.Contains("partial", output);
        Assert.Contains("TypedResults", output);

        // Unmatched patterns are diagnostic context rather than selectable rows.
        Assert.DoesNotContain("notfound", output);
        Assert.Contains("2 search patterns matched no types.", error);
    }
}
