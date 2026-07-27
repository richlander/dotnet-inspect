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

    public static int Run(IReadOnlyList<string> assemblies, string corpusPath, bool json)
    {
        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"Corpus file not found: {corpusPath}");
            return 1;
        }

        var records = ReadCorpus(corpusPath);
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

        if (json)
            return WriteJson(results, records.Count, matchedGroups.Count, byAssembly.Count, unmatchedRows);

        return WriteCard(results, records.Count, matchedGroups.Count, byAssembly.Count, unmatchedRows);
    }

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

    static List<AuthoredSourceHarvest.CorpusRecord> ReadCorpus(string corpusPath)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var records = new List<AuthoredSourceHarvest.CorpusRecord>();
        foreach (var line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonSerializer.Deserialize<AuthoredSourceHarvest.CorpusRecord>(line, options) is { } record)
                    records.Add(record);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Skipping malformed corpus row: {ex.Message}");
            }
        }

        return records;
    }

    static int WriteCard(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        int corpusRows,
        int matchedAssemblies,
        int corpusAssemblies,
        int unmatchedRows)
    {
        int match = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch);
        int different = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent);
        int invalid = results.Count(result => ClassifyTaste(result) == TasteBucket.Invalid);
        int notFull = results.Count(result => ClassifyTaste(result) == TasteBucket.NotFull);
        int drift = results.Count(result => ClassifyTaste(result) == TasteBucket.Drift);
        int unsupported = results.Count(result => ClassifyTaste(result) == TasteBucket.Unsupported);
        int evaluated = results.Count;
        int valid = match + different;
        var invalidBreakdown = InvalidBreakdown(results);

        Console.WriteLine($"AUTHORED-SOURCE CORPUS BENCHMARK");
        Console.WriteLine();
        Console.WriteLine($"  corpus rows        : {corpusRows}");
        Console.WriteLine($"  assemblies matched : {matchedAssemblies} / {corpusAssemblies}");
        if (unmatchedRows > 0)
            Console.WriteLine($"  rows without asm   : {unmatchedRows} (BLOCKER: no local assembly supplied)");
        Console.WriteLine($"  targets evaluated  : {evaluated}");
        if (evaluated == 0)
            Console.WriteLine($"  (BLOCKER: no targets evaluated — nothing was checked)");
        int lowering = results.Count(result => ClassifyTaste(result) == TasteBucket.Lowering);
        int knownTaste = results.Count(result => ClassifyTaste(result) == TasteBucket.KnownTaste);
        int frontierExact = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlExact);
        int frontierDiff = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff);
        int frontierNoVerdict = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlNoVerdict);
        int unknownOutcome = results.Count(result => ClassifyTaste(result) == TasteBucket.UnknownOutcome);

        Console.WriteLine();
        Console.WriteLine($"  Correct  (valid, matches authored)  : {match}");
        Console.WriteLine($"  Valid    (valid, differs)           : {different}");
        Console.WriteLine($"    lowering (inherent, unrecoverable): {lowering}");
        Console.WriteLine($"    known taste (documented decision) : {knownTaste}");
        Console.WriteLine($"    frontier, IL-exact (cosmetic)     : {frontierExact}");
        Console.WriteLine($"    frontier, IL-diff (semantic)      : {frontierDiff}");
        Console.WriteLine($"    UNMEASURED (oracle no verdict)    : {frontierNoVerdict}");
        Console.WriteLine($"  Invalid  (does not round-trip)      : {invalid}");
        Console.WriteLine($"    product body defects              : {invalidBreakdown.ProductBodyDefect}");
        Console.WriteLine($"    harness shell reconstruction      : {invalidBreakdown.HarnessShellReconstruction}");
        Console.WriteLine($"    unclassified invalid              : {invalidBreakdown.Unclassified}");
        Console.WriteLine($"  Not-Full (uncheckable at Full)      : {notFull}");
        Console.WriteLine($"  Drift    (corpus source unresolved) : {drift}");
        Console.WriteLine($"  Unsupported (rts-target)            : {unsupported}");
        Console.WriteLine($"  Unknown outcome (unclassified)      : {unknownOutcome}");
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

        // Nonzero exit if any target failed to round-trip (Invalid) or the corpus
        // no longer corresponds to the pinned assembly (Drift/Unsupported). Not-Full
        // is a surfaced decompiler limitation, not a corpus problem, so it does not
        // fail the run on its own. Inputs-complete contract: an empty or partially
        // unmatched run is never a success — every corpus row must be checked, so
        // unmatched rows (assembly not supplied) and a zero-target run also fail.
        // An unrecognized outcome means the classifier no longer covers the probe,
        // which is a measurement failure rather than a result.
        bool inputsComplete = unmatchedRows == 0 && evaluated > 0;
        return inputsComplete && invalid == 0 && drift == 0 && unsupported == 0 && unknownOutcome == 0 ? 0 : 1;
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
        int matchedAssemblies,
        int corpusAssemblies,
        int unmatchedRows)
    {
        int match = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch);
        int different = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent);
        int invalid = results.Count(result => ClassifyTaste(result) == TasteBucket.Invalid);
        int notFull = results.Count(result => ClassifyTaste(result) == TasteBucket.NotFull);
        int drift = results.Count(result => ClassifyTaste(result) == TasteBucket.Drift);
        int unsupported = results.Count(result => ClassifyTaste(result) == TasteBucket.Unsupported);
        int evaluated = results.Count;
        int unknownOutcome = results.Count(result => ClassifyTaste(result) == TasteBucket.UnknownOutcome);
        var invalidBreakdown = InvalidBreakdown(results);
        // Inputs-complete contract: an empty or partially unmatched run is never a
        // success — unmatched rows (assembly not supplied) and a zero-target run fail.
        // This flag reports only that the inputs were all present; it makes no claim
        // that every evaluated row's product status was measured (see
        // frontierIlNoVerdict and invalidBreakdown.harnessShellReconstruction, both
        // of which are unmeasured rather than clean).
        bool inputsComplete = unmatchedRows == 0 && evaluated > 0;

        var validBreakdown = new
        {
            lowering = results.Count(result => ClassifyTaste(result) == TasteBucket.Lowering),
            knownTaste = results.Count(result => ClassifyTaste(result) == TasteBucket.KnownTaste),
            frontierIlExact = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlExact),
            frontierIlDiff = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff),
            frontierIlNoVerdict = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlNoVerdict),
        };

        var payload = new
        {
            corpusRows,
            matchedAssemblies,
            corpusAssemblies,
            unmatchedRows,
            targetsEvaluated = evaluated,
            methodologyVersion = MethodologyVersion,
            inputsComplete,
            correct = match,
            validDifferent = different,
            validBreakdown,
            invalid,
            invalidBreakdown = new
            {
                productBodyDefect = invalidBreakdown.ProductBodyDefect,
                harnessShellReconstruction = invalidBreakdown.HarnessShellReconstruction,
                unclassified = invalidBreakdown.Unclassified,
            },
            notFull,
            drift,
            unsupported,
            unknownOutcome,
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

        // Schema assertion: both partitions must close exactly. A shortfall means a
        // row was counted in a bucket the schema cannot represent, which is how
        // measurement silently turns into arithmetic. Report it and fail rather than
        // emitting a payload that looks complete.
        int validDifferentSum = validBreakdown.lowering + validBreakdown.knownTaste
            + validBreakdown.frontierIlExact + validBreakdown.frontierIlDiff + validBreakdown.frontierIlNoVerdict;
        int topLevelSum = match + different + invalid + notFull + drift + unsupported + unknownOutcome;
        bool partitionClosed = validDifferentSum == different && topLevelSum == evaluated;
        if (!partitionClosed)
        {
            Console.Error.WriteLine(
                $"BLOCKER: emitted buckets do not partition the run — validDifferent {validDifferentSum} vs {different}, top-level {topLevelSum} vs {evaluated}.");
        }

        return inputsComplete && partitionClosed && invalid == 0 && drift == 0 && unsupported == 0 && unknownOutcome == 0 ? 0 : 1;
    }
}
