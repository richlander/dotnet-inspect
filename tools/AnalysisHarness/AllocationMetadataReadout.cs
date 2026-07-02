using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record AllocationReadout(
    int Assemblies,
    int Opened,
    int Failed,
    int Methods,
    int AllocationOccurrences,
    int OptimizationOpportunities,
    IReadOnlyDictionary<string, int> OccurrenceKind,
    IReadOnlyDictionary<string, int> OccurrenceAllocation,
    IReadOnlyDictionary<string, int> OccurrencePath,
    IReadOnlyDictionary<string, int> OccurrencePathConfidence,
    IReadOnlyDictionary<string, int> OccurrencePostDominance,
    IReadOnlyDictionary<string, int> OccurrenceEscape,
    IReadOnlyDictionary<string, int> OccurrenceCross,
    IReadOnlyDictionary<string, int> OpportunityShape,
    IReadOnlyDictionary<string, int> OpportunityAllocation,
    IReadOnlyDictionary<string, int> OpportunityPath,
    IReadOnlyDictionary<string, int> OpportunityPathConfidence,
    IReadOnlyDictionary<string, int> OpportunityPostDominance,
    IReadOnlyDictionary<string, int> OpportunityConfidence,
    IReadOnlyDictionary<string, int> OpportunityCross);

public static class AllocationMetadataReadout
{
    const string Empty = "<empty>";

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AllocationReadout Measure(IReadOnlyList<string> assemblyPaths)
    {
        var builder = new Builder(assemblyPaths.Count);
        foreach (var assemblyPath in assemblyPaths)
            builder.AddAssembly(assemblyPath);
        return builder.Build();
    }

    public static string ToJson(AllocationReadout readout)
        => JsonSerializer.Serialize(readout, s_json);

