using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Commands;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using InertText;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Tests;

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
    public void PackageProfileSection_BindsProfileQueryAndProjectsDependencySecond()
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
        Assert.StartsWith("package\tdependency\t", tsvLines[0]);
        Assert.StartsWith(
            "Contoso.Package\tThird.Party\t",
            tsvLines[1]);

        string jsonl = RenderPackageProfileTable(
            view,
            tsv: false,
            showHeader: false,
            jsonl: true);
        using JsonDocument jsonlDocument = JsonDocument.Parse(jsonl);
        Assert.Equal(
            "Third.Party",
            jsonlDocument.RootElement.GetProperty("dependency").GetString());

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
            "Third.Party",
            jsonDocument.RootElement
                .GetProperty("packages")[0]
                .GetProperty("dependency")
                .GetString());

        string markdown = MarkoutSerializer.Serialize(
            view,
            SearchViewContext.Default);
        Assert.Contains("| Contoso.Package | Third.Party |", markdown);
    }

    [Fact]
    public void PackageProfileSection_KeepsFailuresAndTruncationVisible()
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

        Assert.Equal(2, view.Results!.Count);
        Assert.Equal(
            "InvalidManifest:IdentityMismatch",
            view.Results[0].Status);
        Assert.Equal("The manifest was invalid.", view.Results[0].Error);
        Assert.Equal("truncated", view.Results[1].Status);
        Assert.Contains(
            "source pagination limit",
            view.Results[1].Error);
        Assert.Equal(
            TestResults.Source.Producer.Display.ToString(),
            view.Results[1].Source);
        Assert.Equal(2, PackageProfileSections.CountRows(view));
        Assert.Equal(
            1,
            PackageProfileSections.CountRows(
                PackageProfileSections.CreateDocument(
                    "Contoso.",
                    events,
                    RowWindow.Head(1))));
        PackageProfileView tail = PackageProfileSections.CreateDocument(
            "Contoso.",
            events,
            RowWindow.Tail(1));
        Assert.Equal(
            "truncated",
            Assert.Single(tail.Results!).Status);
    }

    [Fact]
    public async Task PackageProfileCatalog_MaterializesSourceExecutionOnce()
    {
        var source = new CountingPackageSource();
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

        Assert.Equal(1, source.SearchRequests);
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

        Assert.Equal(dependencyCount, view.Results!.Count);
        Assert.Same(view.Results[0].Authors, view.Results[^1].Authors);
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

        Assert.Equal(5, view.Results!.Count);
        Assert.Equal("Dependency.0", view.Results[0].Dependency);
        Assert.Equal("Dependency.4", view.Results[^1].Dependency);
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
            Assert.Contains("Dependency.Zero", output);
            Assert.Contains("Dependency.One", output);
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
    [InlineData("Azure", 0, "-t must be between 1 and 10000 for a package-prefix profile (got 0).")]
    [InlineData("Azure", 10001, "-t must be between 1 and 10000 for a package-prefix profile (got 10001).")]
    [InlineData("Azure ", 100, "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.")]
    public async Task PackageProfileInvalidInput_UsesComposedDiagnostic(
        string prefix,
        int limit,
        string expected)
    {
        var options = new FindOptions
        {
            Pattern = "",
            PackagePrefix = prefix,
            Limit = limit,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

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

    [Theory]
    [InlineData("-t")]
    [InlineData("--type")]
    public void PackageProfileCountWithPackageLimit_FailsBeforeNetwork(
        string option)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Microsoft",
                option,
                "2",
                "--count",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--count cannot be combined with -t for a package-prefix search",
            error);
        Assert.DoesNotContain("Attempted:", error);
    }

    [Theory]
    [InlineData("-t", "-D")]
    [InlineData("-t", "--discover")]
    [InlineData("--type", "-D")]
    [InlineData("--type", "--discover")]
    public void PackageProfileDiscoveryRejectsCountWithPackageLimit(
        string limitOption,
        string discoverOption)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Microsoft",
                limitOption,
                "2",
                "--count",
                discoverOption,
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--count cannot be combined with -t for a package-prefix search",
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
                "-t",
                limit,
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-t must be an integer between 1 and 10000", error);
        Assert.DoesNotContain("Attempted:", error);
        Assert.DoesNotContain("Arg_", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ArgumentOutOfRange_",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-t")]
    [InlineData("--type")]
    public void PackageProfileSeparatedNegativeLimit_RemainsProfileInput(
        string option)
    {
        var (exit, output, error) = RunCli(
            [
                "find",
                "--package-prefix",
                "Azure",
                option,
                "-5",
                "--offline",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "-t must be between 1 and 10000 for a package-prefix profile",
            error);
        Assert.DoesNotContain("Attempted:", error);
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
            Columns = ["Package", "Dependency"],
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () =>
            {
                FindCommand.WritePackageProfileOutput(view, options);
                return Task.FromResult(0);
            });

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Package | Dependency |", output);
        Assert.DoesNotContain("| Version |", output);
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

    [Fact]
    public async Task Find_RuntimeFramework_FindsJsonSerializer()
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

    // ── Error handling tests ─────────────────────────────────────────

    [Fact]
    public async Task Find_NoScope_AppliesCuratedDefault()
    {
        var options = new FindOptions
        {
            Pattern = "Stream"
            // No scope specified - should apply curated defaults
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        // Should succeed with curated scope applied
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

        var (exit, output, _) = await ConsoleCapture.RunAsync(
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

        // Not found patterns appear as rows with "notfound" match kind
        Assert.Contains("notfound", output);
        Assert.Contains("System.Nonexistent.FooBarXyz", output);
        Assert.Contains("XyzNonexistent123", output);
    }
}
