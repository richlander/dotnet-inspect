using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;

using ILInspector.Analysis;
using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneCorpusTests
{
    [Fact]
    public void CommittedRelationshipCorpus_GradesPublicProductComparator()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            corpus);

        Assert.True(
            report.Success,
            StructuralCloneCorpus.Format(report));
        Assert.Equal(10, report.Total);
        Assert.True(report.Discovery.Passed);
        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Completed,
            report.Discovery.Disposition);
        Assert.Equal(4, report.Discovery.ExpectedClusters.Length);
        Assert.Equal(4, report.Discovery.ActualClusters.Length);
        Assert.True(report.Retrieval.Passed);
        Assert.Equal(3, report.Retrieval.Queries.Length);
        Assert.All(
            report.Retrieval.Queries,
            static query => Assert.True(query.Passed));
        Assert.Contains(
            "Completed/Near (Unique alignment): edits blocks +0/-0/~1, operations +0/-0/~1, edges +0/-0/~0",
            StructuralCloneCorpus.Format(report));
        Assert.Contains(
            "PASS fuzzy retrieval near-constant-peer",
            StructuralCloneCorpus.Format(report));
    }

    [Fact]
    public void Run_RejectsMalformedManagedMetadata()
    {
        byte[] bytes =
            File.ReadAllBytes(typeof(StructuralCloneFixture).Assembly.Location);
        using (var image = new PEReader(new MemoryStream(bytes)))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    image.PEHeaders.CorHeaderStartOffset
                        + 3 * sizeof(uint)),
                uint.MaxValue);
        }
        string path = Path.Combine(
            Path.GetTempPath(),
            $"structural-clone-malformed-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(
                () => StructuralCloneCorpus.Run(path, LoadCorpus()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CommittedRelationshipCorpus_CoversFixtureInventory()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        string[] corpusMethods =
        [
            .. corpus.Cases
                .SelectMany(static item =>
                    new[]
                    {
                        $"{item.Left.Type}.{item.Left.Method}",
                        $"{item.Right.Type}.{item.Right.Method}",
                    })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] fixtureMethods =
        [
            .. typeof(StructuralCloneFixture)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Select(static method =>
                    $"{method.DeclaringType!.FullName}.{method.Name}")
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(fixtureMethods, corpusMethods);
        string[] discoveryMethods =
        [
            .. corpus.Discovery.Population
                .Select(static method =>
                    $"{method.Type}.{method.Method}")
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(fixtureMethods, discoveryMethods);
    }

    [Fact]
    public void Run_ClosedWorldDiscoveryRejectsUndeclaredMerge()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        StructuralCloneCorpusCase exact = corpus.Cases.Single(
            static item => item.Id == "banal.authored.exact");
        StructuralCloneCorpusDocument altered = corpus with
        {
            Cases = corpus.Cases.Replace(
                exact,
                exact with
                {
                    ExpectedRelation = StructuralCloneRelation.Different,
                }),
        };

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            altered);

        Assert.False(report.Discovery.Passed);
        Assert.Equal(3, report.Discovery.ExpectedClusters.Length);
        Assert.Equal(4, report.Discovery.ActualClusters.Length);
    }

    [Fact]
    public void Run_ClosedWorldDiscoveryRejectsMissedFamily()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        StructuralCloneCorpusCase different = corpus.Cases.Single(
            static item =>
                item.Id == "challenging.edge-role.negative");
        StructuralCloneCorpusDocument altered = corpus with
        {
            Cases = corpus.Cases.Replace(
                different,
                different with
                {
                    ExpectedRelation = StructuralCloneRelation.Exact,
                    ExpectedEdits = null,
                }),
        };

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            altered);

        Assert.False(report.Discovery.Passed);
        Assert.Equal(5, report.Discovery.ExpectedClusters.Length);
        Assert.Equal(4, report.Discovery.ActualClusters.Length);
    }

    [Fact]
    public void Run_RetrievalRejectsContrastiveInversion()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        StructuralCloneCorpusRetrievalQuery query =
            corpus.Retrieval.Queries.Single(
                static item => item.Id == "near-constant-peer");
        StructuralCloneCorpusRetrievalExpectation expectation =
            Assert.Single(query.Expectations);
        StructuralCloneCorpusMethod lowerRankedCandidate = new(
            expectation.Candidate.Type,
            nameof(StructuralCloneFixture.NearReorderedA));
        StructuralCloneCorpusDocument altered = corpus with
        {
            Retrieval = corpus.Retrieval with
            {
                Queries = corpus.Retrieval.Queries.Replace(
                    query,
                    query with
                    {
                        Expectations =
                        [
                            expectation with
                            {
                                Candidate = lowerRankedCandidate,
                                ScoresAbove = [expectation.Candidate],
                            },
                        ],
                    }),
            },
        };

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            altered);

        Assert.False(report.Retrieval.Passed);
        Assert.False(
            report.Retrieval.Queries.Single(
                static item => item.Id == "near-constant-peer").Passed);
    }

    [Fact]
    public void Run_RetrievalRejectsContrastiveScoreTie()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        StructuralCloneCorpusRetrievalQuery query =
            corpus.Retrieval.Queries.Single(
                static item => item.Id == "near-call-target-peer");
        string type = query.Seed.Type;
        StructuralCloneCorpusDocument altered = corpus with
        {
            Retrieval = corpus.Retrieval with
            {
                Queries = corpus.Retrieval.Queries.Replace(
                    query,
                    query with
                    {
                        Expectations =
                        [
                            new StructuralCloneCorpusRetrievalExpectation(
                                new StructuralCloneCorpusMethod(
                                    type,
                                    nameof(StructuralCloneFixture
                                        .NearHardNegativeA)),
                                MaximumRank: 20,
                                ScoresAbove:
                                [
                                    new StructuralCloneCorpusMethod(
                                        type,
                                        nameof(StructuralCloneFixture
                                            .NearHardNegativeB)),
                                ]),
                        ],
                    }),
            },
        };

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            altered);

        StructuralCloneCorpusRetrievalExpectationResult result =
            Assert.Single(
                report.Retrieval.Queries.Single(
                    static item => item.Id == "near-call-target-peer")
                    .Expectations);
        Assert.NotNull(result.Actual);
        Assert.Equal(
            Assert.Single(result.Contrasts).Similarity.Score,
            result.Actual.Similarity.Score);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Load_RejectsRelationOnUnsupportedCase()
    {
        const string Invalid =
            """
            {
              "schemaVersion": 4,
              "cases": [{
                "id": "invalid",
                "left": { "type": "T", "method": "A" },
                "right": { "type": "T", "method": "B" },
                "expectedDisposition": "Unsupported",
                "expectedRelation": "Different",
                "difficulty": "banal",
                "intent": "unsupported-boundary",
                "actionability": "none",
                "tags": []
              }],
              "discovery": {
                "population": [
                  { "type": "T", "method": "A" },
                  { "type": "T", "method": "B" }
                ]
              },
              "retrieval": {
                "queries": [{
                  "id": "query",
                  "seed": { "type": "T", "method": "A" },
                  "expectations": [{
                    "candidate": { "type": "T", "method": "B" },
                    "maximumRank": 1,
                    "scoresAbove": []
                  }]
                }]
              }
            }
            """;

        Assert.Throws<InvalidDataException>(
            () => StructuralCloneCorpus.Load(Invalid));
    }

    [Fact]
    public void Load_RejectsDuplicateRetrievalQueryIds()
    {
        JsonObject root = CorpusJson();
        JsonArray queries =
            root["retrieval"]!["queries"]!.AsArray();
        queries[1]!["id"] =
            queries[0]!["id"]!.GetValue<string>();

        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCorpus.Load(root.ToJsonString()));
    }

    [Fact]
    public void Load_RejectsDuplicateRetrievalCandidates()
    {
        JsonObject root = CorpusJson();
        JsonArray expectations =
            root["retrieval"]!["queries"]![0]!
                ["expectations"]!.AsArray();
        expectations.Add(expectations[0]!.DeepClone());

        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCorpus.Load(root.ToJsonString()));
    }

    [Fact]
    public void Load_RejectsNonPositiveRetrievalRank()
    {
        JsonObject root = CorpusJson();
        root["retrieval"]!["queries"]![0]!
            ["expectations"]![0]!["maximumRank"] = 0;

        Assert.Throws<InvalidDataException>(() =>
            StructuralCloneCorpus.Load(root.ToJsonString()));
    }

    [Fact]
    public void Load_RequiresRetrievalContrasts()
    {
        JsonObject root = CorpusJson();
        root["retrieval"]!["queries"]![0]!
            ["expectations"]![0]!
            .AsObject()
            .Remove("scoresAbove");

        Assert.Throws<JsonException>(() =>
            StructuralCloneCorpus.Load(root.ToJsonString()));
    }

    [Theory]
    [MemberData(nameof(MalformedLedgers))]
    public void Load_RejectsIncompleteOrPermissiveJson(string json)
        => Assert.Throws<JsonException>(
            () => StructuralCloneCorpus.Load(json));

    public static TheoryData<string> MalformedLedgers =>
        new()
        {
            {
                """
                {
                  "schemaVersion": 1,
                  "cases": [{
                    "id": "missing-disposition",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
            {
                """
                {
                  "schemaVersion": 1,
                  "unknown": true,
                  "cases": [{
                    "id": "unknown-property",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedDisposition": "Completed",
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
            {
                """
                {
                  "schemaVersion": 1,
                  "cases": [{
                    "id": "integer-enum",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedDisposition": 0,
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
        };

    [Fact]
    public async Task Command_RejectsMissingRelationshipLedgerValue()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCorpus).Assembly.Location);
        start.ArgumentList.Add("--clone-corpus");
        start.ArgumentList.Add(
            typeof(StructuralCloneFixture).Assembly.Location);
        start.ArgumentList.Add("--relationship-ledger");
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
            "--relationship-ledger requires a file path.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    static StructuralCloneCorpusDocument LoadCorpus()
    {
        return StructuralCloneCorpus.Load(
            CorpusJson().ToJsonString());
    }

    static JsonObject CorpusJson()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-relationships.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
