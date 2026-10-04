using System.Text.Json;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public void TypeFindQuestion_RejectsInvalidPatternsAndLimits()
    {
        Assert.Throws<ArgumentException>(
            () => TypeFindQuestion.Create(
                Array.Empty<string>(),
                FindVisibility.Public));
        Assert.Throws<ArgumentException>(
            () => TypeFindQuestion.Create(
                [" "],
                FindVisibility.Public));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TypeFindQuestion.Create(
                ["Widget"],
                FindVisibility.Public,
                maximumMatches: 0));
        Assert.Throws<ArgumentException>(
            () => TypeFindQuestion.Create(
                [
                    TypeFindPattern.Create(1, "Widget"),
                ],
                FindVisibility.Public));
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_PreservesPerCandidateMatchOrder()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Json",
                    metadata =>
                    {
                        LocatorDefinition(
                            metadata,
                            "System.Text.Json",
                            "JsonSerializerDefaults");
                        LocatorDefinition(
                            metadata,
                            "System.Text.Json",
                            "JsonSerializer");
                        LocatorDefinition(
                            metadata,
                            "System.Text.Json",
                            "JsonSerializerOptions");
                    }));
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["JsonSerializer"],
                FindVisibility.Public);

        TypeFindBlock block =
            SemanticBlock(
                question,
                Locate(
                    CaptureDeclarations(workspace, context),
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        Assert.Equal(
            [
                TypeFindSemanticMatchKind.Prefix,
                TypeFindSemanticMatchKind.Exact,
                TypeFindSemanticMatchKind.Prefix,
            ],
            block.Matches.Select(static match => match.Match));
        Assert.Equal(
            [
                "System.Text.Json.JsonSerializerDefaults",
                "System.Text.Json.JsonSerializer",
                "System.Text.Json.JsonSerializerOptions",
            ],
            block.Matches.Select(static match => match.FullName));
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            Assert.Single(block.Settlements).Kind);
        Assert.True(block.IsSourceCoverageComplete);
        Assert.Equal(
            FindMatchCompletion.Exhausted,
            block.MatchCompletion);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_ClassifiesGrammarAndCompleteMiss()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Types",
                    metadata =>
                    {
                        LocatorDefinition(metadata, "N", "Widget");
                        LocatorDefinition(metadata, "N.Child", "Nested");
                        LocatorDefinition(metadata, "N", "Box`1");
                    }));
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                [
                    TypeFindPattern.Create(0, "Widget"),
                    TypeFindPattern.Create(1, "Box<T>"),
                    TypeFindPattern.Create(2, "Wid*"),
                    TypeFindPattern.CreateNamespace(
                        3,
                        "N.*",
                        "N",
                        ILInspector.Metadata.MetadataNamespaceMatch
                            .ExactOrDescendant),
                    TypeFindPattern.Create(4, "idge"),
                    TypeFindPattern.Create(5, "Widgit"),
                    TypeFindPattern.Create(6, "Missing"),
                ],
                FindVisibility.Public);

        TypeFindBlock block =
            SemanticBlock(
                question,
                Locate(
                    CaptureDeclarations(workspace, context),
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        Assert.Contains(
            block.Matches,
            static match =>
                match.Pattern.Ordinal == 0
                && match.Match is TypeFindSemanticMatchKind.Exact);
        Assert.Contains(
            block.Matches,
            static match =>
                match.Pattern.Ordinal == 1
                && match.Match is TypeFindSemanticMatchKind.Direct);
        Assert.Contains(
            block.Matches,
            static match =>
                match.Pattern.Ordinal == 2
                && match.Match is TypeFindSemanticMatchKind.Glob);
        Assert.Equal(
            3,
            block.Matches.Count(
                static match =>
                    match.Pattern.Ordinal == 3
                    && match.Match
                        is TypeFindSemanticMatchKind.Namespace));
        Assert.Contains(
            block.Matches,
            static match =>
                match.Pattern.Ordinal == 4
                && match.Match
                    is TypeFindSemanticMatchKind.Substring);
        Assert.Contains(
            block.Matches,
            static match =>
                match.Pattern.Ordinal == 5
                && match.Match
                    is TypeFindSemanticMatchKind.Partial);
        Assert.Equal(
            FindPatternSettlementKind.NoMatch,
            block.Settlements[6].Kind);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_PartialCoverageWithholdsNoMatch()
    {
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext failed =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                new(),
                Options(
                    client,
                    new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["Missing"],
                FindVisibility.Public);

        TypeFindBlock block =
            SemanticBlock(
                question,
                Locate(
                    CaptureDeclarations(workspace, failed),
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        Assert.Empty(block.Matches);
        Assert.Equal(
            FindPatternSettlementKind.Inconclusive,
            Assert.Single(block.Settlements).Kind);
        Assert.False(block.IsSourceCoverageComplete);
        Assert.NotEmpty(block.PopulationGaps);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_FailedSourceWithholdsNoMatch()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Types",
                    metadata =>
                        LocatorDefinition(
                            metadata,
                            "N",
                            "Widget")));
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, context);
        ContextLoaded(context).Group.Dispose();
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["Missing"],
                FindVisibility.Public);
        TypeFindSemanticPopulation evaluated =
            TypeFindSourceEvaluator.EvaluateLocatorCensus(
                question,
                Locate(
                    population,
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        Assert.IsType<TypeFindSourceEvaluation.Failed>(
            Assert.Single(evaluated.Sources));
        TypeFindBlock block =
            FindSemanticReducer.ReduceType(
                question,
                evaluated);
        Assert.Equal(
            FindPatternSettlementKind.Inconclusive,
            Assert.Single(block.Settlements).Kind);
        Assert.False(block.IsSourceCoverageComplete);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_LimitLeavesLaterPatternUnevaluated()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Types",
                    metadata =>
                    {
                        LocatorDefinition(metadata, "N", "Widget");
                        LocatorDefinition(metadata, "N", "Other");
                    }));
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["Widget", "Other"],
                FindVisibility.Public,
                maximumMatches: 1);

        TypeFindBlock block =
            SemanticBlock(
                question,
                Locate(
                    CaptureDeclarations(workspace, context),
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        Assert.Single(block.Matches);
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            block.Settlements[0].Kind);
        Assert.Equal(
            FindPatternSettlementKind.NotEvaluated,
            block.Settlements[1].Kind);
        Assert.Equal(
            FindMatchCompletion.MatchLimitReached,
            block.MatchCompletion);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_SourceCanJoinTwoPopulations()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Types",
                    metadata =>
                        LocatorDefinition(
                            metadata,
                            "N",
                            "Widget")));
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["Widget"],
                FindVisibility.Public);
        TypeFindSemanticPopulation evaluated =
            TypeFindSourceEvaluator.EvaluateLocatorCensus(
                question,
                Locate(
                    CaptureDeclarations(workspace, context),
                    new TypeDeclarationLocatorRequest.Pattern("*")));
        TypeFindSourceEvaluation source =
            Assert.Single(evaluated.Sources);

        TypeFindBlock first =
            FindSemanticReducer.ReduceType(
                question,
                TypeFindSemanticPopulation.Create(
                    question,
                    [source]));
        TypeFindBlock second =
            FindSemanticReducer.ReduceType(
                question,
                TypeFindSemanticPopulation.Create(
                    question,
                    [source]));

        Assert.Same(
            first.Matches[0].Declaration.Source.Coordinate,
            second.Matches[0].Declaration.Source.Coordinate);
        Assert.Equal(first.Matches, second.Matches);
    }

    [Fact]
    public async Task TypeFindSemanticEvaluation_BlockSerializesExactAssociation()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await LocatorContext(
                workspace,
                LocatorImage(
                    "Types",
                    metadata =>
                        LocatorDefinition(
                            metadata,
                            "N",
                            "Widget")));
        TypeFindQuestion question =
            TypeFindQuestion.Create(
                ["Widget"],
                FindVisibility.Public);
        TypeFindBlock block =
            SemanticBlock(
                question,
                Locate(
                    CaptureDeclarations(workspace, context),
                    new TypeDeclarationLocatorRequest.Pattern("*")));

        string json = JsonSerializer.Serialize(block);

        Assert.Contains("\"kind\":\"package\"", json);
        Assert.Contains("\"Namespace\":\"N\"", json);
        Assert.Contains("\"segments\":[\"Widget\"]", json);
        Assert.Contains("\"ModuleVersionId\":", json);
        TypeFindBlock roundTrip =
            Assert.IsType<TypeFindBlock>(
                JsonSerializer.Deserialize<TypeFindBlock>(json));
        TypeFindSemanticMatch match =
            Assert.Single(roundTrip.Matches);
        Assert.Equal(
            "N.Widget",
            match.Declaration.Name.ToMetadataFullName());
        Assert.IsType<
            DotnetInspector.SourceSelection
                .ExactLibrarySourceCoordinate.Package>(
                    match.Declaration.Source.Coordinate);
    }

    private static TypeFindBlock SemanticBlock(
        TypeFindQuestion question,
        TypeDeclarationLocatorResult result)
    {
        TypeFindSemanticPopulation population =
            TypeFindSourceEvaluator.EvaluateLocatorCensus(
                question,
                result);
        return FindSemanticReducer.ReduceType(
            question,
            population);
    }
}
