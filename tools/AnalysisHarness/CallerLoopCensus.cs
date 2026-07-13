using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public enum CallerLoopClassification
{
    None,
    Direct,
    Transitive,
    BeyondBound,
}

public sealed record CallerLoopWitnessStep(
    string Caller,
    string Callee,
    int ILOffset,
    CallKind Kind,
    bool InLoop);

public sealed record CallerLoopCensusRow(
    string Assembly,
    string Candidate,
    string Member,
    int MethodToken,
    int? ILOffset,
    string Shape,
    string Confidence,
    string LocalMultiplicity,
    bool LocalInLoop,
    string Path,
    PerformanceTriageProvenance Provenance,
    CallerLoopClassification Classification,
    int? NearestDepth,
    IReadOnlyList<CallerLoopWitnessStep> Witness);

public sealed record CallerLoopCensusFailure(string AssemblyPath, string Error);

public sealed record CallerLoopCensusReport(
    int MaxDepth,
    int Assemblies,
    int Opened,
    int Failed,
    int Opportunities,
    int DistinctCandidates,
    int DistinctMethods,
    IReadOnlyDictionary<string, int> RowsByClassification,
    IReadOnlyDictionary<string, int> CandidatesByClassification,
    IReadOnlyDictionary<string, int> MethodsByClassification,
    IReadOnlyDictionary<string, int> RowsByDepth,
    IReadOnlyDictionary<string, int> ProvenanceCross,
    IReadOnlyDictionary<string, int> OpportunityCross,
    IReadOnlyList<CallerLoopCensusFailure> Failures,
    IReadOnlyList<CallerLoopCensusRow> Rows);

public static class CallerLoopCensus
{
    const string Empty = "<empty>";

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CallerLoopCensusReport Measure(IReadOnlyList<string> assemblyPaths, int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);

