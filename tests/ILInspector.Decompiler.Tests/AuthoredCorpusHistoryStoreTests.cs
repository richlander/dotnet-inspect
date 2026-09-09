using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Diagnostics;
using System.Security;
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
    [InlineData("missing-final-lf")]
    [InlineData("blank-record")]
    [InlineData("crlf")]
    [InlineData("non-object-record")]
    [InlineData("multiple-values-one-line")]
    [InlineData("torn-trailing-record")]
    public void StoreVerifier_RejectsMalformedPhysicalFraming(string mutation)
    {
        string valid = AuthoredCorpusHistoryStore.SerializeCanonical(Grandfather());
        string row = valid.TrimEnd('\n');
        string store = mutation switch
        {
            "missing-final-lf" => row,
            "blank-record" => valid + "\n",
            "crlf" => row + "\r\n",
            "non-object-record" => $"[{row}]\n",
            "multiple-values-one-line" => $"{row} {valid}",
            "torn-trailing-record" => valid + """{"date":"2026-08-12","commit":""",
            _ => throw new InvalidOperationException(),
        };

        Assert.ThrowsAny<Exception>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(store, new FakeRepository()));
    }

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

    [Theory]
    [Trait("Area", "Corpus")]
    [InlineData(
        "\"inputsComplete\":true",
        "\"inputsComplete\":true,\"countsAreNonNegative\":false")]
    [InlineData(
        "\"frontierIlDiff\":1",
        "\"frontierIlDiff\":1,\"subBucketSum\":5290")]
    [InlineData(
        "\"compileBackFloor\":0,\"unclassified\":0}}",
        "\"compileBackFloor\":0,\"unclassified\":0,\"sum\":1}}")]
    [InlineData(
        "\"unclassified\":0},\"unsupported\"",
        "\"unclassified\":0,\"countsAreNonNegative\":true},\"unsupported\"")]
    public void StoreVerifier_RejectsIgnoredComputedPropertyNames(
        string existing,
        string forged)
    {
        string row = AuthoredCorpusHistoryStore.SerializeCanonical(
            AuthoredCorpusHistoryStore.Project(Report()));
        string tampered = row.Replace(existing, forged, StringComparison.Ordinal);

        Assert.NotEqual(row, tampered);
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryStore.ParseAndVerify(tampered, new FakeRepository()));
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
    [InlineData("printer-methodology")]
    [InlineData("printer-summary")]
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
            "printer-methodology" => report with { PrinterComparisonVersion = 999 },
            "printer-summary" => report with
            {
                PrinterExact = 1,
                PrinterNotRecorded = 0,
            },
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
    [InlineData("outcome", "Bogus")]
    [InlineData("outcome", "1")]
    [InlineData("tasteBucket", "Bogus")]
    [InlineData("compileBackStatus", "Bogus")]
    [InlineData("compileBackStatus", "1")]
    [InlineData("invalidKind", "Bogus")]
    [InlineData("faultIsolation", "Bogus")]
    [InlineData("faultIsolationMethod", "Bogus")]
    [InlineData("supersededFaultIsolation", "Bogus")]
    [InlineData("supersededFaultIsolationMethod", "Bogus")]
    [InlineData("printerExact", "Bogus")]
    [InlineData("qualityContract", "Bogus")]
    public void AppendProjection_RejectsUnknownEnumShapedFactsWithMatchingSummaries(
        string field,
        string value)
    {
        AuthoredCorpusBenchmark.Report report = Report();
        var rows = report.Rows.ToArray();
        report = field switch
        {
            "outcome" => report with
            {
                Correct = 0,
                UnknownOutcome = 1,
                Rows = Replace(rows, 0, rows[0] with
                {
                    Outcome = value,
                    TasteBucket = "UnknownOutcome",
                }),
            },
            "tasteBucket" => report with
            {
                Rows = Replace(rows, 0, rows[0] with { TasteBucket = value }),
            },
            "compileBackStatus" => report with
            {
                ValidBreakdown = report.ValidBreakdown with
                {
                    FrontierIlExact = 0,
                    FrontierIlNoVerdict = 1,
                },
                Rows = Replace(rows, 1, rows[1] with
                {
                    TasteBucket = "FrontierIlNoVerdict",
                    CompileBackStatus = value,
                }),
            },
            "invalidKind" => report with
            {
                Rows = Replace(rows, 3, rows[3] with { InvalidKind = value }),
            },
            "faultIsolation" => report with
            {
                InvalidBreakdown = new AuthoredCorpusBenchmark.InvalidBreakdownReport(
                    ProductBodyDefect: 0,
                    HarnessShellReconstruction: 0,
                    Unclassified: 1),
                Rows = Replace(rows, 3, rows[3] with
                {
                    InvalidKind = "Unclassified",
                    FaultIsolation = value,
                }),
            },
            "faultIsolationMethod" => report with
            {
                ValidBreakdown = report.ValidBreakdown with
                {
                    FrontierIlDiffAttribution =
                        report.ValidBreakdown.FrontierIlDiffAttribution with
                        {
                            ProductBodyDefect = 0,
                            Unclassified = 1,
                        },
                },
                Rows = Replace(rows, 2, rows[2] with { FaultIsolationMethod = value }),
            },
            "supersededFaultIsolation" => report with
            {
                Rows = Replace(rows, 0, rows[0] with { SupersededFaultIsolation = value }),
            },
            "supersededFaultIsolationMethod" => report with
            {
                Rows = Replace(rows, 0, rows[0] with
                {
                    SupersededFaultIsolationMethod = value,
                }),
            },
            "printerExact" => report with
            {
                Rows = Replace(rows, 0, rows[0] with { PrinterExact = value }),
            },
            "qualityContract" => report with { QualityContract = value },
            _ => throw new InvalidOperationException(),
        };

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(() => AuthoredCorpusHistoryStore.Project(report));
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
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
    public void RepositoryBuildStateTarget_DistinguishesCleanDirtyAndClean()
    {
        string directory = CreateTemporaryDirectory("repository-build-state");
        try
        {
            string target = SecurityElement.Escape(Path.Combine(
                AuthoredCorpusRatchetTests.FindRepositoryRoot(),
                "tools",
                "DecompilerHarness",
                "RepositoryBuildState.targets"))!;
            File.WriteAllText(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
            File.WriteAllText(
                Path.Combine(directory, "Program.cs"),
                "System.Console.WriteLine(\"probe\");\n");
            string project = Path.Combine(directory, "probe.csproj");
            File.WriteAllText(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <Import Project="{{target}}" />
                </Project>
                """);

            RunCommand(directory, "git", "init", "--quiet");
            CommitAll(directory, "initial");

            AssertBuildState(directory, project, "clean");
            string marker = Path.Combine(directory, "untracked.txt");
            File.WriteAllText(marker, "dirty\n");
            AssertBuildState(directory, project, "dirty");
            File.Delete(marker);
            AssertBuildState(directory, project, "clean");
        }
        finally
        {
            DeleteDirectory(directory);
        }
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
    public void AppendProjection_RejectsPrinterExactOutsideCorrect()
    {
        AuthoredCorpusBenchmark.Report report = Report();
        var rows = report.Rows.ToArray();
        rows[3] = rows[3] with { PrinterExact = PrinterExactOutcome.Exact.ToString() };

        Assert.Throws<InvalidDataException>(
            () => AuthoredCorpusHistoryStore.Project(report with { Rows = rows }));
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
    public void GitRepository_RejectsBenchmarklessNonMainAndDuplicateMethodologyCommits()
    {
        string directory = CreateTemporaryDirectory("history-git-repository");
        try
        {
            RunCommand(directory, "git", "init", "--quiet");
            File.WriteAllText(Path.Combine(directory, "README.md"), "root\n");
            CommitAll(directory, "root");
            string benchmarkless = RunCommand(directory, "git", "rev-parse", "HEAD").Trim();
            RunCommand(
                directory,
                "git",
                "update-ref",
                "refs/remotes/origin/main",
                benchmarkless);

            string harnessDirectory = Path.Combine(directory, "tools", "DecompilerHarness");
            Directory.CreateDirectory(harnessDirectory);
            File.WriteAllText(
                Path.Combine(harnessDirectory, "AuthoredCorpusBenchmark.cs"),
                "internal static class AuthoredCorpusBenchmark { }\n");
            File.WriteAllText(
                Path.Combine(harnessDirectory, "AuthoredCorpusMethodology.cs"),
                """
                internal static class AuthoredCorpusMethodology
                {
                    internal const int Version = 3;
                }
                """);
            CommitAll(directory, "benchmark");
            string branchOnly = RunCommand(directory, "git", "rev-parse", "HEAD").Trim();

            var repository = new AuthoredCorpusHistoryStore.GitRepository(directory);
            Assert.Throws<InvalidDataException>(
                () => repository.MethodologyAt(benchmarkless));
            Assert.False(repository.IsOnMain(branchOnly));
            Assert.Equal(3, repository.MethodologyAt(branchOnly));

            File.WriteAllText(
                Path.Combine(harnessDirectory, "SpanAttribution.cs"),
                """
                internal static class SpanAttribution
                {
                    internal const int MethodologyVersion = 2;
                }
                """);
            CommitAll(directory, "duplicate methodology");
            string duplicate = RunCommand(directory, "git", "rev-parse", "HEAD").Trim();

            Assert.Throws<InvalidDataException>(() => repository.MethodologyAt(duplicate));

            File.Delete(Path.Combine(harnessDirectory, "AuthoredCorpusMethodology.cs"));
            File.Delete(Path.Combine(harnessDirectory, "SpanAttribution.cs"));
            File.WriteAllText(
                Path.Combine(harnessDirectory, "AuthoredCorpusBenchmark.cs"),
                """
                internal static class AuthoredCorpusBenchmark
                {
                    internal const int MethodologyVersion = 2;
                    internal const int MethodologyVersion = 3;
                }
                """);
            CommitAll(directory, "duplicate benchmark methodology");
            string duplicateBenchmark = RunCommand(directory, "git", "rev-parse", "HEAD").Trim();

            Assert.Throws<InvalidDataException>(
                () => repository.MethodologyAt(duplicateBenchmark));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    [Trait("Area", "Corpus")]
    public void TrackedHistory_VerifiesUnchangedWithoutRequiringADeepCheckout()
    {
        string path = AuthoredCorpusHistoryCardTests.TrackedHistoryPath();

        IReadOnlyList<HistoryRun> runs =
            AuthoredCorpusHistoryStore.ParseAndVerify(File.ReadAllText(path), new TrackedRepository());

        Assert.Equal(12, runs.Count);
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
            PrinterComparisonVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion,
            PrinterExact: 0,
            PrinterDifferent: 0,
            PrinterNotRecorded: 1,
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
            SourceOracleManifest: null,
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
            SourceFile: null,
            PrinterExact: PrinterExactOutcome.NotRecorded.ToString());

    static IReadOnlyList<AuthoredCorpusBenchmark.RowReport> Replace(
        AuthoredCorpusBenchmark.RowReport[] rows,
        int index,
        AuthoredCorpusBenchmark.RowReport replacement)
    {
        rows[index] = replacement;
        return rows;
    }

    static string CreateTemporaryDirectory(string purpose)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static void DeleteDirectory(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(
            directory,
            "*",
            SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    static void CommitAll(string directory, string message)
    {
        RunCommand(directory, "git", "add", ".");
        RunCommand(
            directory,
            "git",
            "-c",
            "user.name=dotnet-inspect-tests",
            "-c",
            "user.email=dotnet-inspect-tests@example.invalid",
            "commit",
            "--quiet",
            "-m",
            message);
    }

    static void AssertBuildState(string directory, string project, string expected)
    {
        RunCommand(directory, "dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        string generated = File.ReadAllText(Path.Combine(
            directory,
            "obj",
            "Release",
            "net11.0",
            "RepositoryBuildState.g.cs"));

        Assert.Contains($"\"{expected}\"", generated, StringComparison.Ordinal);
    }

    static string RunCommand(string directory, string command, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Assert.True(
            process.WaitForExit(milliseconds: 120_000),
            $"{command} did not exit within two minutes.");
        string standardOutput = output.GetAwaiter().GetResult();
        string standardError = error.GetAwaiter().GetResult();
        Assert.True(
            process.ExitCode == 0,
            $"{command} {string.Join(' ', arguments)} failed ({process.ExitCode}):"
                + $"{Environment.NewLine}{standardOutput}{standardError}");
        return standardOutput;
    }

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

        static readonly HashSet<string> VersionThreeCommits =
        [
            "56f8cef5831bd969a42196b5999125f982006913",
            "96be1b3d695cb5d1286938c6df95cc38ec5f3a30",
        ];

        public string ResolveCommit(string commit) => commit;

        public bool IsOnMain(string commit) => true;

        public int MethodologyAt(string commit)
        {
            if (VersionThreeCommits.Contains(commit))
                return 3;

            return VersionTwoCommits.Contains(commit) ? 2 : 1;
        }
    }
}
