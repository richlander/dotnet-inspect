using System.Text.Json;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Offline benchmark run-mode over the vendored authored-source correspondence
/// corpus. Each corpus row carries a real method identity plus a checksum-verified
/// authored member body captured at harvest time. The benchmark rebuilds the RTS
/// <see cref="ReturnToSender.RequestedTarget"/> for every row and feeds the
/// snapshotted authored bodies into the existing source-correspondence oracle
/// (<see cref="ReturnToSenderSourceProbe"/>) via an in-memory
/// <see cref="ReturnToSenderSourceIndex"/>. The decompiler output is therefore
/// compared against the authored source in the same RTS shell the on-demand probe
/// uses, but with no network access: the corpus is the source of truth.
///
/// Because every row has a body by construction, <c>SourceUnavailable</c> is a
/// drift signal (corpus row whose identity no longer resolves against the pinned
/// assembly) rather than an expected outcome.
/// </summary>
static class AuthoredCorpusBenchmark
{
    /// <summary>
    /// Methodology version for how <c>invalidBreakdown.productBodyDefect</c> is
    /// computed. Defined by, and co-located with, the attribution rule it stamps
    /// (see <see cref="SpanAttribution.MethodologyVersion"/>); this alias keeps
    /// the serialization site readable.
    /// </summary>
    internal const int MethodologyVersion = SpanAttribution.MethodologyVersion;

