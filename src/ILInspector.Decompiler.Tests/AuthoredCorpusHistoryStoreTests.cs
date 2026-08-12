using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class AuthoredCorpusHistoryStoreTests
{
    const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static readonly string Digest = new('b', 64);

    [Fact]
    [Trait("Area", "Corpus")]
    public void ProducerReport_ProjectsToOneDeterministicCanonicalRow()
    {
        string producerJson = AuthoredCorpusBenchmark.SerializeReport(Report());
        AuthoredCorpusBenchmark.Report report =
            AuthoredCorpusHistoryStore.ParseBenchmarkReport(producerJson);

        HistoryRun row = AuthoredCorpusHistoryStore.Project(report);
        string first = AuthoredCorpusHistoryStore.SerializeCanonical(row);
        string second = AuthoredCorpusHistoryStore.SerializeCanonical(row);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
        Assert.Equal(1, first.Count(character => character == '\n'));
        Assert.Equal(75.0, row.ValidPct);
        Assert.Equal(4, row.Evaluated);
        Assert.Equal(2, row.ValidDifferent!.Total);
        Assert.Equal(1, row.ValidDifferent.FrontierIlDiffAttribution!.ProductBodyDefect);

        using JsonDocument document = JsonDocument.Parse(first);
        Assert.Equal(Commit, document.RootElement.GetProperty("commit").GetString());
        Assert.False(document.RootElement.TryGetProperty("sweepManifestSha256", out _));
        Assert.False(document.RootElement.TryGetProperty("methodology", out _));
        Assert.False(document.RootElement.TryGetProperty("topLevelSum", out _));
        Assert.False(
            document.RootElement.GetProperty("validDifferent").TryGetProperty("subBucketSum", out _));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void AppendedProjection_PassesTheSameCompleteStoreVerifier()
    {
        var repository = new FakeRepository();
        HistoryRun row = AuthoredCorpusHistoryStore.Project(Report());
        string store = AuthoredCorpusHistoryStore.SerializeCanonical(Grandfather())
            + AuthoredCorpusHistoryStore.SerializeCanonical(row);

        IReadOnlyList<HistoryRun> parsed = AuthoredCorpusHistoryStore.ParseAndVerify(store, repository);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(Commit, parsed[1].Commit);
    }

    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("{}")]
    [InlineData("{}\n\n")]
    [InlineData("{}\r\n")]
    [InlineData("[]\n")]
    [InlineData("{} {}\n")]
    public void StoreVerifier_RejectsMalformedPhysicalFraming(string store)
        => Assert.ThrowsAny<Exception>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(store, new FakeRepository()));

    [Fact]
    [Trait("Area", "Corpus")]
    public void StoreVerifier_RejectsDuplicateAndUnknownSchemaMembers()
    {
        string row = AuthoredCorpusHistoryStore.SerializeCanonical(Grandfather()).TrimEnd('\n');
        string duplicate = row.Replace(
            "\"date\":\"2026-07-20\"",
            "\"date\":\"2026-07-20\",\"date\":\"2026-07-20\"",
            StringComparison.Ordinal);
        string unknown = row[..^1] + ",\"invented\":0}";

        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(duplicate + "\n", new FakeRepository()));
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(unknown + "\n", new FakeRepository()));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void StoreVerifier_RejectsMissingRequiredCountsAndGrandfatherTampering()
    {
        string row = AuthoredCorpusHistoryStore.SerializeCanonical(
            AuthoredCorpusHistoryStore.Project(Report()));
        string missingPool = row.Replace("\"poolMatched\":2,", "", StringComparison.Ordinal);
        string grandfather = AuthoredCorpusHistoryStore.SerializeCanonical(
            Grandfather() with { Correct = 1502 });

        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(
                AuthoredCorpusHistoryStore.SerializeCanonical(Grandfather()) + missingPool,
                new FakeRepository()));
        Assert.Throws<InvalidDataException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(grandfather, new FakeRepository()));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void BenchmarkParser_RejectsDuplicateUnknownAndMissingMembers()
    {
        string json = AuthoredCorpusBenchmark.SerializeReport(Report());
        string duplicate = json.Replace(
            "\"corpusRows\": 4",
            "\"corpusRows\": 4,\n  \"corpusRows\": 4",
            StringComparison.Ordinal);
        string unknown = json.Replace(
            "\"corpusRows\": 4",
            "\"invented\": 0,\n  \"corpusRows\": 4",
            StringComparison.Ordinal);
        JsonObject missingDocument = JsonNode.Parse(json)!.AsObject();
        Assert.True(missingDocument.Remove("corpusRows"));
        string missing = missingDocument.ToJsonString();

        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseBenchmarkReport(duplicate));
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseBenchmarkReport(unknown));
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseBenchmarkReport(missing));
    }

    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("dirty")]
    [InlineData("dirty-build")]
    [InlineData("unknown-build")]
    [InlineData("incomplete")]
    [InlineData("partition")]
    [InlineData("methodology")]
    public void AppendProjection_RejectsRepresentativeArtifactTampering(string tamper)
    {
        AuthoredCorpusBenchmark.Report report = Report();
        report = tamper switch
        {
            "dirty" => report with { SourceDirty = true },
            "dirty-build" => report with { SourceStateAtBuild = "dirty" },
            "unknown-build" => report with { SourceStateAtBuild = "unknown" },
            "incomplete" => report with { UnmatchedRows = 1 },
            "partition" => report with
            {
                ValidBreakdown = report.ValidBreakdown with { FrontierIlExact = 0 },
            },
            "methodology" => report with { MethodologyVersion = 999 },
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<InvalidDataException>(() => AuthoredCorpusHistoryStore.Project(report));
    }

    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("BodyDefect", null, "ProductBodyDefect")]
    [InlineData("ShellOrClosureDefect", null, "HarnessShellReconstruction")]
    [InlineData(null, "closure-stalled: unresolved member", "HarnessShellReconstruction")]
    [InlineData(null, "closure-root-budget: 64", "HarnessShellReconstruction")]
    [InlineData(null, "compiler diagnostic", "Unclassified")]
    public void InvalidKind_IsDerivedFromFaultIsolationAndDetail(
        string? faultIsolation,
        string? detail,
        string expected)
    {
        AuthoredCorpusBenchmark.RowReport row = Row("Invalid") with
        {
            FaultIsolation = faultIsolation,
            Detail = detail,
        };

        Assert.Equal(expected, AuthoredCorpusHistoryStore.ClassifyInvalidKind(row));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void AppendProjection_RejectsForgedInvalidKindAndMatchingSummary()
    {
        AuthoredCorpusBenchmark.Report report = Report();
        var rows = report.Rows.ToArray();
        rows[3] = rows[3] with { InvalidKind = "HarnessShellReconstruction" };
        report = report with
        {
            Rows = rows,
            InvalidBreakdown = new AuthoredCorpusBenchmark.InvalidBreakdownReport(
                ProductBodyDefect: 0,
                HarnessShellReconstruction: 1,
                Unclassified: 0),
        };

        Assert.Throws<InvalidDataException>(() => AuthoredCorpusHistoryStore.Project(report));
    }

    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData("clean")]
    [InlineData("dirty")]
    [InlineData("unknown")]
    public void BuildSourceState_RecognizesTheThreeProducerStates(string state)
    {
        var attributes = new[] { new AssemblyMetadataAttribute("RepositorySourceStateAtBuild", state) };

        Assert.Equal(state, AuthoredCorpusHistoryStore.ReadSourceStateAtBuild(attributes));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void BuildSourceState_RejectsMissingInvalidAndDuplicateMetadata()
    {
        Assert.Throws<InvalidOperationException>(
            () => AuthoredCorpusHistoryStore.ReadSourceStateAtBuild([]));
        Assert.Throws<InvalidOperationException>(
            () => AuthoredCorpusHistoryStore.ReadSourceStateAtBuild(
                [new AssemblyMetadataAttribute("RepositorySourceStateAtBuild", "invented")]));
        Assert.Throws<InvalidOperationException>(
            () => AuthoredCorpusHistoryStore.ReadSourceStateAtBuild(
                [
                    new AssemblyMetadataAttribute("RepositorySourceStateAtBuild", "clean"),
                    new AssemblyMetadataAttribute("RepositorySourceStateAtBuild", "dirty"),
                ]));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void AppendProjection_RecomputesTasteFromTheProducerFacts()
    {
        AuthoredCorpusBenchmark.Report report = Report();
        var rows = report.Rows.ToArray();
        rows[1] = rows[1] with { CompileBackStatus = "OpcodeDiff" };
        report = report with { Rows = rows };

        Assert.Throws<InvalidDataException>(() => AuthoredCorpusHistoryStore.Project(report));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void StoreVerifier_RejectsNonMainAndBenchmarklessCommits()
    {
        HistoryRun row = AuthoredCorpusHistoryStore.Project(Report());
        string store = AuthoredCorpusHistoryStore.SerializeCanonical(Grandfather())
            + AuthoredCorpusHistoryStore.SerializeCanonical(row);

        Assert.Throws<InvalidDataException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(
                store,
                new FakeRepository { OnMain = false }));
        Assert.Throws<InvalidDataException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(
                store,
                new FakeRepository { BenchmarkExists = false }));
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void TrackedHistory_VerifiesUnchangedWithoutRequiringADeepCheckout()
    {
        string path = AuthoredCorpusHistoryCardTests.TrackedHistoryPath();

        IReadOnlyList<HistoryRun> runs =
            AuthoredCorpusHistoryStore.ParseAndVerify(File.ReadAllText(path), new TrackedRepository());

        Assert.Equal(10, runs.Count);
    }

    static AuthoredCorpusBenchmark.Report Report()
        => new(
            Date: "2026-08-11",
            Commit,
            SourceStateAtBuild: "clean",
            SourceRevisionMatchesHead: true,
            SourceDirty: false,
            CorpusRows: 4,
            MatchedAssemblies: 2,
            CorpusAssemblies: 2,
            UnmatchedRows: 0,
            MalformedRows: 0,
            PoolSha256: Digest,
            CorpusSha256: Digest,
            TargetsEvaluated: 4,
            MethodologyVersion: AuthoredCorpusMethodology.Version,
            InputsComplete: true,
            QualityContract: "Perfection",
            Correct: 1,
            ValidDifferent: 2,
            ValidBreakdown: new AuthoredCorpusBenchmark.ValidBreakdownReport(
                Total: 2,
                Lowering: 0,
                KnownTaste: 0,
                FrontierIlExact: 1,
                FrontierIlDiff: 1,
                FrontierIlDiffAttribution: new AuthoredCorpusBenchmark.FrontierIlDiffAttributionReport(
                    Total: 1,
                    ProductBodyDefect: 1,
                    HarnessShellReconstruction: 0,
                    CompileBackFloor: 0,
                    Unclassified: 0),
                FrontierIlNoVerdict: 0),
            Invalid: 1,
            InvalidBreakdown: new AuthoredCorpusBenchmark.InvalidBreakdownReport(
                ProductBodyDefect: 1,
                HarnessShellReconstruction: 0,
                Unclassified: 0),
            NotFull: 0,
            Drift: 0,
            Unsupported: 0,
            UnknownOutcome: 0,
            Ratchet: null,
            Rows:
            [
                Row("Correct"),
                Row("FrontierIlExact"),
                Row("FrontierIlDiff"),
                Row("Invalid"),
            ]);

    static AuthoredCorpusBenchmark.RowReport Row(string taste)
        => new(
            Type: "Example.Type",
            Method: "M",
            Overload: 0,
            Outcome: taste switch
            {
                "Correct" => "ValidMatch",
                "Invalid" => "Invalid",
                _ => "ValidDifferent",
            },
            TasteBucket: taste,
            CompileBackStatus: taste switch
            {
                "FrontierIlExact" => "Exact",
                "FrontierIlDiff" => "OpcodeDiff",
                _ => null,
            },
            InvalidKind: taste == "Invalid" ? "ProductBodyDefect" : null,
            FaultIsolation: taste is "FrontierIlDiff" or "Invalid" ? "BodyDefect" : null,
            FaultIsolationMethod: taste is "FrontierIlDiff" or "Invalid"
                ? "FidelityControl"
                : null,
            UsedCompileBackFloor: false,
            SupersededFaultIsolation: null,
            SupersededFaultIsolationMethod: null,
            Reason: "test",
            Detail: null,
            SourceFile: null);

    static HistoryRun Grandfather()
        => new(
            Date: "2026-07-20",
            Commit: null,
            PoolMatched: 26,
            PoolTotal: 26,
            Evaluated: 12000,
            ValidPct: 56.6,
            Correct: 1501,
            ValidDifferent: new HistoryRunValidDifferent(
                Total: 5290,
                FrontierIlExact: 3097,
                FrontierIlDiff: 2181),
            Invalid: 5209,
            InvalidBreakdown: null,
            Unsupported: 0,
            Drift: 0,
            InputsComplete: true,
            SweepManifestSha256: null);

    sealed class FakeRepository : AuthoredCorpusHistoryStore.IRepository
    {
        public bool OnMain { get; init; } = true;
        public bool BenchmarkExists { get; init; } = true;

        public string ResolveCommit(string commit) => commit;

        public bool IsOnMain(string commit) => OnMain;

        public int MethodologyAt(string commit)
            => BenchmarkExists
                ? AuthoredCorpusMethodology.Version
                : throw new InvalidDataException("benchmark missing");
    }

    sealed class TrackedRepository : AuthoredCorpusHistoryStore.IRepository
    {
        static readonly HashSet<string> VersionTwoCommits =
        [
            "14781e8d",
            "d4002cf1",
            "35014d91",
            "168464d9",
            "50046669",
        ];

        public string ResolveCommit(string commit) => commit;

        public bool IsOnMain(string commit) => true;

        public int MethodologyAt(string commit) => VersionTwoCommits.Contains(commit) ? 2 : 1;
    }
}
