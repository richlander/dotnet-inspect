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
        int invalid = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid);
        int unavailable = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable);
        int unsupported = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget);
        int evaluated = results.Count;
        int valid = match + different;

        Console.WriteLine($"AUTHORED-SOURCE CORPUS BENCHMARK");
        Console.WriteLine();
        Console.WriteLine($"  corpus rows        : {corpusRows}");
        Console.WriteLine($"  assemblies matched : {matchedAssemblies} / {corpusAssemblies}");
        if (unmatchedRows > 0)
            Console.WriteLine($"  rows without asm   : {unmatchedRows} (no local assembly supplied)");
        Console.WriteLine($"  targets evaluated  : {evaluated}");
        int lowering = results.Count(result => ClassifyTaste(result) == TasteBucket.Lowering);
        int knownTaste = results.Count(result => ClassifyTaste(result) == TasteBucket.KnownTaste);
        int frontierExact = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlExact);
        int frontierDiff = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff);
        int frontierUnknown = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlUnknown);

        Console.WriteLine();
        Console.WriteLine($"  Correct  (valid, matches authored)  : {match}");
        Console.WriteLine($"  Valid    (valid, differs)           : {different}");
        Console.WriteLine($"    lowering (inherent, unrecoverable): {lowering}");
        Console.WriteLine($"    known taste (documented decision) : {knownTaste}");
        Console.WriteLine($"    frontier, IL-exact (cosmetic)     : {frontierExact}");
        Console.WriteLine($"    frontier, IL-diff (semantic)      : {frontierDiff}");
        if (frontierUnknown > 0)
            Console.WriteLine($"    frontier, IL-unknown              : {frontierUnknown}");
        Console.WriteLine($"  Invalid  (does not round-trip)      : {invalid}");
        if (unavailable > 0)
            Console.WriteLine($"  Drift    (source-unavailable)       : {unavailable}");
        if (unsupported > 0)
            Console.WriteLine($"  Unsupported (rts-target)            : {unsupported}");
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
        WriteReasonBuckets("Invalid reasons", results, result => result.Outcome == ReturnToSenderSourceOutcome.Invalid);
        WriteReasonBuckets("Drift reasons", results, result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable);

        // Nonzero exit if any target failed to round-trip or drifted from the corpus.
        return invalid == 0 && unavailable == 0 ? 0 : 1;
    }

    enum TasteBucket
    {
        Correct,
        Lowering,
        KnownTaste,
        FrontierIlExact,
        FrontierIlDiff,
        FrontierIlUnknown,
        Invalid,
        Drift,
        Unsupported,
    }

    /// <summary>
    /// Buckets a probe result along two taste axes. Family comes first: authored
    /// sugar the compiler erases (<c>compiler_lowering</c>) is an inherent,
    /// unrecoverable limit, and a documented product decision (<c>known_taste</c>)
    /// is already accounted for. Everything else is the raise frontier, split by
    /// whether the shape difference is free at the IL level (Exact) or carries an
    /// opcode/operand diff (semantic).
    /// </summary>
    static TasteBucket ClassifyTaste(ReturnToSenderSourceProbeResult result)
        => result.Outcome switch
        {
            ReturnToSenderSourceOutcome.ValidMatch => TasteBucket.Correct,
            ReturnToSenderSourceOutcome.Invalid => TasteBucket.Invalid,
            ReturnToSenderSourceOutcome.SourceUnavailable => TasteBucket.Drift,
            ReturnToSenderSourceOutcome.UnsupportedTarget => TasteBucket.Unsupported,
            ReturnToSenderSourceOutcome.ValidDifferent when result.Reason.Contains("compiler_lowering", StringComparison.Ordinal) => TasteBucket.Lowering,
            ReturnToSenderSourceOutcome.ValidDifferent when result.Reason.Contains("known_taste", StringComparison.Ordinal) => TasteBucket.KnownTaste,
            ReturnToSenderSourceOutcome.ValidDifferent => result.CompileBackStatus switch
            {
                FidelityCheck.CompileBackStatus.Exact => TasteBucket.FrontierIlExact,
                FidelityCheck.CompileBackStatus.OpcodeDiff or FidelityCheck.CompileBackStatus.OperandDiff => TasteBucket.FrontierIlDiff,
                _ => TasteBucket.FrontierIlUnknown,
            },
            _ => TasteBucket.FrontierIlUnknown,
        };

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
        int invalid = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid);
        int unavailable = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable);
        int unsupported = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget);

        var payload = new
        {
            corpusRows,
            matchedAssemblies,
            corpusAssemblies,
            unmatchedRows,
            targetsEvaluated = results.Count,
            correct = match,
            validDifferent = different,
            validBreakdown = new
            {
                lowering = results.Count(result => ClassifyTaste(result) == TasteBucket.Lowering),
                knownTaste = results.Count(result => ClassifyTaste(result) == TasteBucket.KnownTaste),
                frontierIlExact = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlExact),
                frontierIlDiff = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlDiff),
                frontierIlUnknown = results.Count(result => ClassifyTaste(result) == TasteBucket.FrontierIlUnknown),
            },
            invalid,
            drift = unavailable,
            unsupported,
            rows = results.Select(result => new
            {
                type = result.Target.Type,
                method = result.Target.Method,
                overload = result.Target.Overload,
                outcome = result.Outcome.ToString(),
                tasteBucket = ClassifyTaste(result).ToString(),
                compileBackStatus = result.CompileBackStatus?.ToString(),
                reason = result.Reason,
                detail = result.Detail,
                sourceFile = result.SourcePath,
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return invalid == 0 && unavailable == 0 ? 0 : 1;
    }
}
