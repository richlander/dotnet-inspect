using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public enum RecursiveTraversalClassification
{
    None,
    TraversalRoot,
    Direct,
    Transitive,
    BeyondBound,
}

public sealed record RecursiveTraversalRoot(
    string Assembly,
    string Method,
    int MethodToken,
    int RecursionOffset,
    CallKind RecursionKind);

public sealed record RecursiveTraversalWitnessStep(
    string Caller,
    string Callee,
    int ILOffset,
    CallKind Kind,
    bool InLoop);

public sealed record RecursiveTraversalCensusRow(
    string Assembly,
    string Candidate,
    string Member,
    int MethodToken,
    int? ILOffset,
    string Shape,
    string Confidence,
    string LocalMultiplicity,
    bool LocalInLoop,
    PerformanceTriageProvenance Provenance,
    RecursiveTraversalClassification Classification,
    int? DownstreamDepth,
    RecursiveTraversalRoot? Root,
    IReadOnlyList<RecursiveTraversalWitnessStep> Witness);

public sealed record RecursiveTraversalCensusFailure(string AssemblyPath, string Error);

public sealed record RecursiveTraversalCensusReport(
    int MaxDepth,
    int Assemblies,
    int Opened,
    int Failed,
    int TraversalRoots,
    int Opportunities,
    int ReachableOpportunities,
    int DistinctReachableCandidates,
    int DistinctReachableMethods,
    IReadOnlyDictionary<string, int> RowsByClassification,
    IReadOnlyDictionary<string, int> OpportunityCross,
    IReadOnlyList<RecursiveTraversalCensusFailure> Failures,
    IReadOnlyList<RecursiveTraversalRoot> Roots,
    IReadOnlyList<RecursiveTraversalCensusRow> Rows);

public static class RecursiveTraversalCensus
{
    const string Empty = "<empty>";

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RecursiveTraversalCensusReport Measure(
        IReadOnlyList<string> assemblyPaths,
        int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 0);

