using System.Diagnostics;
using System.Security.Cryptography;
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
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();
        StructuralCloneCoreLibCorpusReport report =
            StructuralCloneCoreLibCorpus.Run(
                PinnedCoreLib(corpus),
                corpus);

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
        StructuralCloneCoreLibLabelResult failedLabel =
            lowerCase with { Passed = false };
        StructuralCloneCoreLibQueryResult failedQuery =
            convert with
            {
                Labels = convert.Labels.Replace(
                    lowerCase,
                    failedLabel),
                Passed = false,
            };
        string failedContrastCard =
            StructuralCloneCoreLibCorpus.Format(
                report with
                {
                    PassedQueries = report.PassedQueries - 1,
                    Queries = report.Queries.Replace(
                        convert,
                        failedQuery),
                });
        Assert.Contains(
            "contrast score=9847 must exceed score=9695",
            failedContrastCard);

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

    [Fact]
    public void CommittedCorpus_PinsNonVacuousReviewCoverage()
    {
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();

        Assert.Equal("System.Private.CoreLib.dll", corpus.Artifact.FileName);
        Assert.Equal(
            "6c14b54d28c604613aef5a690fa10aa768e584556e90a736b506f40022f8afea",
            corpus.Artifact.Sha256);
        Assert.Equal(
            "82790b72-6139-4dc6-8bd5-0d6b13d1c5e8",
            corpus.Artifact.ModuleVersionId);
        Assert.Equal(
            "https://github.com/dotnet/dotnet",
            corpus.Source.Repository);
        Assert.Equal(
            "e2c1e00b3d0f96afb892fb261d5921565b400246",
            corpus.Source.Commit);
        Assert.Equal(38, corpus.Methods.Length);
        Assert.Equal(6, corpus.Queries.Length);
        Assert.Equal(
            27,
            corpus.Queries.Sum(
                static query => query.ReviewedTopK));
        Assert.Equal(
            32,
            corpus.Queries.Sum(
                static query => query.Labels.Length));
        Assert.Equal(
            20,
            corpus.Queries.Sum(query =>
                query.Labels.Count(static label =>
                    label.Relevance
                        == StructuralCloneReviewRelevance.Relevant)));
    }

    [Fact]
    public void Run_PreservesUnreviewedTopKCandidateAsUnknown()
    {
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();
        StructuralCloneCoreLibQuery guid =
            corpus.Queries.Single(
                static query =>
                    query.Id == "guid-relational-operators");
        StructuralCloneCoreLibCorpusReport report =
            StructuralCloneCoreLibCorpus.Run(
                PinnedCoreLib(corpus),
                corpus with
                {
                    Queries = corpus.Queries.Replace(
                        guid,
                        guid with { ReviewedTopK = 4 }),
                });

        StructuralCloneCoreLibQueryResult query =
            report.Queries.Single(
                static result =>
                    result.Id == "guid-relational-operators");
        Assert.False(query.Passed);
        Assert.False(query.TopKFullyReviewed);
        Assert.Equal(4, query.TopCandidates.Length);
        StructuralCloneCoreLibTopCandidate unreviewed =
            query.TopCandidates.Single(
                static candidate =>
                    candidate.Relevance is null);
        Assert.Equal(4, unreviewed.Rank);
        Assert.Null(unreviewed.ActualDisposition);
        Assert.Null(query.PrecisionBasisPoints);
        Assert.Null(report.PrecisionBasisPoints);
        Assert.Contains(
            $"#{unreviewed.Rank} score=",
            StructuralCloneCoreLibCorpus.Format(report));
        Assert.Contains(
            "Unreviewed",
            StructuralCloneCoreLibCorpus.Format(report));
    }

    [Fact]
    public void Run_PreservesComparisonFailureEvidence()
    {
        StructuralCloneCoreLibCorpusDocument corpus = LoadCorpus();
        StructuralCloneCoreLibCorpusReport report =
            StructuralCloneCoreLibCorpus.Run(
                PinnedCoreLib(corpus),
                corpus with
                {
                    Limits = corpus.Limits with
                    {
                        MaximumBlocks = 1,
                    },
                });

        StructuralCloneCoreLibLabelResult failed =
            report.Queries
                .SelectMany(static query => query.Labels)
                .First(static label =>
                    label.ActualDisposition
                        == StructuralCloneDisposition.LimitReached);
        Assert.NotEmpty(failed.ActualBlockers);
        Assert.Contains(
            "comparison blocker",
            StructuralCloneCoreLibCorpus.Format(report));
        Assert.Contains(
            "comparison receipt",
            StructuralCloneCoreLibCorpus.Format(report));
        Assert.Contains(
            report.Queries,
            static query => !query.RetrievalBlockers.IsEmpty);
        Assert.Contains(
            "retrieval blocker",
            StructuralCloneCoreLibCorpus.Format(report));
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
                PinnedCoreLib(corpus),
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

        JsonObject nullMethod = CorpusJson();
        nullMethod["methods"]![0] = null;
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                nullMethod.ToJsonString()));

        JsonObject nullLabel = CorpusJson();
        nullLabel["queries"]![0]!["labels"]![0] = null;
        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                nullLabel.ToJsonString()));

        string duplicateProperty =
            CorpusJson()
                .ToJsonString()
                .Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"schemaVersion\":1",
                    StringComparison.Ordinal);
        Assert.Throws<JsonException>(() =>
            StructuralCloneCoreLibCorpus.Load(
                duplicateProperty));

        string caseAlias =
            CorpusJson()
                .ToJsonString()
                .Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"SchemaVersion\":1",
                    StringComparison.Ordinal);
        Assert.Throws<JsonException>(() =>
            StructuralCloneCoreLibCorpus.Load(caseAlias));
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

    static string PinnedCoreLib(
        StructuralCloneCoreLibCorpusDocument corpus)
    {
        var candidates = new HashSet<string>(
            StringComparer.Ordinal);
        AddCandidate(
            candidates,
            Environment.GetEnvironmentVariable(
                "DOTNET_INSPECT_CORELIB_CORPUS_ARTIFACT"));
        string current = typeof(object).Assembly.Location;
        AddCandidate(candidates, current);
        string? sharedRuntime = Directory.GetParent(
            Path.GetDirectoryName(current)!)?.FullName;
        if (sharedRuntime is not null
            && Directory.Exists(sharedRuntime))
        {
            foreach (string directory
                in Directory.EnumerateDirectories(sharedRuntime))
            {
                AddCandidate(
                    candidates,
                    Path.Combine(
                        directory,
                        corpus.Artifact.FileName));
            }
        }

        string? pinned = candidates.FirstOrDefault(path =>
            StringComparer.Ordinal.Equals(
                Hash(path),
                corpus.Artifact.Sha256));
        Assert.SkipWhen(
            pinned is null,
            $"Pinned CoreLib {corpus.Artifact.Sha256} is not "
                + "installed. Set "
                + "DOTNET_INSPECT_CORELIB_CORPUS_ARTIFACT to run "
                + "the real-artifact corpus gate.");
        return pinned!;
    }

    static void AddCandidate(
        HashSet<string> candidates,
        string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)
            && File.Exists(path))
        {
            candidates.Add(Path.GetFullPath(path));
        }
    }

    static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(
            SHA256.HashData(stream));
    }
}
