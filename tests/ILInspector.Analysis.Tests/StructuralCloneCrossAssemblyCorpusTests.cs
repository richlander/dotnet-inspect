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
        Assert.Contains(
            "Query selection: 7 query entries in the loaded ledger; "
                + "each selects a method in the query artifact",
            text);
        Assert.Contains(
            "Submitted candidate population: all methods in the "
                + "ledger-declared right-side type; RetrieveSimilar ranks "
                + "only methods with completed body analysis and a "
                + "query-compatible signature",
            text);
        Assert.Contains("Expectations met: 7/7 queries", text);
        Assert.Contains(
            "Results within declared review depth: 14/14 requested",
            text);
        Assert.Contains(
            "Precision over labeled results within reviewed depth: "
                + "42.85%",
            text);
        Assert.Contains(
            "Recall at reviewed depth over declared peers: "
                + "85.71% (6/7)",
            text);
        Assert.Contains(
            "Declared peers not recovered within reviewed depth: 1",
            text);
        Assert.Contains(
            "EXPECTATIONS MET: stable-body"
                + Environment.NewLine
                + "  Query method: Stable",
            text);
        Assert.Contains(
            "Candidate methods submitted: 36; completed body analysis: "
                + "32; ranked candidates: 6; "
                + "retrieval returned candidates: 6",
            text);
        Assert.Contains(
            "Precision over labeled results at depth: 50.00%; "
                + "recall@2 over declared peers: 100.00% (1/1)",
            text);
        Assert.Contains(
            "structural-score=10000/10000 Stable [relevant peer]",
            text);
        Assert.Contains(
            "SemanticCallStringLiteralNearMiss "
                + "[semantic lookalike (behavior differs)]",
            text);
        Assert.Contains(
            "Assign [hard negative (unrelated lookalike)]",
            text);
        Assert.Contains(
            "allocation-regression-miss",
            text);

        StructuralCloneCrossAssemblyQueryResult stringHazard =
            report.Queries.Single(static query =>
                query.Id == "user-string-hazard");
        Assert.Equal(
            stringHazard.TopCandidates[0].Similarity.Score,
            stringHazard.TopCandidates[1].Similarity.Score);
        Assert.Equal(
            2,
            stringHazard.Labels.Single(static label =>
                label.Label.Relevance
                    == StructuralCloneReviewRelevance.Relevant)
                .Label.MaximumRank);
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
        string text = StructuralCloneCrossAssemblyCorpus.Format(report);
        Assert.Contains(
            "unreviewed (relevance unknown)",
            text);
        Assert.Contains(
            "Results within declared review depth: 15/15 requested",
            text);
        Assert.DoesNotContain("Reviewed results:", text);
        Assert.Contains(
            "Review depth: 3; results within depth: 3; "
                + "relevant peers: 1/3",
            text);
        Assert.Contains(
            "INCOMPLETE REVIEW: results within top-K are incomplete "
                + "or include unknown relevance",
            text);
    }

    [Fact]
    public void Run_DescribesUnrankedRelevantPeerAsNotRecovered()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        StructuralCloneCrossAssemblyQuery stable =
            corpus.Queries.Single(static query =>
                query.Id == "stable-body");
        StructuralCloneCrossAssemblyLabel typeToken =
            corpus.Queries.Single(static query =>
                    query.Id == "type-token-shapes")
                .Labels.Single(static label =>
                    label.Relevance
                        == StructuralCloneReviewRelevance.Relevant);
        StructuralCloneCrossAssemblyCorpusReport report =
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.NewAssemblyPath(),
                corpus with
                {
                    Queries =
                    [
                        stable with
                        {
                            Labels =
                            [
                                typeToken with { ScoresAbove = [] },
                            ],
                        },
                    ],
                });

        StructuralCloneCrossAssemblyQueryResult query =
            Assert.Single(report.Queries);
        Assert.Null(Assert.Single(query.Labels).Rank);
        Assert.Equal(1, report.KnownMisses);
        string text = StructuralCloneCrossAssemblyCorpus.Format(report);
        Assert.Contains(
            "Query selection: 1 query entry in the loaded ledger; "
                + "each selects a method in the query artifact",
            text);
        Assert.Contains(
            "Declared peers not recovered within reviewed depth: 1",
            text);
    }

    [Fact]
    public void Run_PreservesPreAdmissionMethodLimitAsCorpusFailure()
    {
        StructuralCloneCrossAssemblyCorpusDocument corpus = LoadCorpus();
        StructuralCloneCrossAssemblyCorpusReport report =
            StructuralCloneCrossAssemblyCorpus.Run(
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                FixtureCatalog.DiffPair.NewAssemblyPath(),
                corpus with
                {
                    Limits = corpus.Limits with { MaximumMethods = 1 },
                });

        Assert.False(report.Success);
        Assert.Equal(0, report.KnownMisses);
        Assert.All(
            report.Queries,
            static query =>
            {
                Assert.Equal(
                    StructuralCloneRetrievalDisposition.LimitReached,
                    query.RetrievalDisposition);
                Assert.Equal(
                    Guid.Empty,
                    query.Seed.Address.ModuleVersionId);
                Assert.Equal(0, query.RetrievalReceipt.BodyProductions);
                Assert.Contains(
                    query.RetrievalBlockers,
                    static blocker =>
                        blocker.Kind
                            == StructuralCloneRetrievalBlockerKind
                                .MethodLimit);
            });
        string text = StructuralCloneCrossAssemblyCorpus.Format(report);
        Assert.Contains(
            "Recall at reviewed depth over declared peers: n/a"
                + Environment.NewLine,
            text);
        Assert.DoesNotContain(
            "over declared peers: n/a (",
            text);
        Assert.Contains(
            "Declared peers not recovered within reviewed depth: n/a",
            text);
        Assert.DoesNotContain(
            "Declared peers not recovered within reviewed depth: 0",
            text);
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
            "fixtures/diff/DiffFixtures.V1",
            corpus.Left.Project);
        Assert.Equal(
            "fixtures/diff/DiffFixtures.V2",
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
    public async Task Command_ReportsMethodLimitAsCorpusFailure()
    {
        string ledgerPath = Path.Combine(
            Path.GetTempPath(),
            $"cross-assembly-limit-{Guid.NewGuid():N}.json");
        try
        {
            JsonObject ledger = CorpusJson();
            ledger["limits"]!["maximumMethods"] = 1;
            File.WriteAllText(ledgerPath, ledger.ToJsonString());

            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(
                typeof(StructuralCloneCrossAssemblyCorpus)
                    .Assembly.Location);
            start.ArgumentList.Add("--clone-cross-assembly-corpus");
            start.ArgumentList.Add(
                FixtureCatalog.DiffPair.OldAssemblyPath());
            start.ArgumentList.Add(
                FixtureCatalog.DiffPair.NewAssemblyPath());
            start.ArgumentList.Add("--cross-assembly-ledger");
            start.ArgumentList.Add(ledgerPath);
            start.ArgumentList.Add("--json");

            using Process process = Process.Start(start)!;
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            Task<string> standardError =
                process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> standardOutput =
                process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            Assert.Equal(1, process.ExitCode);
            Assert.Equal("", await standardError);
            Assert.Contains(
                "\"retrievalDisposition\": \"limitReached\"",
                await standardOutput);
        }
        finally
        {
            File.Delete(ledgerPath);
        }
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