    /// <param name="output">
    /// Where the report is written. Defaults to standard output; tests pass their own
    /// writer so that capturing a run does not mutate process-global console state,
    /// which raced other test classes under xunit's parallel runner.
    /// </param>
    public static int Run(
        IReadOnlyList<string> assemblies,
        string corpusPath,
        bool json,
        string? ratchetBaselinePath = null,
        bool integrityOnly = false,
        TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"Corpus file not found: {corpusPath}");
            return 1;
        }

        // A baseline that cannot be read is a hard error, never a skip. A typo'd path
        // that degraded into "nothing to compare" would be a permanently green gate —
        // the exact failure mode this ratchet exists to remove.
        IReadOnlyList<HistoryRun>? baselines = null;
        if (ratchetBaselinePath is not null)
        {
            if (!File.Exists(ratchetBaselinePath))
            {
                Console.Error.WriteLine($"Ratchet baseline not found: {ratchetBaselinePath}");
                return 1;
            }

            try
            {
                baselines = AuthoredCorpusHistoryCard.ParseHistory(File.ReadLines(ratchetBaselinePath));
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Ratchet baseline is not valid JSONL: {ratchetBaselinePath}: {ex.Message}");
                return 1;
            }
        }

        var records = ReadCorpus(corpusPath, out int malformedRows);
        if (records.Count == 0)
        {
            Console.Error.WriteLine($"Corpus is empty or unparseable: {corpusPath}");
            return 1;
        }

        var byAssembly = records
            .GroupBy(record => record.Assembly, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AuthoredSourceHarvest.CorpusRecord>)group.ToArray(), StringComparer.Ordinal);

        // Selection and identity come out of one call, so the pool this run identifies
        // is the pool it measures. They used to be computed separately, and a reviewer
        // showed the two could disagree: evaluation takes the first path per assembly
        // identity, so reordering two byte-distinct assemblies with the same identity
        // changed what was measured without changing the digest.
        var pool = AuthoredCorpusRatchet.SelectPool(
            assemblies,
            path => File.Exists(path)
                && AuthoredSourceHarvest.ReadAssemblyIdentity(path).Name is { } name
                && byAssembly.ContainsKey(name)
                    ? name
                    : null);

        if (pool.Assemblies.Count == 0)
        {
            Console.Error.WriteLine($"No supplied assembly matched the corpus, so the run measured nothing: {corpusPath}");
            return 1;
        }

        var results = new List<ReturnToSenderSourceProbeResult>();
        var matchedGroups = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pool.Assemblies.Count; i++)
        {
            string assemblyPath = pool.Assemblies[i];
            var group = byAssembly[pool.Identities[i]];
            matchedGroups.Add(pool.Identities[i]);

            var index = ReturnToSenderSourceIndex.FromMembers(group.Select(ToSourceMember));
            var targets = group.Select(ToTarget).ToArray();
            results.AddRange(ReturnToSenderSourceProbe.EvaluateWithIndex(assemblyPath, targets, index));
        }

        string? poolSha256 = pool.Sha256;

        int unmatchedRows = byAssembly
            .Where(entry => !matchedGroups.Contains(entry.Key))
            .Sum(entry => entry.Value.Count);

        // Always identified, baseline or not: the trend-store append procedure runs
        // *without* --ratchet-baseline, and it is that run's JSON the recorded row is
        // copied from. Withholding the digest here would make every appended row
        // unidentifiable, and so unusable as a future baseline.
        string corpusSha256 = AuthoredCorpusRatchet.CorpusDigest(corpusPath);

        var inputs = new RunInputs(
            matchedGroups.Count,
            byAssembly.Count,
            unmatchedRows,
            malformedRows,
            poolSha256,
            corpusSha256);

        if (json)
            return WriteJson(results, records.Count, inputs, baselines, integrityOnly, output);

        return WriteCard(results, records.Count, inputs, baselines, integrityOnly, output);
    }

    /// <summary>
    /// What the run was fed, as distinct from what it found. Every field here feeds
    /// measurement integrity or run identity, never the quality metrics.
    /// </summary>
    internal sealed record RunInputs(
        int MatchedAssemblies,
        int CorpusAssemblies,
        int UnmatchedRows,
        int MalformedRows,
        string? PoolSha256,
        string? CorpusSha256);

    static ReturnToSenderSourceMember ToSourceMember(AuthoredSourceHarvest.CorpusRecord record)
        => new(
            record.Type,
            record.Method,
            record.Overload,
            record.Signature ?? "",
            record.SourceUrl ?? "",
            record.AuthoredBody);

    static ReturnToSender.RequestedTarget ToTarget(AuthoredSourceHarvest.CorpusRecord record)
        => new(record.Type, record.Method, record.Overload, record.Signature);

    static List<AuthoredSourceHarvest.CorpusRecord> ReadCorpus(string corpusPath, out int malformedRows)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var records = new List<AuthoredSourceHarvest.CorpusRecord>();
        int malformed = 0;
        foreach (var line in File.ReadLines(corpusPath))
        {
            // Counted, not skipped. A reviewer replaced one corpus row with whitespace
            // and the run reported 99 rows, 0 malformed, inputsComplete true — the
            // denominator quietly shortened, which is the shape of defect this gate
            // exists to catch. Blank lines are not a legitimate part of a JSONL corpus,
            // and File.ReadLines does not manufacture one for the trailing newline.
            if (string.IsNullOrWhiteSpace(line))
            {
                malformed++;
                Console.Error.WriteLine("Skipping malformed corpus row: the line is empty, so a row was erased rather than absent.");
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<AuthoredSourceHarvest.CorpusRecord>(line, options) is { } record)
                    records.Add(record);
                else
                    malformed++;
            }
            catch (JsonException ex)
            {
                malformed++;
                Console.Error.WriteLine($"Skipping malformed corpus row: {ex.Message}");
            }
        }

        malformedRows = malformed;
        return records;
    }

    static int WriteCard(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        int corpusRows,
        RunInputs inputs,
        IReadOnlyList<HistoryRun>? baselines,
        bool integrityOnly,
        TextWriter output)
    {
        var census = Census(results);
        int match = census.Correct;
        int different = census.ValidDifferent;
        int invalid = census.Invalid;
        int evaluated = census.Evaluated;
        int valid = match + different;
        var invalidBreakdown = InvalidBreakdown(results);

        output.WriteLine($"AUTHORED-SOURCE CORPUS BENCHMARK");
        output.WriteLine();
        output.WriteLine($"  corpus rows        : {corpusRows}");
        output.WriteLine($"  assemblies matched : {inputs.MatchedAssemblies} / {inputs.CorpusAssemblies}");
        if (inputs.UnmatchedRows > 0)
            output.WriteLine($"  rows without asm   : {inputs.UnmatchedRows} (BLOCKER: no local assembly supplied)");
        if (inputs.MalformedRows > 0)
            output.WriteLine($"  malformed rows     : {inputs.MalformedRows} (BLOCKER: corpus row dropped, denominator is short)");
        output.WriteLine($"  targets evaluated  : {evaluated}");
        if (evaluated == 0)
            output.WriteLine($"  (BLOCKER: no targets evaluated — nothing was checked)");

        output.WriteLine();
        output.WriteLine($"  Correct  (valid, matches authored)  : {match}");
        output.WriteLine($"  Valid    (valid, differs)           : {different}");
        output.WriteLine($"    lowering (inherent, unrecoverable): {census.Lowering}");
        output.WriteLine($"    known taste (documented decision) : {census.KnownTaste}");
        output.WriteLine($"    frontier, IL-exact (cosmetic)     : {census.FrontierIlExact}");
        output.WriteLine($"    frontier, IL-diff (semantic)      : {census.FrontierIlDiff}");
        output.WriteLine($"    UNMEASURED (oracle no verdict)    : {census.FrontierIlNoVerdict}");
        output.WriteLine($"  Invalid  (does not round-trip)      : {invalid}");
        output.WriteLine($"    product body defects              : {invalidBreakdown.ProductBodyDefect}");
        output.WriteLine($"    harness shell reconstruction      : {invalidBreakdown.HarnessShellReconstruction}");
        output.WriteLine($"    unclassified invalid              : {invalidBreakdown.Unclassified}");
        output.WriteLine($"  Not-Full (uncheckable at Full)      : {census.NotFull}");
        output.WriteLine($"  Drift    (corpus source unresolved) : {census.Drift}");
        output.WriteLine($"  Unsupported (rts-target)            : {census.Unsupported}");
        output.WriteLine($"  Unknown outcome (unclassified)      : {census.UnknownOutcome}");
        output.WriteLine();
        if (valid + invalid > 0)
        {
            double correctPct = valid == 0 ? 0 : 100.0 * match / valid;
            double validPct = evaluated == 0 ? 0 : 100.0 * valid / evaluated;
            output.WriteLine($"  Valid rate         : {valid}/{evaluated} ({validPct:F1}%)");
            output.WriteLine($"  Correct rate       : {match}/{valid} of valid ({correctPct:F1}%)");
        }

        WriteReasonBuckets("Frontier, IL-diff (semantic) reasons", results, result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff, output);
        WriteReasonBuckets("Frontier, IL-exact (cosmetic) reasons", results, result => ClassifyTaste(result) == TasteBucket.FrontierIlExact, output);
        WriteReasonBuckets("Lowering (inherent) reasons", results, result => ClassifyTaste(result) == TasteBucket.Lowering, output);
        WriteReasonBuckets("Known-taste reasons", results, result => ClassifyTaste(result) == TasteBucket.KnownTaste, output);
        WriteReasonBuckets("Invalid reasons", results, result => ClassifyTaste(result) == TasteBucket.Invalid, output);
        WriteReasonBuckets("Not-Full reasons", results, result => ClassifyTaste(result) == TasteBucket.NotFull, output);
        WriteReasonBuckets("Drift reasons", results, result => ClassifyTaste(result) == TasteBucket.Drift, output);
        WriteReasonBuckets("Unsupported reasons", results, result => ClassifyTaste(result) == TasteBucket.Unsupported, output);

        // Both output modes share one exit contract and one partition check, so a
        // malformed run cannot pass in text mode and fail in --json mode.
        if (!census.PartitionClosed)
            Console.Error.WriteLine(census.PartitionFailureMessage);

        var ratchet = Ratchet(census, invalidBreakdown, inputs, baselines);
        if (ratchet is not null)
            AuthoredCorpusRatchet.Report(ratchet, Console.Out);

        var contract = AuthoredCorpusExitContract.ContractFor(integrityOnly, ratchet);
        ReportContract(contract, census.Invalid, Console.Out);

        return ExitCode(census, inputs, ratchet, contract);
    }

    /// <summary>
    /// One pass over the probe results, counting every bucket exactly once. Both the
    /// text-report and <c>--json</c> paths render from this same census so the two
    /// modes cannot disagree about what a run contained, and both apply the same
    /// partition check and exit contract.
    /// </summary>
    internal sealed record BucketCensus(
        int Evaluated,
        int Correct,
        int ValidDifferent,
        int Lowering,
        int KnownTaste,
        int FrontierIlExact,
        int FrontierIlDiff,
        int FrontierIlNoVerdict,
        int Invalid,
        int NotFull,
        int Drift,
        int Unsupported,
        int UnknownOutcome)
    {
        public int ValidDifferentSum
            => Lowering + KnownTaste + FrontierIlExact + FrontierIlDiff + FrontierIlNoVerdict;

        public int TopLevelSum
            => Correct + ValidDifferent + Invalid + NotFull + Drift + Unsupported + UnknownOutcome;

        /// <summary>
        /// Both partitions close exactly. A shortfall means a row was counted in a
        /// bucket the schema cannot represent, which is how measurement silently
        /// turns into arithmetic.
        /// </summary>
        public bool PartitionClosed => ValidDifferentSum == ValidDifferent && TopLevelSum == Evaluated;

        public string PartitionFailureMessage
            => $"BLOCKER: emitted buckets do not partition the run — validDifferent {ValidDifferentSum} vs {ValidDifferent}, top-level {TopLevelSum} vs {Evaluated}.";
    }

    static BucketCensus Census(IReadOnlyList<ReturnToSenderSourceProbeResult> results)
    {
        int correct = 0, lowering = 0, knownTaste = 0, frontierIlExact = 0, frontierIlDiff = 0;
        int frontierIlNoVerdict = 0, invalid = 0, notFull = 0, drift = 0, unsupported = 0, unknownOutcome = 0;

        foreach (var result in results)
        {
            switch (ClassifyTaste(result))
            {
                case TasteBucket.Correct: correct++; break;
                case TasteBucket.Lowering: lowering++; break;
                case TasteBucket.KnownTaste: knownTaste++; break;
                case TasteBucket.FrontierIlExact: frontierIlExact++; break;
                case TasteBucket.FrontierIlDiff: frontierIlDiff++; break;
                case TasteBucket.FrontierIlNoVerdict: frontierIlNoVerdict++; break;
                case TasteBucket.Invalid: invalid++; break;
                case TasteBucket.NotFull: notFull++; break;
                case TasteBucket.Drift: drift++; break;
                case TasteBucket.Unsupported: unsupported++; break;
                default: unknownOutcome++; break;
            }
        }

        // ValidDifferent is counted from the outcome, not from the sub-buckets, so
        // that PartitionClosed compares two independently derived numbers rather
        // than a sum against itself.
        return new BucketCensus(
            Evaluated: results.Count,
            Correct: correct,
            ValidDifferent: results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent),
            Lowering: lowering,
            KnownTaste: knownTaste,
            FrontierIlExact: frontierIlExact,
            FrontierIlDiff: frontierIlDiff,
            FrontierIlNoVerdict: frontierIlNoVerdict,
            Invalid: invalid,
            NotFull: notFull,
            Drift: drift,
            Unsupported: unsupported,
            UnknownOutcome: unknownOutcome);
    }

    /// <summary>
    /// Builds the ratchet comparison for a completed run, or null when no baseline was
    /// supplied. Shared by both output modes so text and <c>--json</c> cannot disagree
    /// about whether quality regressed.
    /// </summary>
    static AuthoredCorpusRatchet.Comparison? Ratchet(
        BucketCensus census,
        InvalidBreakdownCounts invalidBreakdown,
        RunInputs inputs,
        IReadOnlyList<HistoryRun>? baselines)
    {
        if (baselines is null)
            return null;

        // A live run always identifies its pool, because the identity is taken from the
        // assemblies it measured. A recorded row that predates that scheme carries no
        // pool identity, and RunKey.IsComparableTo refuses the comparison rather than
        // assuming the inputs matched.
        var key = new AuthoredCorpusRatchet.RunKey(
            census.Evaluated,
            inputs.MatchedAssemblies,
            inputs.CorpusAssemblies,
            inputs.PoolSha256,
            inputs.CorpusSha256);

        var metrics = new AuthoredCorpusRatchet.RunMetrics(
            Valid: census.Correct + census.ValidDifferent,
            census.Correct,
            census.Invalid,
            invalidBreakdown.ProductBodyDefect,
            MethodologyVersion);

        return AuthoredCorpusRatchet.Compare(key, metrics, baselines);
    }

    static bool InputsComplete(BucketCensus census, RunInputs inputs)
        => AuthoredCorpusExitContract.InputsComplete(inputs.UnmatchedRows, inputs.MalformedRows, census.Evaluated);

    /// <summary>
    /// The single exit contract for both output modes, delegating to
    /// <see cref="AuthoredCorpusExitContract"/> so the decision is unit-testable
    /// without running the corpus. See that type for the integrity/quality split.
    /// Not-Full is a surfaced decompiler limitation, not a corpus problem, so it does
    /// not fail the run on its own.
    /// </summary>
    internal static int ExitCode(
        BucketCensus census,
        RunInputs inputs,
        AuthoredCorpusRatchet.Comparison? ratchet,
        AuthoredCorpusExitContract.QualityContract contract)
    {
        bool measurementIsSound = AuthoredCorpusExitContract.MeasurementIsSound(
            InputsComplete(census, inputs),
            census.PartitionClosed,
            census.Drift,
            census.Unsupported,
            census.UnknownOutcome);

        return AuthoredCorpusExitContract.ExitCode(measurementIsSound, census.Invalid, ratchet, contract);
    }

    /// <summary>
    /// Says out loud which quality claim the exit code is about to make, so that a
    /// green integrity-only run cannot be read as a quality pass by anyone looking at
    /// the log or the exit code alone.
    /// </summary>
    static void ReportContract(AuthoredCorpusExitContract.QualityContract contract, int invalid, TextWriter writer)
    {
        if (contract is AuthoredCorpusExitContract.QualityContract.NotJudged)
            writer.WriteLine($"[integrity-only] Quality was not judged: {invalid} invalid rows stand unreviewed. This exit code reports measurement integrity only.");
    }

    enum TasteBucket
    {
        Correct,
        Lowering,
        KnownTaste,
        FrontierIlExact,
        FrontierIlDiff,

        /// <summary>
        /// A valid-different row the compile-back oracle returned no verdict for.
        /// This is instrument failure, not a classification: the row's IL
        /// correspondence is <em>unmeasured</em>, not "neither exact nor diff".
        /// It is reported and serialized unconditionally so a shortfall can never
        /// be mistaken for data.
        /// </summary>
        FrontierIlNoVerdict,
        Invalid,
        NotFull,
        Drift,
        Unsupported,

        /// <summary>
        /// A probe outcome this classifier does not recognize. Unreachable while
        /// every <see cref="ReturnToSenderSourceOutcome"/> member is handled above;
        /// it exists so that adding an outcome without classifying it surfaces as
        /// its own failure rather than silently inflating a real bucket.
        /// </summary>
        UnknownOutcome,
    }

    /// <summary>
    /// Buckets a probe result along two taste axes. Family comes first: authored
    /// sugar the compiler erases (<c>compiler_lowering</c>) is an inherent,
    /// unrecoverable limit, and a documented product decision (<c>known_taste</c>
    /// or <c>known_compiler_option</c>) is already accounted for. Everything else
    /// is the raise frontier, split by whether the shape difference is free at the
    /// IL level (Exact) or carries an opcode/operand diff (semantic).
    ///
    /// The probe collapses several statuses into <c>SourceUnavailable</c>: a
    /// decompiler body that could not be graded at Full fidelity
    /// (<c>fidelity-unavailable</c>/<c>NotFull</c>) is a decompiler limitation, not
    /// corpus drift, so it gets its own <c>NotFull</c> bucket; only a genuinely
    /// unresolved corpus identity counts as <c>Drift</c>.
    /// </summary>
    static TasteBucket ClassifyTaste(ReturnToSenderSourceProbeResult result)
        => result.Outcome switch
        {
            ReturnToSenderSourceOutcome.ValidMatch => TasteBucket.Correct,
            ReturnToSenderSourceOutcome.Invalid => TasteBucket.Invalid,
            ReturnToSenderSourceOutcome.SourceUnavailable => IsNotFullReason(result.Reason) ? TasteBucket.NotFull : TasteBucket.Drift,
            ReturnToSenderSourceOutcome.UnsupportedTarget => TasteBucket.Unsupported,
            ReturnToSenderSourceOutcome.ValidDifferent when result.Reason.Contains("compiler_lowering", StringComparison.Ordinal) => TasteBucket.Lowering,
            ReturnToSenderSourceOutcome.ValidDifferent when result.Reason.Contains("known_taste", StringComparison.Ordinal) || result.Reason.Contains("known_compiler_option", StringComparison.Ordinal) => TasteBucket.KnownTaste,
            ReturnToSenderSourceOutcome.ValidDifferent => result.CompileBackStatus switch
            {
                FidelityCheck.CompileBackStatus.Exact => TasteBucket.FrontierIlExact,
                FidelityCheck.CompileBackStatus.OpcodeDiff or FidelityCheck.CompileBackStatus.OperandDiff => TasteBucket.FrontierIlDiff,
                _ => TasteBucket.FrontierIlNoVerdict,
            },
            _ => TasteBucket.UnknownOutcome,
        };

    // A SourceUnavailable row whose reason names a decompiler fidelity drop
    // (the body could not be graded at Full) rather than a missing corpus source.
    static bool IsNotFullReason(string reason)
        => reason.Contains("fidelity-unavailable", StringComparison.Ordinal)
            || reason.Equals("NotFull", StringComparison.Ordinal);

    internal sealed record InvalidBreakdownCounts(        int ProductBodyDefect,
        int HarnessShellReconstruction,
        int Unclassified)
    {
        public int Total => ProductBodyDefect + HarnessShellReconstruction + Unclassified;
    }

    internal static InvalidBreakdownCounts InvalidBreakdown(IReadOnlyList<ReturnToSenderSourceProbeResult> results)
    {
        int productBodyDefect = 0;
        int harnessShellReconstruction = 0;
        int unclassified = 0;

        foreach (var result in results.Where(result => ClassifyTaste(result) == TasteBucket.Invalid))
        {
            switch (ReturnToSenderInvalidClassifier.Classify(result))
            {
                case ReturnToSenderInvalidKind.ProductBodyDefect:
                    productBodyDefect++;
                    break;
                case ReturnToSenderInvalidKind.HarnessShellReconstruction:
                    harnessShellReconstruction++;
                    break;
                case ReturnToSenderInvalidKind.Unclassified:
                case null:
                    unclassified++;
                    break;
            }
        }

        return new InvalidBreakdownCounts(productBodyDefect, harnessShellReconstruction, unclassified);
    }

    static void WriteReasonBuckets(
        string title,
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        Func<ReturnToSenderSourceProbeResult, bool> predicate,
        TextWriter output)
    {
        var buckets = results
            .Where(predicate)
            .GroupBy(result => result.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (buckets.Length == 0)
            return;

        output.WriteLine();
        output.WriteLine($"  {title}:");
        foreach (var bucket in buckets)
            output.WriteLine($"    {bucket.Count(),5}  {bucket.Key}");
    }

    static int WriteJson(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        int corpusRows,
        RunInputs inputs,
        IReadOnlyList<HistoryRun>? baselines,
        bool integrityOnly,
        TextWriter output)
    {
        var census = Census(results);
        var invalidBreakdown = InvalidBreakdown(results);
        // Inputs-complete contract: an empty run, a row whose assembly was not
        // supplied, or a corpus row that failed to parse all mean the denominator is
        // not the one the corpus describes. This flag reports only that the inputs were
        // all present; it makes no claim that every evaluated row's product status was
        // measured (see frontierIlNoVerdict and invalidBreakdown.harnessShellReconstruction,
        // both of which are unmeasured rather than clean).
        bool inputsComplete = InputsComplete(census, inputs);
        var ratchet = Ratchet(census, invalidBreakdown, inputs, baselines);
        var contract = AuthoredCorpusExitContract.ContractFor(integrityOnly, ratchet);

        // `total` leads, and is emitted here rather than only as the sibling
        // `validDifferent` field, because this object is what an author copies into the
        // trend store's `validDifferent` row member — where the total is the number the
        // ratchet's `valid` metric is built from. Emitting the parts without their sum
        // invited a row that recorded 0, and a row whose partition does not close is
        // rejected as unsound: a loud skip, but one caused by the shape of this output.
        var validBreakdown = new
        {
            total = census.ValidDifferent,
            lowering = census.Lowering,
            knownTaste = census.KnownTaste,
            frontierIlExact = census.FrontierIlExact,
            frontierIlDiff = census.FrontierIlDiff,
            frontierIlNoVerdict = census.FrontierIlNoVerdict,
        };

        var payload = new
        {
            corpusRows,
            matchedAssemblies = inputs.MatchedAssemblies,
            corpusAssemblies = inputs.CorpusAssemblies,
            unmatchedRows = inputs.UnmatchedRows,
            malformedRows = inputs.MalformedRows,
            poolSha256 = inputs.PoolSha256,
            corpusSha256 = inputs.CorpusSha256,
            targetsEvaluated = census.Evaluated,
            methodologyVersion = MethodologyVersion,
            inputsComplete,
            // Which quality claim this run's exit code makes. A consumer that sees
            // exit 0 must read this before treating the run as a quality pass:
            // "NotJudged" means no quality claim was made at all.
            qualityContract = contract.ToString(),
            correct = census.Correct,
            validDifferent = census.ValidDifferent,
            validBreakdown,
            invalid = census.Invalid,
            invalidBreakdown = new
            {
                productBodyDefect = invalidBreakdown.ProductBodyDefect,
                harnessShellReconstruction = invalidBreakdown.HarnessShellReconstruction,
                unclassified = invalidBreakdown.Unclassified,
            },
            notFull = census.NotFull,
            drift = census.Drift,
            unsupported = census.Unsupported,
            unknownOutcome = census.UnknownOutcome,
            ratchet = ratchet is null ? null : new
            {
                skipped = ratchet.Skipped,
                skipReason = ratchet.SkipReason,
                baselineDate = ratchet.Baseline?.Date,
                baselineCommit = ratchet.Baseline?.Commit,
                regressed = ratchet.Regressions.Count > 0,
                metrics = ratchet.Metrics.Select(metric => new
                {
                    name = metric.Name,
                    baseline = metric.Baseline,
                    current = metric.Current,
                    higherIsBetter = metric.HigherIsBetter,
                    regressed = metric.Regressed,
                }),
            },
            rows = results.Select(result => new
            {
                type = result.Target.Type,
                method = result.Target.Method,
                overload = result.Target.Overload,
                outcome = result.Outcome.ToString(),
                tasteBucket = ClassifyTaste(result).ToString(),
                compileBackStatus = result.CompileBackStatus?.ToString(),
                invalidKind = ReturnToSenderInvalidClassifier.Classify(result)?.ToString(),
                faultIsolation = result.FaultIsolationKind?.ToString(),
                faultIsolationMethod = result.FaultIsolationMethod?.ToString(),
                reason = result.Reason,
                detail = result.Detail,
                sourceFile = result.SourcePath,
            }),
        };

        output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        // Same partition check and exit contract as the text-report path. The ratchet
        // verdict goes to stderr here so it stays visible without corrupting the JSON
        // document that callers redirect to a file; it is also in the payload above.
        if (!census.PartitionClosed)
            Console.Error.WriteLine(census.PartitionFailureMessage);

        if (ratchet is not null)
            AuthoredCorpusRatchet.Report(ratchet, Console.Error);

        ReportContract(contract, census.Invalid, Console.Error);

        return ExitCode(census, inputs, ratchet, contract);
    }
}
