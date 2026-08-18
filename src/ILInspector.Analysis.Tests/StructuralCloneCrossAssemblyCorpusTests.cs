using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneCrossAssemblyCorpusTests
{
    [Fact]
    public void CommittedCorpus_GradesVersionPair()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        StructuralCloneCrossAssemblyCorpusReport report =
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.NewAssemblyPath(),
                corpus);

        Assert.True(report.Success);
        Assert.Equal(7, report.PassedQueries);
        Assert.Equal(7, report.TotalQueries);
        Assert.Equal(14, report.ReviewedCandidates);
        Assert.Equal(14, report.RequestedReviewSlots);
        Assert.Equal(6, report.RelevantAtK);
        Assert.Equal(7, report.RelevantLabels);
        Assert.Equal(4285, report.PrecisionBasisPoints);
        Assert.Equal(8571, report.RecallBasisPoints);
        Assert.Equal(2, report.SemanticHazardsAtK);
        Assert.Equal(6, report.HardNegativesAtK);
        Assert.Equal(1, report.KnownMisses);
        Assert.NotEqual(
            report.LeftModuleVersionId,
            report.RightModuleVersionId);
        Assert.All(
            report.Queries,
            query => Assert.Equal(
                report.LeftModuleVersionId,
                query.Seed.Address.ModuleVersionId));
        Assert.All(
            report.Queries.SelectMany(static query =>
                query.TopCandidates),
            candidate => Assert.Equal(
                report.RightModuleVersionId,
                candidate.Method.Address.ModuleVersionId));

        string text = StructuralCloneCrossAssemblyCorpus.Format(report);
        Assert.Contains("7/7 queries", text);
        Assert.Contains("precision=42.85%", text);
        Assert.Contains("labeled-recall=85.71%", text);
        Assert.Contains("known-misses=1", text);
        Assert.Contains(
            "allocation-regression-miss",
            text);
    }

    [Fact]
    public void Run_PreservesUnreviewedReturnedRowAsUnknown()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        StructuralCloneCrossAssemblyQuery stable =
            corpus.Queries.Single(static query =>
                query.Id == "stable-body");
        StructuralCloneCrossAssemblyCorpusReport report =
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.NewAssemblyPath(),
                corpus with
                {
                    Queries = corpus.Queries.Replace(
                        stable,
                        stable with { ReviewedTopK = 3 }),
                });

        StructuralCloneCrossAssemblyQueryResult query =
            report.Queries.Single(static query =>
                query.Id == "stable-body");
        Assert.False(query.Passed);
        Assert.False(query.TopKFullyReviewed);
        Assert.Equal(3, query.TopCandidates.Length);
        Assert.Single(
            query.TopCandidates,
            static candidate => candidate.Relevance is null);
        Assert.Null(query.PrecisionBasisPoints);
        Assert.Null(report.PrecisionBasisPoints);
        Assert.Contains(
            "Unreviewed",
            StructuralCloneCrossAssemblyCorpus.Format(report));
    }

    [Fact]
    public void Run_RejectsSameArtifactAndAssemblyDrift()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                corpus));

        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.NewAssemblyPath(),
                corpus with
                {
                    Right = corpus.Right with
                    {
                        AssemblyName = "NotDiffFixtureSample",
                    },
                }));
    }

    [Fact]
    public void Load_RejectsIncompleteAndPermissiveJson()
    {
        JsonObject unknown = CorpusJson();
        unknown["unknown"] = true;
        Assert.Throws<JsonException>(() =>
            StructuralCloneCrossAssemblyCorpus.Load(
                unknown.ToJsonString()));

        JsonObject missingLabels = CorpusJson();
        missingLabels["queries"]![0]!
            .AsObject()
            .Remove("labels");
        Assert.Throws<JsonException>(() =>
            StructuralCloneCrossAssemblyCorpus.Load(
                missingLabels.ToJsonString()));

        JsonObject invalidContrast = CorpusJson();
        invalidContrast["queries"]![0]!["labels"]![0]!
            ["scoresAbove"]![0] = "MissingLabel";
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCrossAssemblyCorpus.Load(
                invalidContrast.ToJsonString()));

        string duplicateProperty =
            CorpusJson()
                .ToJsonString()
                .Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"schemaVersion\":1",
                    StringComparison.Ordinal);
        Assert.Throws<JsonException>(() =>
            StructuralCloneCrossAssemblyCorpus.Load(
                duplicateProperty));
    }

    [Fact]
    public void CommittedCorpus_PinsNonVacuousReviewCoverage()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        Assert.Equal(
            "src/DiffFixtures.V1",
            corpus.Left.Project);
        Assert.Equal(
            "src/DiffFixtures.V2",
            corpus.Right.Project);
        Assert.Equal(
            "DiffFixtureSample.DiffSample",
            corpus.Left.Type);
        Assert.Equal(corpus.Left.Type, corpus.Right.Type);
        Assert.Equal(7, corpus.Queries.Length);
        Assert.Equal(
            14,
            corpus.Queries.Sum(static query =>
                query.ReviewedTopK));
        Assert.Equal(
            15,
            corpus.Queries.Sum(static query =>
                query.Labels.Length));
        Assert.Equal(
            7,
            corpus.Queries.Sum(query =>
                query.Labels.Count(static label =>
                    label.Relevance
                        == StructuralCloneReviewRelevance.Relevant)));
        Assert.Equal(
            5,
            corpus.Queries.Sum(query =>
                query.Labels.Sum(static label =>
                    label.ScoresAbove.Length)));
        Assert.Equal(
            6,
            corpus.Queries.Count(static query =>
                query.MinimumPrecisionBasisPoints == 5000
                && query.MinimumRecallBasisPoints == 10000));
        StructuralCloneCrossAssemblyQuery knownMiss =
            corpus.Queries.Single(static query =>
                query.Id == "allocation-regression-miss");
        Assert.Equal(0, knownMiss.MinimumPrecisionBasisPoints);
        Assert.Equal(0, knownMiss.MinimumRecallBasisPoints);
    }

    [Fact]
    public async Task Command_GradesCommittedCorpus()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCrossAssemblyCorpus).Assembly.Location);
        start.ArgumentList.Add("--clone-cross-assembly-corpus");
        start.ArgumentList.Add(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        start.ArgumentList.Add(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        start.ArgumentList.Add("--json");

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("", await standardError);
        Assert.Contains(
            "\"passedQueries\": 7",
            await standardOutput);
    }

    [Fact]
    public async Task Command_RejectsMissingRightAssembly()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCrossAssemblyCorpus).Assembly.Location);
        start.ArgumentList.Add("--clone-cross-assembly-corpus");
        start.ArgumentList.Add(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        start.ArgumentList.Add("--json");

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains(
            "--clone-cross-assembly-corpus requires left and right "
                + "assembly paths.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    static StructuralCloneCrossAssemblyCorpusDocument LoadCorpus()
        => StructuralCloneCrossAssemblyCorpus.Load(
            CorpusJson().ToJsonString());

    static JsonObject CorpusJson()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-cross-assembly.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
