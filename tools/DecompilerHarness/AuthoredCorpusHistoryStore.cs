using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Projects complete authored-corpus benchmark reports into the compact EVIL
/// history schema and verifies the tracked JSONL store.
/// </summary>
static partial class AuthoredCorpusHistoryStore
{
    internal sealed record BenchmarkProvenance(
        string Date,
        string Commit,
        string SourceStateAtBuild,
        bool SourceRevisionMatchesHead,
        bool SourceDirty);

    internal interface IRepository
    {
        string ResolveCommit(string commit);
        bool IsOnMain(string commit);
        int MethodologyAt(string commit);
    }

    internal sealed class GitRepository(string root) : IRepository
    {
        public string Root { get; } = root;

        public static GitRepository OpenCurrent()
        {
            string root = RunGit(Environment.CurrentDirectory, "rev-parse", "--show-toplevel").Trim();
            return new GitRepository(root);
        }

        public string ResolveCommit(string commit)
            => RunGit(Root, "rev-parse", "--verify", "--end-of-options", $"{commit}^{{commit}}").Trim();

        public bool IsOnMain(string commit)
            => RunGitForExitCode(Root, "merge-base", "--is-ancestor", commit, "origin/main") == 0;

        public int MethodologyAt(string commit)
        {
            if (!TryShow(commit, "tools/DecompilerHarness/AuthoredCorpusBenchmark.cs", out string benchmark))
            {
                throw new InvalidDataException(
                    $"Recorded commit '{commit}' does not contain the authored-corpus benchmark.");
            }

            var owners = new List<(string Path, int Version)>();
            const string methodologyPath = "tools/DecompilerHarness/AuthoredCorpusMethodology.cs";
            bool hasMethodologyOwner = TryShow(commit, methodologyPath, out string methodologyContent);
            if (hasMethodologyOwner)
            {
                AddNumericOwner(
                    commit,
                    methodologyPath,
                    methodologyContent,
                    MethodologyVersionPattern(),
                    requireMatch: true,
                    owners);
            }
            AddNumericOwner(
                commit,
                "tools/DecompilerHarness/SpanAttribution.cs",
                LegacyMethodologyVersionPattern(),
                requireMatch: false,
                owners);

            AddNumericOwner(
                commit,
                "tools/DecompilerHarness/AuthoredCorpusBenchmark.cs",
                benchmark,
                LegacyMethodologyVersionPattern(),
                requireMatch: false,
                owners);

            return owners.Count switch
            {
                0 => 1,
                1 => owners[0].Version,
                _ => throw new InvalidDataException(
                    $"Recorded commit '{commit}' has duplicate methodology owners: "
                    + string.Join(", ", owners.Select(owner => owner.Path))),
            };
        }

        void AddNumericOwner(
            string commit,
            string path,
            Regex pattern,
            bool requireMatch,
            List<(string Path, int Version)> owners)
        {
            if (!TryShow(commit, path, out string content))
                return;

            AddNumericOwner(commit, path, content, pattern, requireMatch, owners);
        }

        static void AddNumericOwner(
            string commit,
            string path,
            string content,
            Regex pattern,
            bool requireMatch,
            List<(string Path, int Version)> owners)
        {
            MatchCollection matches = pattern.Matches(content);
            if (matches.Count > 1)
            {
                throw new InvalidDataException(
                    $"Recorded commit '{commit}' declares methodology more than once in '{path}'.");
            }

            if (requireMatch && matches.Count == 0)
            {
                throw new InvalidDataException(
                    $"Recorded commit '{commit}' has an unrecognized methodology owner in '{path}'.");
            }

            if (matches.Count == 1)
            {
                owners.Add((
                    path,
                    int.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture)));
            }
        }

