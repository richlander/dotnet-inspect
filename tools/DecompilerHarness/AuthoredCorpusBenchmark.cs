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

    public static int Run(
        IReadOnlyList<string> assemblies,
        string corpusPath,
        bool json,
        string? ratchetBaselinePath = null,
        string? poolManifestPath = null)
    {
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

        // Same rule: a pool manifest that was asked for but cannot be read is an error,
        // not a silently unidentified pool.
        string? poolManifestSha256 = null;
        if (poolManifestPath is not null)
        {
            if (!File.Exists(poolManifestPath))
            {
                Console.Error.WriteLine($"Pool manifest not found: {poolManifestPath}");
                return 1;
            }

            poolManifestSha256 = AuthoredCorpusRatchet.PoolManifestDigest(poolManifestPath);
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

        var results = new List<ReturnToSenderSourceProbeResult>();
        var matchedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assemblyPath in assemblies)
        {
            if (!File.Exists(assemblyPath))
                continue;

            string name = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath).Name;
            if (!byAssembly.TryGetValue(name, out var group) || !matchedGroups.Add(name))
                continue;

            var index = ReturnToSenderSourceIndex.FromMembers(group.Select(ToSourceMember));
            var targets = group.Select(ToTarget).ToArray();
            results.AddRange(ReturnToSenderSourceProbe.EvaluateWithIndex(assemblyPath, targets, index));
        }

        int unmatchedRows = byAssembly
            .Where(entry => !matchedGroups.Contains(entry.Key))
            .Sum(entry => entry.Value.Count);

        var inputs = new RunInputs(matchedGroups.Count, byAssembly.Count, unmatchedRows, malformedRows, poolManifestSha256);

        if (json)
            return WriteJson(results, records.Count, inputs, baselines);

        return WriteCard(results, records.Count, inputs, baselines);
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
        string? PoolManifestSha256);

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
            if (string.IsNullOrWhiteSpace(line))
                continue;
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
        IReadOnlyList<HistoryRun>? baselines)
    {
        var census = Census(results);
        int match = census.Correct;
        int different = census.ValidDifferent;
        int invalid = census.Invalid;
        int evaluated = census.Evaluated;
        int valid = match + different;
        var invalidBreakdown = InvalidBreakdown(results);

        Console.WriteLine($"AUTHORED-SOURCE CORPUS BENCHMARK");
        Console.WriteLine();
        Console.WriteLine($"  corpus rows        : {corpusRows}");
        Console.WriteLine($"  assemblies matched : {inputs.MatchedAssemblies} / {inputs.CorpusAssemblies}");
        if (inputs.UnmatchedRows > 0)
            Console.WriteLine($"  rows without asm   : {inputs.UnmatchedRows} (BLOCKER: no local assembly supplied)");
        if (inputs.MalformedRows > 0)
            Console.WriteLine($"  malformed rows     : {inputs.MalformedRows} (BLOCKER: corpus row dropped, denominator is short)");
        Console.WriteLine($"  targets evaluated  : {evaluated}");
        if (evaluated == 0)
            Console.WriteLine($"  (BLOCKER: no targets evaluated — nothing was checked)");

        Console.WriteLine();
        Console.WriteLine($"  Correct  (valid, matches authored)  : {match}");
        Console.WriteLine($"  Valid    (valid, differs)           : {different}");
        Console.WriteLine($"    lowering (inherent, unrecoverable): {census.Lowering}");
        Console.WriteLine($"    known taste (documented decision) : {census.KnownTaste}");
        Console.WriteLine($"    frontier, IL-exact (cosmetic)     : {census.FrontierIlExact}");
        Console.WriteLine($"    frontier, IL-diff (semantic)      : {census.FrontierIlDiff}");
        Console.WriteLine($"    UNMEASURED (oracle no verdict)    : {census.FrontierIlNoVerdict}");
        Console.WriteLine($"  Invalid  (does not round-trip)      : {invalid}");
        Console.WriteLine($"    product body defects              : {invalidBreakdown.ProductBodyDefect}");
        Console.WriteLine($"    harness shell reconstruction      : {invalidBreakdown.HarnessShellReconstruction}");
        Console.WriteLine($"    unclassified invalid              : {invalidBreakdown.Unclassified}");
        Console.WriteLine($"  Not-Full (uncheckable at Full)      : {census.NotFull}");
        Console.WriteLine($"  Drift    (corpus source unresolved) : {census.Drift}");
        Console.WriteLine($"  Unsupported (rts-target)            : {census.Unsupported}");
        Console.WriteLine($"  Unknown outcome (unclassified)      : {census.UnknownOutcome}");
        Console.WriteLine();
        if (valid + invalid > 0)
        {
            double correctPct = valid == 0 ? 0 : 100.0 * match / valid;
            double validPct = evaluated == 0 ? 0 : 100.0 * valid / evaluated;
            Console.WriteLine($"  Valid rate         : {valid}/{evaluated} ({validPct:F1}%)");
            Console.WriteLine($"  Correct rate       : {match}/{valid} of valid ({correctPct:F1}%)");
        }

        WriteReasonBuckets("Frontier, IL-diff (semantic) reasons", results, result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff);
        WriteReasonBuckets("Frontier, IL-exact (cosmetic) reasons", results, result => ClassifyTaste(result) == TasteBucket.FrontierIlExact);
        WriteReasonBuckets("Lowering (inherent) reasons", results, result => ClassifyTaste(result) == TasteBucket.Lowering);
        WriteReasonBuckets("Known-taste reasons", results, result => ClassifyTaste(result) == TasteBucket.KnownTaste);
        WriteReasonBuckets("Invalid reasons", results, result => ClassifyTaste(result) == TasteBucket.Invalid);
        WriteReasonBuckets("Not-Full reasons", results, result => ClassifyTaste(result) == TasteBucket.NotFull);
        WriteReasonBuckets("Drift reasons", results, result => ClassifyTaste(result) == TasteBucket.Drift);
        WriteReasonBuckets("Unsupported reasons", results, result => ClassifyTaste(result) == TasteBucket.Unsupported);

        // Both output modes share one exit contract and one partition check, so a
        // malformed run cannot pass in text mode and fail in --json mode.
        if (!census.PartitionClosed)
            Console.Error.WriteLine(census.PartitionFailureMessage);

        var ratchet = Ratchet(census, invalidBreakdown, inputs, baselines);
        if (ratchet is not null)
            AuthoredCorpusRatchet.Report(ratchet, Console.Out);

        return ExitCode(census, inputs, ratchet);
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

        // A run identifies its pool only when --ratchet-pool-manifest was supplied.
        // When it cannot, and the baseline row can, RunKey.IsComparableTo refuses the
        // comparison rather than assuming the inputs matched.
        var key = new AuthoredCorpusRatchet.RunKey(
            census.Evaluated,
            inputs.MatchedAssemblies,
            inputs.CorpusAssemblies,
            inputs.PoolManifestSha256);

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
    internal static int ExitCode(BucketCensus census, RunInputs inputs, AuthoredCorpusRatchet.Comparison? ratchet)
    {
        bool measurementIsSound = AuthoredCorpusExitContract.MeasurementIsSound(
            InputsComplete(census, inputs),
            census.PartitionClosed,
            census.Drift,
            census.Unsupported,
            census.UnknownOutcome);

        return AuthoredCorpusExitContract.ExitCode(measurementIsSound, census.Invalid, ratchet);
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
        Func<ReturnToSenderSourceProbeResult, bool> predicate)
    {
        var buckets = results
            .Where(predicate)
            .GroupBy(result => result.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (buckets.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"  {title}:");
        foreach (var bucket in buckets)
            Console.WriteLine($"    {bucket.Count(),5}  {bucket.Key}");
    }

    static int WriteJson(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        int corpusRows,
        RunInputs inputs,
        IReadOnlyList<HistoryRun>? baselines)
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

        var validBreakdown = new
        {
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
            targetsEvaluated = census.Evaluated,
            methodologyVersion = MethodologyVersion,
            inputsComplete,
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

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        // Same partition check and exit contract as the text-report path. The ratchet
        // verdict goes to stderr here so it stays visible without corrupting the JSON
        // document that callers redirect to a file; it is also in the payload above.
        if (!census.PartitionClosed)
            Console.Error.WriteLine(census.PartitionFailureMessage);

        if (ratchet is not null)
            AuthoredCorpusRatchet.Report(ratchet, Console.Error);

        return ExitCode(census, inputs, ratchet);
    }
}