    public static string FormatCard(AllocationReadout readout, int top)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ALLOCATION METADATA READOUT: {readout.Opened}/{readout.Assemblies} assemblies opened ({readout.Failed} failed)");
        sb.AppendLine($"  methods={readout.Methods} allocation-occurrences={readout.AllocationOccurrences} optimization-opportunities={readout.OptimizationOpportunities}");
        AppendCounts(sb, "occurrence kind", readout.OccurrenceKind, top);
        AppendCounts(sb, "occurrence allocation", readout.OccurrenceAllocation, top);
        AppendCounts(sb, "occurrence path", readout.OccurrencePath, top);
        AppendCounts(sb, "occurrence path confidence", readout.OccurrencePathConfidence, top);
        AppendCounts(sb, "occurrence post dominance", readout.OccurrencePostDominance, top);
        AppendCounts(sb, "occurrence escape", readout.OccurrenceEscape, top);
        AppendCounts(sb, "opportunity shape", readout.OpportunityShape, top);
        AppendCounts(sb, "opportunity allocation", readout.OpportunityAllocation, top);
        AppendCounts(sb, "opportunity path", readout.OpportunityPath, top);
        AppendCounts(sb, "opportunity path confidence", readout.OpportunityPathConfidence, top);
        AppendCounts(sb, "opportunity post dominance", readout.OpportunityPostDominance, top);
        AppendCounts(sb, "opportunity confidence", readout.OpportunityConfidence, top);
        AppendCounts(sb, "opportunity cross shape|allocation|path|path-confidence|post-dominance", readout.OpportunityCross, top);
        AppendCounts(sb, "occurrence cross kind|allocation|path|path-confidence|post-dominance|escape", readout.OccurrenceCross, top);
        return sb.ToString();
    }

    static void AppendCounts(StringBuilder sb, string title, IReadOnlyDictionary<string, int> counts, int top)
    {
        sb.AppendLine($"  {title}:");
        foreach (var (key, value) in counts.OrderByDescending(static kv => kv.Value).ThenBy(static kv => kv.Key, StringComparer.Ordinal).Take(top))
            sb.AppendLine($"    {key}={value}");
        if (counts.Count > top)
            sb.AppendLine($"    ... {counts.Count - top} more");
    }

    static string PathContext(AllocationPathContext value)
        => value switch
        {
            AllocationPathContext.Branch => "branch",
            AllocationPathContext.SwitchArm => "switch arm",
            AllocationPathContext.LoopBody => "loop body",
            AllocationPathContext.ErrorPath => "error path",
            _ => "straight-line",
        };

    static string PathConfidence(AllocationPathConfidence value)
        => value switch
        {
            AllocationPathConfidence.DominatesReturn => "dominates-return",
            AllocationPathConfidence.BehindBranch => "behind-branch",
            _ => Empty,
        };

    static string PostDominance(AllocationPostDominance value)
        => value switch
        {
            AllocationPostDominance.ReturnPostDominates => "return-post-dominates",
            _ => Empty,
        };

    static string Escape(AllocationEscape value)
        => value switch
        {
            AllocationEscape.LocalOnly => "local-only",
            AllocationEscape.Escapes => "escapes",
            AllocationEscape.ThrowPath => "throw-path",
            _ => Empty,
        };

    static string Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? Empty : value;

    sealed class Builder
    {
        readonly int _assemblies;
        readonly Dictionary<string, int> _occurrenceKind = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrenceAllocation = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrencePath = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrencePathConfidence = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrencePostDominance = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrenceEscape = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _occurrenceCross = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityShape = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityAllocation = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityPath = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityPathConfidence = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityPostDominance = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityConfidence = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _opportunityCross = new(StringComparer.Ordinal);
        int _opened;
        int _methods;
        int _allocationOccurrences;
        int _optimizationOpportunities;

        public Builder(int assemblies)
        {
            _assemblies = assemblies;
        }

        public void AddAssembly(string path)
        {
            LibraryBodyIndex index;
            try
            {
                index = LibraryBodyIndex.Open(path);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                return;
            }

            _opened++;
            _methods += index.Methods.Length;
            foreach (var occurrence in index.GetAllocationOccurrences().Values.SelectMany(static value => value))
            {
                _allocationOccurrences++;
                var kind = occurrence.Kind.ToString();
                var allocation = Text(occurrence.RuntimeAllocationType);
                var pathContext = PathContext(occurrence.PathContext);
                var pathConfidence = PathConfidence(occurrence.PathConfidence);
                var postDominance = PostDominance(occurrence.PostDominance);
                var escape = Escape(occurrence.Escape);
                Add(_occurrenceKind, kind);
                Add(_occurrenceAllocation, allocation);
                Add(_occurrencePath, pathContext);
                Add(_occurrencePathConfidence, pathConfidence);
                Add(_occurrencePostDominance, postDominance);
                Add(_occurrenceEscape, escape);
                Add(_occurrenceCross, string.Join('|', kind, allocation, pathContext, pathConfidence, postDominance, escape));
            }

            foreach (var opportunity in index.OptimizationOpportunities)
            {
                _optimizationOpportunities++;
                var allocation = Text(opportunity.RuntimeAllocationType);
                var pathContext = Text(opportunity.PathContext);
                var pathConfidence = Text(opportunity.PathConfidence);
                var postDominance = Text(opportunity.PostDominance);
                Add(_opportunityShape, opportunity.Shape);
                Add(_opportunityAllocation, allocation);
                Add(_opportunityPath, pathContext);
                Add(_opportunityPathConfidence, pathConfidence);
                Add(_opportunityPostDominance, postDominance);
                Add(_opportunityConfidence, opportunity.Confidence);
                Add(_opportunityCross, string.Join('|', opportunity.Shape, allocation, pathContext, pathConfidence, postDominance));
            }
        }

        public AllocationReadout Build()
            => new(
                _assemblies,
                _opened,
                _assemblies - _opened,
                _methods,
                _allocationOccurrences,
                _optimizationOpportunities,
                Sort(_occurrenceKind),
                Sort(_occurrenceAllocation),
                Sort(_occurrencePath),
                Sort(_occurrencePathConfidence),
                Sort(_occurrencePostDominance),
                Sort(_occurrenceEscape),
                Sort(_occurrenceCross),
                Sort(_opportunityShape),
                Sort(_opportunityAllocation),
                Sort(_opportunityPath),
                Sort(_opportunityPathConfidence),
                Sort(_opportunityPostDominance),
                Sort(_opportunityConfidence),
                Sort(_opportunityCross));

        static void Add(Dictionary<string, int> counts, string key)
            => counts[key] = counts.GetValueOrDefault(key) + 1;

        static IReadOnlyDictionary<string, int> Sort(Dictionary<string, int> counts)
            => counts
                .OrderByDescending(static kv => kv.Value)
                .ThenBy(static kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.Ordinal);
    }
}