        bool TryShow(string commit, string path, out string content)
        {
            var result = RunGitResult(Root, "show", $"{commit}:{path}");
            content = result.Output;
            return result.ExitCode == 0;
        }
    }

    internal static BenchmarkProvenance CaptureBenchmarkProvenance()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? throw new InvalidOperationException("The harness assembly has no informational version.");
        int separator = informationalVersion.LastIndexOf('+');
        string commit = separator >= 0 ? informationalVersion[(separator + 1)..] : "";
        if (!FullCommitPattern().IsMatch(commit))
        {
            throw new InvalidOperationException(
                $"The harness informational version does not carry a full Git commit: '{informationalVersion}'.");
        }

        var repository = GitRepository.OpenCurrent();
        string head = repository.ResolveCommit("HEAD");
        bool sourceRevisionMatchesHead = string.Equals(commit, head, StringComparison.Ordinal);
        bool sourceDirty = RunGit(repository.Root, "status", "--porcelain", "--untracked-files=all").Length != 0;
        string sourceStateAtBuild = ReadSourceStateAtBuild(
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>());

        return new BenchmarkProvenance(
            DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            commit,
            sourceStateAtBuild,
            sourceRevisionMatchesHead,
            sourceDirty);
    }

    internal static string ReadSourceStateAtBuild(IEnumerable<AssemblyMetadataAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        string[] values = attributes
            .Where(attribute => attribute.Key == "RepositorySourceStateAtBuild")
            .Select(attribute => attribute.Value ?? "")
            .ToArray();
        if (values.Length != 1 || values[0] is not ("clean" or "dirty" or "unknown"))
        {
            throw new InvalidOperationException(
                "The harness assembly does not carry one recognized repository source state.");
        }

        return values[0];
    }

    public static int Append(string artifactPath, string? historyPath)
    {
        string path = historyPath ?? AuthoredCorpusHistoryCard.DefaultHistoryRelativePath;
        try
        {
            var repository = GitRepository.OpenCurrent();
            string existing = File.ReadAllText(path);
            IReadOnlyList<HistoryRun> runs = ParseAndVerify(existing, repository);
            AuthoredCorpusBenchmark.Report report = ParseBenchmarkReport(File.ReadAllText(artifactPath));
            HistoryRun row = Project(report);
            VerifyRun(row, repository, allowGrandfather: false);

            var combined = runs.Append(row).ToArray();
            VerifyRuns(combined, repository);
            File.AppendAllText(path, SerializeCanonical(row));
            Console.WriteLine($"Appended EVIL history row for {row.Commit} ({row.Date}) to {path}.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not append EVIL history: {ex.Message}");
            return 1;
        }
    }

    public static int Verify(string? historyPath)
    {
        string path = historyPath ?? AuthoredCorpusHistoryCard.DefaultHistoryRelativePath;
        try
        {
            var repository = GitRepository.OpenCurrent();
            IReadOnlyList<HistoryRun> runs = ParseAndVerify(File.ReadAllText(path), repository);
            Console.WriteLine($"Verified {runs.Count} EVIL history rows in {path}.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            Console.Error.WriteLine($"EVIL history verification failed: {ex.Message}");
            return 1;
        }
    }

    internal static AuthoredCorpusBenchmark.Report ParseBenchmarkReport(string json)
    {
        RejectDuplicateProperties(json, "benchmark artifact");
        return JsonSerializer.Deserialize<AuthoredCorpusBenchmark.Report>(json, StrictJsonOptions())
            ?? throw new JsonException("Benchmark artifact is null.");
    }

    internal static HistoryRun Project(AuthoredCorpusBenchmark.Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ValidBreakdown is null
            || report.ValidBreakdown.FrontierIlDiffAttribution is null
            || report.InvalidBreakdown is null
            || report.Rows is null
            || report.Rows.Any(row => row is null))
        {
            throw new InvalidDataException("Benchmark artifact contains a null required object.");
        }
        if (!report.SourceRevisionMatchesHead)
            throw new InvalidDataException("Benchmark build revision did not match the checked-out HEAD.");
        if (report.SourceStateAtBuild != "clean")
        {
            throw new InvalidDataException(
                $"Benchmark source tree was '{report.SourceStateAtBuild}' when the harness was built.");
        }
        if (report.SourceDirty)
            throw new InvalidDataException("Benchmark source tree was dirty when the artifact was produced.");
        if (string.IsNullOrEmpty(report.Commit) || !FullCommitPattern().IsMatch(report.Commit))
            throw new InvalidDataException($"Benchmark commit '{report.Commit}' is not a full immutable commit ID.");
        if (!DateOnly.TryParseExact(
                report.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new InvalidDataException($"Benchmark date '{report.Date}' is not yyyy-MM-dd.");
        }
        if (!AuthoredCorpusExitContract.ReportInputsAreComplete(report))
        {
            throw new InvalidDataException("Benchmark artifact is incomplete and cannot become a history row.");
        }
        if (!AuthoredCorpusRatchet.IdentityIsWellFormed(report.PoolSha256)
            || !AuthoredCorpusRatchet.IdentityIsWellFormed(report.CorpusSha256)
            || report.PoolSha256 is null
            || report.CorpusSha256 is null)
        {
            throw new InvalidDataException("Benchmark artifact does not carry complete run identity digests.");
        }
        ParseRequiredEnum<AuthoredCorpusExitContract.QualityContract>(
            report.QualityContract,
            "qualityContract");

        RowCensus census = Census(report.Rows);
        VerifyProducerSummary(report, census);

        var row = new HistoryRun(
            report.Date,
            report.Commit,
            report.MatchedAssemblies,
            report.CorpusAssemblies,
            report.TargetsEvaluated,
            Math.Round(
                100.0 * (census.Correct + census.ValidDifferent) / report.TargetsEvaluated,
                1,
                MidpointRounding.ToEven),
            census.Correct,
            new HistoryRunValidDifferent(
                census.ValidDifferent,
                census.FrontierIlExact,
                census.FrontierIlDiff,
                census.Lowering,
                census.KnownTaste,
                census.FrontierIlNoVerdict,
                new HistoryRunFrontierIlDiffAttribution(
                    census.FrontierIlDiff,
                    census.FrontierProductBodyDefect,
                    census.FrontierHarnessShellReconstruction,
                    census.FrontierCompileBackFloor,
                    census.FrontierUnclassified)),
            census.Invalid,
            new HistoryRunInvalidBreakdown(
                census.InvalidProductBodyDefect,
                census.InvalidHarnessShellReconstruction,
                census.InvalidUnclassified),
            census.Unsupported,
            census.Drift,
            report.InputsComplete,
            SweepManifestSha256: null,
            report.MethodologyVersion,
            report.CorpusSha256,
            census.NotFull,
            census.UnknownOutcome,
            report.PoolSha256);

        VerifyMeasurement(row);
        return row;
    }

    sealed record RowCensus(
        int Correct,
        int PrinterExact,
        int PrinterDifferent,
        int PrinterNotRecorded,
        int Lowering,
        int KnownTaste,
        int FrontierIlExact,
        int FrontierIlDiff,
        int FrontierIlNoVerdict,
        int Invalid,
        int NotFull,
        int Drift,
        int Unsupported,
        int UnknownOutcome,
        int InvalidProductBodyDefect,
        int InvalidHarnessShellReconstruction,
        int InvalidUnclassified,
        int FrontierProductBodyDefect,
        int FrontierHarnessShellReconstruction,
        int FrontierCompileBackFloor,
        int FrontierUnclassified)
    {
        public int ValidDifferent
            => Lowering + KnownTaste + FrontierIlExact + FrontierIlDiff + FrontierIlNoVerdict;
    }

    static RowCensus Census(IReadOnlyList<AuthoredCorpusBenchmark.RowReport> rows)
    {
        int correct = 0, printerExact = 0, printerDifferent = 0, printerNotRecorded = 0;
        int lowering = 0, knownTaste = 0, frontierIlExact = 0, frontierIlDiff = 0;
        int frontierIlNoVerdict = 0, invalid = 0, notFull = 0, drift = 0, unsupported = 0, unknownOutcome = 0;
        int invalidProduct = 0, invalidHarness = 0, invalidUnclassified = 0;
        int frontierProduct = 0, frontierHarness = 0, frontierFloor = 0, frontierUnclassified = 0;

        foreach (AuthoredCorpusBenchmark.RowReport row in rows)
        {
            if (string.IsNullOrEmpty(row.Type)
                || string.IsNullOrEmpty(row.Method)
                || string.IsNullOrEmpty(row.Outcome)
                || string.IsNullOrEmpty(row.TasteBucket)
                || row.Reason is null)
            {
                throw new InvalidDataException("Benchmark artifact contains an incomplete per-row classification.");
            }

            ReturnToSenderSourceOutcome outcome =
                ParseRequiredEnum<ReturnToSenderSourceOutcome>(row.Outcome, "outcome");
            AuthoredCorpusBenchmark.TasteBucket taste =
                ParseRequiredEnum<AuthoredCorpusBenchmark.TasteBucket>(row.TasteBucket, "tasteBucket");
            FidelityCheck.CompileBackStatus? compileBackStatus =
                ParseOptionalEnum<FidelityCheck.CompileBackStatus>(
                    row.CompileBackStatus,
                    "compileBackStatus");
            ReturnToSenderInvalidKind? invalidKind =
                ParseOptionalEnum<ReturnToSenderInvalidKind>(row.InvalidKind, "invalidKind");
            PrinterExactOutcome printerOutcome =
                ParseRequiredEnum<PrinterExactOutcome>(row.PrinterExact, "printerExact");
            ReturnToSender.FaultIsolationKind? faultIsolation =
                ParseOptionalEnum<ReturnToSender.FaultIsolationKind>(
                    row.FaultIsolation,
                    "faultIsolation");
            ReturnToSender.FaultIsolationMethod? faultIsolationMethod =
                ParseOptionalEnum<ReturnToSender.FaultIsolationMethod>(
                    row.FaultIsolationMethod,
                    "faultIsolationMethod");
            ParseOptionalEnum<ReturnToSender.FaultIsolationKind>(
                row.SupersededFaultIsolation,
                "supersededFaultIsolation");
            ParseOptionalEnum<ReturnToSender.FaultIsolationMethod>(
                row.SupersededFaultIsolationMethod,
                "supersededFaultIsolationMethod");

            AuthoredCorpusBenchmark.TasteBucket expectedTaste =
                ExpectedTaste(row, outcome, compileBackStatus);
            if (taste != expectedTaste)
            {
                throw new InvalidDataException(
                    $"Benchmark row tasteBucket '{row.TasteBucket}' does not match "
                    + $"outcome/reason/compile status ('{expectedTaste}').");
            }

            ReturnToSenderInvalidKind? expectedInvalidKind =
                outcome == ReturnToSenderSourceOutcome.Invalid
                ? ReturnToSenderInvalidClassifier.ClassifyKind(faultIsolation, row.Detail)
                : null;
            if (invalidKind != expectedInvalidKind)
            {
                throw new InvalidDataException(
                    $"Benchmark row invalidKind '{row.InvalidKind}' does not match "
                    + $"outcome/fault-isolation/detail facts ('{expectedInvalidKind}').");
            }
            if (printerOutcome == PrinterExactOutcome.Exact
                && outcome != ReturnToSenderSourceOutcome.ValidMatch)
            {
                throw new InvalidDataException(
                    "Benchmark row reports Printer exact without being Correct.");
            }

            switch (taste)
            {
                case AuthoredCorpusBenchmark.TasteBucket.Correct:
                    correct++;
                    switch (printerOutcome)
                    {
                        case PrinterExactOutcome.Exact: printerExact++; break;
                        case PrinterExactOutcome.Different: printerDifferent++; break;
                        default: printerNotRecorded++; break;
                    }
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.Lowering:
                    lowering++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.KnownTaste:
                    knownTaste++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.FrontierIlExact:
                    frontierIlExact++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.FrontierIlDiff:
                    frontierIlDiff++;
                    if (row.UsedCompileBackFloor)
                    {
                        frontierFloor++;
                    }
                    else if (faultIsolationMethod != ReturnToSender.FaultIsolationMethod.FidelityControl)
                    {
                        frontierUnclassified++;
                    }
                    else if (faultIsolation == ReturnToSender.FaultIsolationKind.BodyDefect)
                    {
                        frontierProduct++;
                    }
                    else if (faultIsolation == ReturnToSender.FaultIsolationKind.ShellOrClosureDefect)
                    {
                        frontierHarness++;
                    }
                    else
                    {
                        frontierUnclassified++;
                    }
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.FrontierIlNoVerdict:
                    frontierIlNoVerdict++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.Invalid:
                    invalid++;
                    switch (expectedInvalidKind)
                    {
                        case ReturnToSenderInvalidKind.ProductBodyDefect: invalidProduct++; break;
                        case ReturnToSenderInvalidKind.HarnessShellReconstruction: invalidHarness++; break;
                        case ReturnToSenderInvalidKind.Unclassified: invalidUnclassified++; break;
                    }
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.NotFull:
                    notFull++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.Drift:
                    drift++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.Unsupported:
                    unsupported++;
                    break;
                case AuthoredCorpusBenchmark.TasteBucket.UnknownOutcome:
                    unknownOutcome++;
                    break;
            }
        }

        return new RowCensus(
            correct,
            printerExact,
            printerDifferent,
            printerNotRecorded,
            lowering,
            knownTaste,
            frontierIlExact,
            frontierIlDiff,
            frontierIlNoVerdict,
            invalid,
            notFull,
            drift,
            unsupported,
            unknownOutcome,
            invalidProduct,
            invalidHarness,
            invalidUnclassified,
            frontierProduct,
            frontierHarness,
            frontierFloor,
            frontierUnclassified);
    }

    static AuthoredCorpusBenchmark.TasteBucket ExpectedTaste(
        AuthoredCorpusBenchmark.RowReport row,
        ReturnToSenderSourceOutcome outcome,
        FidelityCheck.CompileBackStatus? compileBackStatus)
        => outcome switch
        {
            ReturnToSenderSourceOutcome.ValidMatch => AuthoredCorpusBenchmark.TasteBucket.Correct,
            ReturnToSenderSourceOutcome.Invalid => AuthoredCorpusBenchmark.TasteBucket.Invalid,
            ReturnToSenderSourceOutcome.SourceUnavailable
                => row.Reason.Contains("fidelity-unavailable", StringComparison.Ordinal)
                || row.Reason.Equals("NotFull", StringComparison.Ordinal)
                    ? AuthoredCorpusBenchmark.TasteBucket.NotFull
                    : AuthoredCorpusBenchmark.TasteBucket.Drift,
            ReturnToSenderSourceOutcome.UnsupportedTarget
                => AuthoredCorpusBenchmark.TasteBucket.Unsupported,
            ReturnToSenderSourceOutcome.ValidDifferent
                when row.Reason.Contains("compiler_lowering", StringComparison.Ordinal)
                => AuthoredCorpusBenchmark.TasteBucket.Lowering,
            ReturnToSenderSourceOutcome.ValidDifferent
                when row.Reason.Contains("known_taste", StringComparison.Ordinal)
                || row.Reason.Contains("known_compiler_option", StringComparison.Ordinal)
                => AuthoredCorpusBenchmark.TasteBucket.KnownTaste,
            ReturnToSenderSourceOutcome.ValidDifferent => compileBackStatus switch
            {
                FidelityCheck.CompileBackStatus.Exact
                    => AuthoredCorpusBenchmark.TasteBucket.FrontierIlExact,
                FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff
                    => AuthoredCorpusBenchmark.TasteBucket.FrontierIlDiff,
                _ => AuthoredCorpusBenchmark.TasteBucket.FrontierIlNoVerdict,
            },
            _ => AuthoredCorpusBenchmark.TasteBucket.UnknownOutcome,
        };

    internal static string ClassifyInvalidKind(AuthoredCorpusBenchmark.RowReport row)
        => ReturnToSenderInvalidClassifier.ClassifyKind(
            ParseOptionalEnum<ReturnToSender.FaultIsolationKind>(
                row.FaultIsolation,
                "faultIsolation"),
            row.Detail).ToString();

    static TEnum ParseRequiredEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
        => ParseOptionalEnum<TEnum>(value, field)
            ?? throw new InvalidDataException(
                $"Benchmark artifact has null enum-shaped field '{field}'.");

    static TEnum? ParseOptionalEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (value is null)
            return null;

        if (!Enum.TryParse(value, ignoreCase: false, out TEnum parsed)
            || !Enum.IsDefined(parsed)
            || !string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Benchmark artifact has unknown {field} value '{value}'.");
        }

        return parsed;
    }

    static void VerifyProducerSummary(AuthoredCorpusBenchmark.Report report, RowCensus census)
    {
        var valid = report.ValidBreakdown;
        var frontier = valid.FrontierIlDiffAttribution;
        var invalid = report.InvalidBreakdown;

        if (report.Correct != census.Correct
            || report.PrinterComparisonVersion
                != AuthoredSourceOracleManifest.PrinterComparisonVersion
            || report.PrinterExact != census.PrinterExact
            || report.PrinterDifferent != census.PrinterDifferent
            || report.PrinterNotRecorded != census.PrinterNotRecorded
            || report.ValidDifferent != census.ValidDifferent
            || valid.Total != census.ValidDifferent
            || valid.Lowering != census.Lowering
            || valid.KnownTaste != census.KnownTaste
            || valid.FrontierIlExact != census.FrontierIlExact
            || valid.FrontierIlDiff != census.FrontierIlDiff
            || valid.FrontierIlNoVerdict != census.FrontierIlNoVerdict
            || report.Invalid != census.Invalid
            || report.NotFull != census.NotFull
            || report.Drift != census.Drift
            || report.Unsupported != census.Unsupported
            || report.UnknownOutcome != census.UnknownOutcome
            || invalid.ProductBodyDefect != census.InvalidProductBodyDefect
            || invalid.HarnessShellReconstruction != census.InvalidHarnessShellReconstruction
            || invalid.Unclassified != census.InvalidUnclassified
            || frontier.Total != census.FrontierIlDiff
            || frontier.ProductBodyDefect != census.FrontierProductBodyDefect
            || frontier.HarnessShellReconstruction != census.FrontierHarnessShellReconstruction
            || frontier.CompileBackFloor != census.FrontierCompileBackFloor
            || frontier.Unclassified != census.FrontierUnclassified)
        {
            throw new InvalidDataException(
                "Benchmark summary does not match the artifact's per-row classifications.");
        }
    }

    internal static string SerializeCanonical(HistoryRun run)
        => JsonSerializer.Serialize(run, CanonicalJsonOptions()) + "\n";

    internal static IReadOnlyList<HistoryRun> ParseAndVerify(string jsonl, IRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (jsonl.Length == 0)
            throw new InvalidDataException("History store is empty.");
        if (!jsonl.EndsWith('\n'))
            throw new InvalidDataException("History store must end with exactly one LF newline.");
        if (jsonl.Contains('\r'))
            throw new InvalidDataException("History store must use LF line endings.");

        string[] lines = jsonl.Split('\n');
        var runs = new List<HistoryRun>(lines.Length - 1);
        for (int index = 0; index < lines.Length - 1; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException($"History line {index + 1} is blank.");
            RejectDuplicateProperties(line, $"history line {index + 1}");
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"History line {index + 1} is not one JSON object.");
            RequireProperties(
                document.RootElement,
                $"history line {index + 1}",
                "date",
                "commit",
                "poolMatched",
                "poolTotal",
                "evaluated",
                "validPct",
                "correct",
                "validDifferent",
                "invalid",
                "invalidBreakdown",
                "unsupported",
                "drift",
                "inputsComplete");
            AllowOnlyProperties(
                document.RootElement,
                $"history line {index + 1}",
                "date",
                "commit",
                "poolMatched",
                "poolTotal",
                "evaluated",
                "validPct",
                "correct",
                "validDifferent",
                "invalid",
                "invalidBreakdown",
                "unsupported",
                "drift",
                "inputsComplete",
                "sweepManifestSha256",
                "methodologyVersion",
                "corpusSha256",
                "notFull",
                "unknownOutcome",
                "poolSha256");
            JsonElement validDifferent = document.RootElement.GetProperty("validDifferent");
            if (validDifferent.ValueKind != JsonValueKind.Object)
                throw new JsonException($"History line {index + 1} validDifferent is not an object.");
            RequireProperties(
                validDifferent,
                $"history line {index + 1} validDifferent",
                "total",
                "frontierIlExact",
                "frontierIlDiff");
            AllowOnlyProperties(
                validDifferent,
                $"history line {index + 1} validDifferent",
                "total",
                "frontierIlExact",
                "frontierIlDiff",
                "lowering",
                "knownTaste",
                "frontierIlNoVerdict",
                "frontierIlDiffAttribution");

            if (validDifferent.TryGetProperty("frontierIlDiffAttribution", out JsonElement frontier)
                && frontier.ValueKind != JsonValueKind.Null)
            {
                if (frontier.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException(
                        $"History line {index + 1} frontierIlDiffAttribution is not an object.");
                }
                RequireProperties(
                    frontier,
                    $"history line {index + 1} frontierIlDiffAttribution",
                    "total",
                    "productBodyDefect",
                    "harnessShellReconstruction",
                    "compileBackFloor",
                    "unclassified");
                AllowOnlyProperties(
                    frontier,
                    $"history line {index + 1} frontierIlDiffAttribution",
                    "total",
                    "productBodyDefect",
                    "harnessShellReconstruction",
                    "compileBackFloor",
                    "unclassified");
            }

            JsonElement invalidBreakdown = document.RootElement.GetProperty("invalidBreakdown");
            if (invalidBreakdown.ValueKind != JsonValueKind.Null)
            {
                if (invalidBreakdown.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException(
                        $"History line {index + 1} invalidBreakdown is not an object.");
                }
                RequireProperties(
                    invalidBreakdown,
                    $"history line {index + 1} invalidBreakdown",
                    "productBodyDefect",
                    "harnessShellReconstruction",
                    "unclassified");
                AllowOnlyProperties(
                    invalidBreakdown,
                    $"history line {index + 1} invalidBreakdown",
                    "productBodyDefect",
                    "harnessShellReconstruction",
                    "unclassified");
            }

            var run = JsonSerializer.Deserialize<HistoryRun>(line, StrictJsonOptions())
                ?? throw new JsonException($"History line {index + 1} is null.");
            runs.Add(run);
        }

        VerifyRuns(runs, repository);
        return runs;
    }

    static void RequireProperties(JsonElement element, string source, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out _))
                throw new JsonException($"{source} is missing required property '{name}'.");
        }
    }

    static void AllowOnlyProperties(JsonElement element, string source, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new JsonException($"{source} has unknown property '{property.Name}'.");
        }
    }

    internal static void VerifyRuns(IReadOnlyList<HistoryRun> runs, IRepository repository)
    {
        if (runs.Count == 0)
            throw new InvalidDataException("History store has no rows.");

        for (int index = 0; index < runs.Count; index++)
            VerifyRun(runs[index], repository, allowGrandfather: index == 0);

        if (runs[0] is not
            {
                Date: "2026-07-20",
                Commit: null,
                MethodologyVersion: null,
                InvalidBreakdown: null,
            })
        {
            throw new InvalidDataException("Only the original 2026-07-20 row may omit commit and methodology provenance.");
        }

        if (runs.Skip(1).Any(run => run.Commit is null))
            throw new InvalidDataException("A non-grandfathered history row omits its commit.");
    }

    static void VerifyRun(HistoryRun run, IRepository repository, bool allowGrandfather)
    {
        if (!DateOnly.TryParseExact(
                run.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new InvalidDataException($"History date '{run.Date}' is not yyyy-MM-dd.");
        }

        if (allowGrandfather && run.Commit is null)
        {
            VerifyGrandfather(run);
            return;
        }
        if (run.Commit is null || !CommitPattern().IsMatch(run.Commit))
            throw new InvalidDataException($"History commit '{run.Commit}' is not a hexadecimal commit ID.");

        VerifyMeasurement(run);
        string commit = repository.ResolveCommit(run.Commit);
        if (!repository.IsOnMain(commit))
            throw new InvalidDataException($"Recorded commit '{run.Commit}' is not an ancestor of origin/main.");

        int methodology = repository.MethodologyAt(commit);
        if (methodology != run.Methodology)
        {
            throw new InvalidDataException(
                $"Recorded methodology {run.Methodology} was not produced by '{run.Commit}' "
                + $"(source implements {methodology}).");
        }
    }

    static void VerifyGrandfather(HistoryRun run)
    {
        var expected = new HistoryRun(
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

        if (run != expected)
            throw new InvalidDataException("The grandfathered 2026-07-20 row has changed.");
    }

    static void VerifyMeasurement(HistoryRun run)
    {
        if (AuthoredCorpusRatchet.RefuseMalformedIdentities([run]) is { } malformed)
            throw new InvalidDataException(malformed);
        if (AuthoredCorpusRatchet.RefuseUnknownMethodologies([run]) is { } unknown)
            throw new InvalidDataException(unknown);
        if (AuthoredCorpusRatchet.RefuseFrontierAttributionMethodologyMismatch([run]) is { } mismatch)
            throw new InvalidDataException(mismatch);
        if (!run.CountsAreNonNegative)
            throw new InvalidDataException($"{run.Date}: history counts must be non-negative.");
        if (run.PoolTotal <= 0 || run.PoolMatched != run.PoolTotal)
            throw new InvalidDataException($"{run.Date}: history row does not record a complete assembly pool.");
        if (!run.TopLevelIsComplete || run.TopLevelSum != run.Evaluated)
            throw new InvalidDataException($"{run.Date}: top-level history partition does not close.");
        if (run.ValidDifferent is not { IsComplete: true } validDifferent
            || validDifferent.SubBucketSum != validDifferent.Total)
        {
            throw new InvalidDataException($"{run.Date}: validDifferent history partition does not close.");
        }
        if (run.InvalidBreakdown is { } invalidBreakdown && invalidBreakdown.Sum != run.Invalid)
            throw new InvalidDataException($"{run.Date}: invalid history partition does not close.");
        if (run.Methodology >= 2 && run.InvalidBreakdown is null)
            throw new InvalidDataException($"{run.Date}: invalidBreakdown is required from methodology v2 onward.");
        if (!AuthoredCorpusRatchet.IsTrustworthy(run))
            throw new InvalidDataException($"{run.Date}: history row does not record a sound measurement.");

        double expectedValidPct = Math.Round(
            100.0 * (run.Correct + validDifferent.Total) / run.Evaluated,
            1,
            MidpointRounding.ToEven);
        if (run.ValidPct != expectedValidPct)
        {
            throw new InvalidDataException(
                $"{run.Date}: validPct {run.ValidPct} does not match the recorded partition ({expectedValidPct}).");
        }
    }

    static void RejectDuplicateProperties(string json, string source)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Visit(document.RootElement, source);

        static void Visit(JsonElement element, string source)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new JsonException($"{source} contains duplicate property '{property.Name}'.");
                    Visit(property.Value, source);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    Visit(item, source);
            }
        }
    }

    static JsonSerializerOptions StrictJsonOptions()
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    static JsonSerializerOptions CanonicalJsonOptions()
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    static string RunGit(string workingDirectory, params string[] arguments)
    {
        var result = RunGitResult(workingDirectory, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({result.ExitCode}): {result.Error.Trim()}");
        }

        return result.Output;
    }

    static int RunGitForExitCode(string workingDirectory, params string[] arguments)
        => RunGitResult(workingDirectory, arguments).ExitCode;

    static (int ExitCode, string Output, string Error) RunGitResult(
        string workingDirectory,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex FullCommitPattern();

    [GeneratedRegex("^[0-9a-f]{8,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex(
        @"^[ \t]*internal const int Version = ([0-9]+);[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MethodologyVersionPattern();

    [GeneratedRegex(
        @"^[ \t]*internal const int MethodologyVersion = ([0-9]+);[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyMethodologyVersionPattern();
}
