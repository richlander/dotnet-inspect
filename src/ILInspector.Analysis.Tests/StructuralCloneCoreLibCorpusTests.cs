using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using ILInspector.Analysis;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneCoreLibCorpusTests
{
    [Fact]
    public void CommittedCorpus_GradesPinnedCoreLib()
    {
        StructuralCloneCoreLibCorpusReport report =
            StructuralCloneCoreLibCorpus.Run(
                typeof(object).Assembly.Location,
                LoadCorpus());

        Assert.True(
            report.Success,
            StructuralCloneCoreLibCorpus.Format(report));
        Assert.Equal(6, report.PassedQueries);
        Assert.Equal(6, report.TotalQueries);
        Assert.Equal(27, report.ReviewedCandidates);
        Assert.Equal(16, report.RelevantAtK);
        Assert.Equal(20, report.RelevantLabels);
        Assert.Equal(5925, report.PrecisionBasisPoints);
        Assert.Equal(8000, report.RecallBasisPoints);
        Assert.Equal(22, report.StructuralMatchesAtK);
        Assert.Equal(9, report.SemanticHazardsAtK);
        Assert.Equal(2, report.HardNegativesAtK);
        Assert.Equal(0, report.OrdinaryNegativesAtK);
        Assert.All(
            report.Queries,
            static query =>
            {
                Assert.True(query.TopKFullyReviewed);
                Assert.Equal(
                    query.ReviewedTopK,
                    query.TopCandidates.Length);
                Assert.All(
                    query.TopCandidates,
                    candidate => Assert.Contains(
                        query.Labels,
                        label =>
                            label.Label.Candidate
                                == $"0x{candidate.Method.Token:X8}"));
            });

        StructuralCloneCoreLibQueryResult convert =
            report.Queries.Single(
                static query =>
                    query.Id == "convert-hex-casing");
        Assert.Equal(5000, convert.PrecisionBasisPoints);
        Assert.Equal(3333, convert.RecallBasisPoints);
        StructuralCloneCoreLibQuery convertDefinition =
            LoadCorpus().Queries.Single(
                static query =>
                    query.Id == "convert-hex-casing");
        Assert.Equal(
            convert.PrecisionBasisPoints,
            convertDefinition.MinimumPrecisionBasisPoints);
        Assert.Equal(
            convert.RecallBasisPoints,
            convertDefinition.MinimumRecallBasisPoints);
        StructuralCloneCoreLibLabelResult lowerCase =
            convert.Labels.Single(
                static result =>
                    result.Label.Candidate == "0x06000F8A");
        StructuralCloneCoreLibScoreContrastResult contrast =
            Assert.Single(lowerCase.Contrasts);
        Assert.NotNull(lowerCase.Similarity);
        Assert.NotNull(contrast.Similarity);
        Assert.True(
            lowerCase.Similarity.Score
                > contrast.Similarity.Score,
            "The committed score contrast must be strict.");

        StructuralCloneCoreLibLabelResult[] misses =
        [
            .. report.Queries.SelectMany(query =>
                query.Labels.Where(result =>
                    result.Label.Relevance
                        == StructuralCloneReviewRelevance.Relevant
                    && result.Rank is { } rank
                    && rank > query.ReviewedTopK)),
        ];
        Assert.Equal(4, misses.Length);

        StructuralCloneCoreLibQueryResult unsafeStubs =
            report.Queries.Single(
                static query =>
                    query.Id == "unsafe-intrinsic-stubs");
        Assert.Equal(0, unsafeStubs.RelevantAtK);
        Assert.Equal(9, unsafeStubs.SemanticHazardsAtK);
        Assert.All(
            unsafeStubs.TopCandidates,
            static candidate =>
            {
                Assert.Equal(
                    StructuralCloneReviewRelevance.SemanticHazard,
                    candidate.Relevance);
                Assert.Equal(
                    StructuralCloneRelation.Exact,
                    candidate.ActualRelation);
            });
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("mvid")]
    [InlineData("token")]
    [InlineData("type")]
    [InlineData("name")]
    public void Run_RejectsArtifactAndIdentityDrift(string mutation)
    {
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();
        StructuralCloneCoreLibMethod first =
            corpus.Methods[0];
        StructuralCloneCoreLibCorpusDocument altered =
            mutation switch
            {
                "sha" => corpus with
                {
                    Artifact = corpus.Artifact with
                    {
                        Sha256 = new string('0', 64),
                    },
                },
                "mvid" => corpus with
                {
                    Artifact = corpus.Artifact with
                    {
                        ModuleVersionId =
                            "00000000-0000-0000-0000-000000000000",
                    },
                },
                "token" => corpus with
                {
                    Methods = corpus.Methods
                        .SetItem(
                            0,
                            first with
                            {
                                Token = corpus.Methods[1].Token,
                            })
                        .SetItem(
                            1,
                            corpus.Methods[1] with
                            {
                                Token = first.Token,
                            }),
                },
                "type" => corpus with
                {
                    Methods = corpus.Methods.SetItem(
                        0,
                        first with { Type = "System.NotGuid" }),
                },
                "name" => corpus with
                {
                    Methods = corpus.Methods.SetItem(
                        0,
                        first with { Method = "NotGreaterThan" }),
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mutation)),
            };

        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCoreLibCorpus.Run(
                typeof(object).Assembly.Location,
                altered));
    }

    [Fact]
    public void Load_RejectsDuplicateAndOrphanCatalogEntries()
    {
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCoreLibCorpus.Run(
                typeof(object).Assembly.Location,
                corpus with
                {
                    Methods = corpus.Methods.Add(corpus.Methods[0]),
                }));

        StructuralCloneCoreLibQuery guid =
            corpus.Queries[0];
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCoreLibCorpus.Run(
                typeof(object).Assembly.Location,
                corpus with
                {
                    Queries = corpus.Queries.SetItem(
                        0,
                        guid with
                        {
                            Labels = guid.Labels.RemoveAt(0),
                        }),
                }));
    }

    [Fact]
    public void Load_RejectsIncompleteOrPermissiveJson()
    {
        JsonObject unknown = CorpusJson();
        unknown["unknown"] = true;
        Assert.Throws<JsonException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                unknown.ToJsonString()));

        JsonObject missingLabels = CorpusJson();
        missingLabels["queries"]![0]!
            .AsObject()
            .Remove("labels");
        Assert.Throws<JsonException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                missingLabels.ToJsonString()));

        JsonObject integerEnum = CorpusJson();
        integerEnum["queries"]![0]!["labels"]![0]!
            ["relevance"] = 0;
        Assert.Throws<JsonException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                integerEnum.ToJsonString()));
    }

    [Fact]
    public async Task Command_RejectsMissingCoreLibLedgerValue()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCoreLibCorpus).Assembly.Location);
        start.ArgumentList.Add("--clone-corelib-corpus");
        start.ArgumentList.Add(typeof(object).Assembly.Location);
        start.ArgumentList.Add("--corelib-ledger");
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
            "--corelib-ledger requires a file path.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    static StructuralCloneCoreLibCorpusDocument LoadCorpus() =>
        StructuralCloneCoreLibCorpus.Load(
            CorpusJson().ToJsonString());

    static JsonObject CorpusJson()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-corelib.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