        var rows = new List<CallerLoopCensusRow>();
        var failures = new List<CallerLoopCensusFailure>();
        int opened = 0;
        foreach (string path in assemblyPaths)
        {
            try
            {
                var index = LibraryBodyIndex.Open(path);
                rows.AddRange(Analyze(
                    Path.GetFileName(path),
                    index.Methods,
                    index.DirectCalls,
                    index.OptimizationOpportunities,
                    maxDepth));
                opened++;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                failures.Add(new CallerLoopCensusFailure(path, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return BuildReport(maxDepth, assemblyPaths.Count, opened, failures, rows);
    }

    public static IReadOnlyList<CallerLoopCensusRow> Analyze(
        string assembly,
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<OptimizationOpportunity> opportunities,
        int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);

        var methodByToken = methods.ToDictionary(static method => method.MetadataToken);
        var methodMap = MethodDefinitionMap.Create(methods);
        var edges = directCalls
            .Where(IsInvocation)
            .Select(call => new GraphEdge(call, methodMap.Resolve(call)))
            .Where(static edge => edge.CalleeToken != 0)
            .OrderBy(edge => EdgeKey(edge.Call, methodByToken[edge.CalleeToken]), StringComparer.Ordinal)
            .ToArray();

        var adjacency = edges
            .GroupBy(static edge => edge.Call.Caller.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        var bestByToken = NearestLoopWitnesses(edges, adjacency);
        return opportunities
            .OrderBy(static opportunity => opportunity.Method.MetadataToken)
            .ThenBy(static opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(static opportunity => opportunity.Shape, StringComparer.Ordinal)
            .Select(opportunity => CreateRow(assembly, opportunity, bestByToken, methodByToken, maxDepth))
            .ToArray();
    }

    public static string ToJson(CallerLoopCensusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string FormatCard(CallerLoopCensusReport report, int top)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CALLER-LOOP CENSUS: {report.Opened}/{report.Assemblies} assemblies opened ({report.Failed} failed), max-depth={report.MaxDepth}");
        sb.AppendLine($"  opportunities={report.Opportunities} candidates={report.DistinctCandidates} methods={report.DistinctMethods}");
        AppendCounts(sb, "rows", report.RowsByClassification);
        AppendCounts(sb, "distinct candidates", report.CandidatesByClassification);
        AppendCounts(sb, "distinct methods", report.MethodsByClassification);
        AppendCounts(sb, "nearest depth", report.RowsByDepth);
        AppendCounts(sb, "provenance|caller-loop", report.ProvenanceCross);
        AppendCounts(sb, "caller-loop|shape|confidence|local-multiplicity", report.OpportunityCross, top);
        foreach (var failure in report.Failures)
            sb.AppendLine($"  failed: {failure.AssemblyPath}: {failure.Error}");

        sb.AppendLine("  examples:");
        foreach (var row in report.Rows
            .Where(static row => row.Classification != CallerLoopClassification.None)
            .OrderBy(static row => row.Classification)
            .ThenBy(static row => row.NearestDepth)
            .ThenBy(static row => row.Assembly, StringComparer.Ordinal)
            .ThenBy(static row => row.Member, StringComparer.Ordinal)
            .ThenBy(static row => row.Candidate, StringComparer.Ordinal)
            .Take(top))
        {
            string witness = string.Join(" -> ", row.Witness.Select(static step => $"{step.Caller} @ IL_{step.ILOffset:X4}"));
            if (row.Witness.Count > 0)
                witness += $" -> {row.Witness[^1].Callee}";
            sb.AppendLine($"    {Classification(row.Classification)} depth={row.NearestDepth}: {row.Assembly} {row.Member} [{row.Shape}, {row.Candidate}]");
            sb.AppendLine($"      {witness}");
        }
        return sb.ToString();
    }

    static Dictionary<int, WitnessState> NearestLoopWitnesses(
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyDictionary<int, GraphEdge[]> adjacency)
    {
        var best = new Dictionary<int, WitnessState>();
        var seeds = new List<WitnessState>();
        foreach (var edge in edges.Where(static edge =>
            edge.Call.InLoop
            && edge.Call.Caller.MetadataToken != edge.CalleeToken))
        {
            if (best.ContainsKey(edge.CalleeToken))
                continue;
            var state = WitnessState.Seed(edge);
            best[edge.CalleeToken] = state;
            seeds.Add(state);
        }

        WitnessState[] frontier = [.. seeds];
        while (frontier.Length > 0)
        {
            var next = new Dictionary<int, WitnessState>();
            var orderedNext = new List<WitnessState>();
            foreach (var state in frontier)
            {
                if (!adjacency.TryGetValue(state.Token, out var outgoing))
                    continue;
                foreach (var edge in outgoing)
                {
                    if (best.ContainsKey(edge.CalleeToken))
                        continue;
                    // Returning to the loop owner is recursive repetition, not evidence that
                    // a distinct upstream caller loop repeats the method.
                    if (edge.CalleeToken == state.LoopCallerToken)
                        continue;
                    if (next.ContainsKey(edge.CalleeToken))
                        continue;
                    var candidate = state.Append(edge);
                    next[edge.CalleeToken] = candidate;
                    orderedNext.Add(candidate);
                }
            }

            frontier = [.. orderedNext];
            foreach (var state in frontier)
                best[state.Token] = state;
        }
        return best;
    }

    static CallerLoopCensusRow CreateRow(
        string assembly,
        OptimizationOpportunity opportunity,
        IReadOnlyDictionary<int, WitnessState> bestByToken,
        IReadOnlyDictionary<int, MethodIdentity> methodByToken,
        int maxDepth)
    {
        bestByToken.TryGetValue(opportunity.Method.MetadataToken, out var witness);
        var classification = witness is null
            ? CallerLoopClassification.None
            : witness.Depth == 1
                ? CallerLoopClassification.Direct
                : witness.Depth <= maxDepth
                    ? CallerLoopClassification.Transitive
                    : CallerLoopClassification.BeyondBound;
        return new CallerLoopCensusRow(
            assembly,
            opportunity.CandidateId ?? Empty,
            MethodDisplay(opportunity.Method),
            opportunity.Method.MetadataToken,
            opportunity.ILOffset,
            opportunity.Shape,
            opportunity.Confidence,
            Text(opportunity.Multiplicity),
            opportunity.InLoop,
            Text(opportunity.PathContext),
            opportunity.Provenance,
            classification,
            witness?.Depth,
            witness?.BuildSteps(methodByToken) ?? []);
    }

    static CallerLoopCensusReport BuildReport(
        int maxDepth,
        int assemblies,
        int opened,
        IReadOnlyList<CallerLoopCensusFailure> failures,
        IReadOnlyList<CallerLoopCensusRow> rows)
    {
        var candidateRows = rows
            .GroupBy(
                static row => row.Candidate == Empty
                    ? $"{row.Assembly}|{row.MethodToken:X8}|{row.ILOffset?.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) ?? Empty}|{row.Shape}"
                    : $"{row.Assembly}|{row.Candidate}",
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        var methodRows = rows
            .GroupBy(static row => $"{row.Assembly}|{row.MethodToken:X8}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return new CallerLoopCensusReport(
            maxDepth,
            assemblies,
            opened,
            failures.Count,
            rows.Count,
            candidateRows.Length,
            methodRows.Length,
            Counts(rows, static row => Classification(row.Classification)),
            Counts(candidateRows, static row => Classification(row.Classification)),
            Counts(methodRows, static row => Classification(row.Classification)),
            Counts(rows, static row => row.NearestDepth?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Empty),
            Counts(rows, static row => $"{row.Provenance.ToString().ToLowerInvariant()}|{Classification(row.Classification)}"),
            Counts(rows, static row => $"{Classification(row.Classification)}|{row.Shape}|{row.Confidence}|{row.LocalMultiplicity}"),
            failures,
            rows);
    }

    static bool IsInvocation(DirectCall call)
        => call.Kind is CallKind.Call or CallKind.CallVirtual or CallKind.NewObject;

    static string EdgeKey(DirectCall call, MethodIdentity callee)
        => $"{MethodKey(call.Caller)}|{call.ILOffset:X8}|{MethodKey(callee)}|{call.Kind}";

    static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static string MethodDisplay(MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}({string.Join(", ", method.ParameterTypes.Select(static type => type.ToQualifiedDisplayString()))})";

    static string MethodDisplay(MemberRef member)
        => $"{member.DeclaringType.ToQualifiedDisplayString()}::{member.Name}({string.Join(", ", member.ParameterTypes.Select(static type => type.ToQualifiedDisplayString()))})";

    static string Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? Empty : value;

    static string Classification(CallerLoopClassification value)
        => value switch
        {
            CallerLoopClassification.Direct => "direct",
            CallerLoopClassification.Transitive => "transitive",
            CallerLoopClassification.BeyondBound => "beyond-bound",
            _ => "none",
        };

    static IReadOnlyDictionary<string, int> Counts<T>(IEnumerable<T> values, Func<T, string> key)
        => values
            .GroupBy(key, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    static void AppendCounts(StringBuilder sb, string title, IReadOnlyDictionary<string, int> counts, int top = int.MaxValue)
    {
        sb.AppendLine($"  {title}:");
        foreach (var (key, value) in counts.Take(top))
            sb.AppendLine($"    {key}={value}");
        if (counts.Count > top)
            sb.AppendLine($"    ... {counts.Count - top} more");
    }

    sealed record GraphEdge(DirectCall Call, int CalleeToken);

    sealed record WitnessState(
        int Token,
        int Depth,
        int LoopCallerToken,
        GraphEdge Incoming,
        WitnessState? Previous)
    {
        public static WitnessState Seed(GraphEdge edge)
            => new(
                edge.CalleeToken,
                1,
                edge.Call.Caller.MetadataToken,
                edge,
                null);

        public WitnessState Append(GraphEdge edge)
            => new(
                edge.CalleeToken,
                Depth + 1,
                LoopCallerToken,
                edge,
                this);

        public IReadOnlyList<CallerLoopWitnessStep> BuildSteps(IReadOnlyDictionary<int, MethodIdentity> methodByToken)
        {
            var reversed = new List<GraphEdge>(Depth);
            for (WitnessState? state = this; state is not null; state = state.Previous)
                reversed.Add(state.Incoming);
            reversed.Reverse();
            return reversed.Select(edge =>
            {
                var callee = methodByToken[edge.CalleeToken];
                return new CallerLoopWitnessStep(
                    MethodDisplay(edge.Call.Caller),
                    MethodDisplay(callee),
                    edge.Call.ILOffset,
                    edge.Call.Kind,
                    edge.Call.InLoop);
            }).ToArray();
        }
    }
}