        var roots = new List<RecursiveTraversalRoot>();
        var rows = new List<RecursiveTraversalCensusRow>();
        var failures = new List<RecursiveTraversalCensusFailure>();
        int opened = 0;
        foreach (string path in assemblyPaths)
        {
            try
            {
                var index = LibraryBodyIndex.Open(path);
                var result = Analyze(
                    Path.GetFileName(path),
                    index.Methods,
                    index.DirectCalls,
                    index.OptimizationOpportunities,
                    maxDepth);
                roots.AddRange(result.Roots);
                rows.AddRange(result.Rows);
                opened++;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                failures.Add(new RecursiveTraversalCensusFailure(path, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return BuildReport(
            maxDepth,
            assemblyPaths.Count,
            opened,
            failures,
            roots,
            rows);
    }

    public static (
        IReadOnlyList<RecursiveTraversalRoot> Roots,
        IReadOnlyList<RecursiveTraversalCensusRow> Rows) Analyze(
        string assembly,
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<OptimizationOpportunity> opportunities,
        int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 0);

        var methodTokens = methods
            .Select(static method => method.MetadataToken)
            .ToHashSet();
        var rootEvidence = directCalls
            .Where(call =>
                call.Kind == CallKind.Call
                && call.InLoop
                && call.CalleeDefinitionToken == call.Caller.MetadataToken
                && methodTokens.Contains(call.Caller.MetadataToken))
            .OrderBy(static call => call.Caller.MetadataToken)
            .ThenBy(static call => call.ILOffset)
            .GroupBy(static call => call.Caller.MetadataToken)
            .Select(static group => group.First())
            .Select((call, index) => CreateRootEvidence(assembly, call, index))
            .ToArray();
        var rootBySeed = rootEvidence.ToDictionary(
            static root => (root.Seed.Caller.MetadataToken, root.Seed.ILOffset));

        var graphCalls = directCalls
            .Select(static call => call with { InLoop = false })
            .Concat(rootEvidence.Select(static root => root.Seed))
            .ToImmutableArray();
        var nearest = rootEvidence.Length == 0
            ? ImmutableDictionary<int, CallerLoopEvidence>.Empty
            : CallerLoopEvidenceAnalysis.FindNearest(methods, graphCalls);

        var rows = opportunities
            .OrderBy(static opportunity => opportunity.Method.MetadataToken)
            .ThenBy(static opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(static opportunity => opportunity.Shape, StringComparer.Ordinal)
            .Select(opportunity => CreateRow(
                assembly,
                opportunity,
                nearest,
                rootBySeed,
                maxDepth))
            .ToArray();

        return (
            rootEvidence.Select(static root => root.Row).ToArray(),
            rows);
    }

    public static string ToJson(RecursiveTraversalCensusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string FormatCard(RecursiveTraversalCensusReport report, int top)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RECURSIVE-TRAVERSAL CENSUS: {report.Opened}/{report.Assemblies} assemblies opened ({report.Failed} failed), max-downstream-depth={report.MaxDepth}");
        sb.AppendLine($"  roots={report.TraversalRoots} opportunities={report.Opportunities} reachable={report.ReachableOpportunities} distinct-candidates={report.DistinctReachableCandidates} distinct-methods={report.DistinctReachableMethods}");
        AppendCounts(sb, "opportunity rows", report.RowsByClassification);
        AppendCounts(sb, "recursion|shape|confidence|local-multiplicity|provenance", report.OpportunityCross, top);
        foreach (var failure in report.Failures)
            sb.AppendLine($"  failed: {failure.AssemblyPath}: {failure.Error}");

        sb.AppendLine("  examples:");
        foreach (var row in report.Rows
            .Where(static row => row.Classification != RecursiveTraversalClassification.None)
            .OrderBy(static row => row.Classification)
            .ThenBy(static row => row.DownstreamDepth)
            .ThenBy(static row => row.Assembly, StringComparer.Ordinal)
            .ThenBy(static row => row.Member, StringComparer.Ordinal)
            .ThenBy(static row => row.Candidate, StringComparer.Ordinal)
            .Take(top))
        {
            sb.AppendLine($"    {Classification(row.Classification)} depth={row.DownstreamDepth}: {row.Assembly} {row.Member} [{row.Shape}, {row.Candidate}]");
            foreach (var step in row.Witness)
                sb.AppendLine($"      -> {step.Caller} @ IL_{step.ILOffset:X4} -- {step.Kind}{(step.InLoop ? " in-loop" : "")} --> {step.Callee}");
        }
        return sb.ToString();
    }

    static RootEvidence CreateRootEvidence(
        string assembly,
        DirectCall recursion,
        int index)
    {
        var row = new RecursiveTraversalRoot(
            assembly,
            MethodDisplay(recursion.Caller),
            recursion.Caller.MetadataToken,
            recursion.ILOffset,
            recursion.Kind);
        var syntheticCaller = recursion.Caller with
        {
            MetadataToken = int.MinValue + index,
        };
        var seed = recursion with
        {
            Caller = syntheticCaller,
            InLoop = true,
        };
        return new RootEvidence(row, recursion, seed);
    }

    static RecursiveTraversalCensusRow CreateRow(
        string assembly,
        OptimizationOpportunity opportunity,
        IReadOnlyDictionary<int, CallerLoopEvidence> nearest,
        IReadOnlyDictionary<(int MethodToken, int Offset), RootEvidence> rootBySeed,
        int maxDepth)
    {
        nearest.TryGetValue(opportunity.Method.MetadataToken, out var evidence);
        RootEvidence? root = null;
        if (evidence is { Witness.IsDefaultOrEmpty: false })
        {
            var seed = evidence.Witness[0];
            rootBySeed.TryGetValue((seed.Caller.MetadataToken, seed.ILOffset), out root);
        }

        int? downstreamDepth = evidence is null ? null : evidence.Depth - 1;
        var classification = downstreamDepth is null
            ? RecursiveTraversalClassification.None
            : downstreamDepth == 0
                ? RecursiveTraversalClassification.TraversalRoot
                : downstreamDepth == 1
                    ? RecursiveTraversalClassification.Direct
                    : downstreamDepth <= maxDepth
                        ? RecursiveTraversalClassification.Transitive
                        : RecursiveTraversalClassification.BeyondBound;
        var witness = new List<RecursiveTraversalWitnessStep>();
        if (root is not null)
        {
            witness.Add(new RecursiveTraversalWitnessStep(
                MethodDisplay(root.Recursion.Caller),
                MethodDisplay(root.Recursion.Caller),
                root.Recursion.ILOffset,
                root.Recursion.Kind,
                InLoop: true));
            witness.AddRange(evidence!.Witness.Skip(1).Select(static step =>
                new RecursiveTraversalWitnessStep(
                    MethodDisplay(step.Caller),
                    MethodDisplay(step.Callee),
                    step.ILOffset,
                    step.Kind,
                    InLoop: false)));
        }

        return new RecursiveTraversalCensusRow(
            assembly,
            opportunity.CandidateId ?? Empty,
            MethodDisplay(opportunity.Method),
            opportunity.Method.MetadataToken,
            opportunity.ILOffset,
            opportunity.Shape,
            opportunity.Confidence,
            Text(opportunity.Multiplicity),
            opportunity.InLoop,
            opportunity.Provenance,
            classification,
            downstreamDepth,
            root?.Row,
            witness);
    }

    static RecursiveTraversalCensusReport BuildReport(
        int maxDepth,
        int assemblies,
        int opened,
        IReadOnlyList<RecursiveTraversalCensusFailure> failures,
        IReadOnlyList<RecursiveTraversalRoot> roots,
        IReadOnlyList<RecursiveTraversalCensusRow> rows)
    {
        var reachable = rows
            .Where(static row => row.Classification != RecursiveTraversalClassification.None)
            .ToArray();
        var candidateRows = reachable
            .GroupBy(
                static row => row.Candidate == Empty
                    ? $"{row.Assembly}|{row.MethodToken:X8}|{row.ILOffset?.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) ?? Empty}|{row.Shape}"
                    : $"{row.Assembly}|{row.Candidate}",
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        var methodRows = reachable
            .GroupBy(static row => $"{row.Assembly}|{row.MethodToken:X8}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return new RecursiveTraversalCensusReport(
            maxDepth,
            assemblies,
            opened,
            failures.Count,
            roots.Count,
            rows.Count,
            reachable.Length,
            candidateRows.Length,
            methodRows.Length,
            Counts(rows, static row => Classification(row.Classification)),
            Counts(reachable, static row =>
                $"{Classification(row.Classification)}|{row.Shape}|{row.Confidence}|{row.LocalMultiplicity}|{row.Provenance.ToString().ToLowerInvariant()}"),
            failures,
            roots,
            rows);
    }

    static string MethodDisplay(MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}({string.Join(", ", method.ParameterTypes.Select(static type => type.ToQualifiedDisplayString()))})";

    static string Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? Empty : value;

    static string Classification(RecursiveTraversalClassification value)
        => value switch
        {
            RecursiveTraversalClassification.TraversalRoot => "traversal-root",
            RecursiveTraversalClassification.Direct => "direct",
            RecursiveTraversalClassification.Transitive => "transitive",
            RecursiveTraversalClassification.BeyondBound => "beyond-bound",
            _ => "none",
        };

    static IReadOnlyDictionary<string, int> Counts<T>(
        IEnumerable<T> values,
        Func<T, string> key)
        => values
            .GroupBy(key, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    static void AppendCounts(
        StringBuilder sb,
        string title,
        IReadOnlyDictionary<string, int> counts,
        int top = int.MaxValue)
    {
        sb.AppendLine($"  {title}:");
        foreach (var (key, value) in counts.Take(top))
            sb.AppendLine($"    {key}={value}");
        if (counts.Count > top)
            sb.AppendLine($"    ... {counts.Count - top} more");
    }

    sealed record RootEvidence(
        RecursiveTraversalRoot Row,
        DirectCall Recursion,
        DirectCall Seed);
}
