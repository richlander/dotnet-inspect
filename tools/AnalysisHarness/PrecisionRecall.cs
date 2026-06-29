using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>A triage candidate distilled for precision/recall judgement.</summary>
public sealed record TriageCandidate(string Type, string Method, string Signature, string Shape, string Confidence, bool InLoop, int RootReach);

/// <summary>A reference paydirt entry: a method+shape a healthy analyzer must keep surfacing.</summary>
public sealed record PaydirtReference(string Assembly, string Type, string Method, string Signature, string Shape);

public sealed record RecallResult(IReadOnlyList<string> Missing, int Expected)
{
    public bool Passed => Missing.Count == 0;
}

public sealed record PrecisionSample(IReadOnlyList<TriageCandidate> Candidates);

/// <summary>
/// Layer 3 of #1818: precision and recall over real triage candidates. There is no mechanical
/// "is this a true finding" oracle, so the layer splits in two. RECALL is mechanical once
/// curated — a committed reference paydirt list (top loop+high sites with stable reach) must keep
/// surfacing; the curation is the human step, the coverage check is automatic. PRECISION is the
/// genuinely judged loop — sample the top-N candidates as a worksheet, an agent labels each true/
/// false positive, and FP rate is computed from the labels.
/// </summary>
public static class PrecisionRecall
{
    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<TriageCandidate> Candidates(string assemblyPath)
    {
        var index = LibraryBodyIndex.Open(assemblyPath);
        return index.OptimizationOpportunities
            .Select(o => new TriageCandidate(
                o.Method.DeclaringType.ToQualifiedDisplayString(),
                o.Method.Name,
                Signature(o.Method),
                o.Shape, o.Confidence, o.InLoop, o.RootReach))
            .OrderByDescending(c => c.InLoop && c.Confidence == "high")
            .ThenByDescending(c => c.RootReach)
            .ThenBy(c => c.Type, StringComparer.Ordinal)
            .ThenBy(c => c.Method, StringComparer.Ordinal)
            .ThenBy(c => c.Signature, StringComparer.Ordinal)
            .ThenBy(c => c.Shape, StringComparer.Ordinal)
            .ToList();
    }

    static string Signature(MethodIdentity m)
        => string.Join(",", m.ParameterTypes.Select(p => p.ToQualifiedDisplayString()));

    // Recall: every reference paydirt site for an assembly must still be present (loop+high), or
    // it is a recall regression. Matched by qualified type + method + signature + shape so a
    // same-leaf-name overload or another namespace cannot keep a stale reference green.
    public static RecallResult CheckRecall(string assemblyPath, IReadOnlyList<PaydirtReference> references)
    {
        var present = new HashSet<(string, string, string, string)>(
            Candidates(assemblyPath).Where(c => c.InLoop && c.Confidence == "high")
                .Select(c => (c.Type, c.Method, c.Signature, c.Shape)));
        var missing = references
            .Where(r => !present.Contains((r.Type, r.Method, r.Signature, r.Shape)))
            .Select(r => $"{r.Type}.{r.Method}({r.Signature}) [{r.Shape}]")
            .ToList();
        return new RecallResult(missing, references.Count);
    }

    public static PrecisionSample Sample(string assemblyPath, int top)
        => new(Candidates(assemblyPath).Take(top).ToList());

    public static string ToJson(PrecisionSample sample) => JsonSerializer.Serialize(sample, s_json);
    public static string ToJson(IReadOnlyList<PaydirtReference> r) => JsonSerializer.Serialize(r, s_json);
    public static IReadOnlyList<PaydirtReference> ReferencesFromJson(string json)
        => JsonSerializer.Deserialize<List<PaydirtReference>>(json, s_json) ?? [];

    public static string FormatRecall(string assembly, RecallResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PAYDIRT RECALL {assembly}: {r.Expected - r.Missing.Count}/{r.Expected} present" + (r.Passed ? "" : "  REGRESSION"));
        foreach (var m in r.Missing) sb.AppendLine($"  MISSING: {m}");
        return sb.ToString();
    }
}
