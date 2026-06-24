using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Per-method analysis signals that describe the <em>kind</em> of work a method
/// does, complementing the scale/leverage cues (fanin/fanout/depth/loop). Derived
/// from the call index and unsafe evidence, so they are cheap and need no extra
/// IL scan. The vocabulary starts small and is expected to grow (newarr, throw,
/// reflection, …).
/// </summary>
public sealed record MethodSignals(int Allocations, int Copies, bool Unsafe)
{
    public static readonly MethodSignals None = new(0, 0, false);
}

public static class MethodSignalAnalysis
{
    /// <summary>
    /// Aggregates per-method signals keyed by the method's metadata token:
    /// <list type="bullet">
    /// <item><c>Allocations</c> — <c>newobj</c> object allocations in the body.</item>
    /// <item><c>Copies</c> — calls to well-known copy/materialize APIs (ToArray, Substring, string.Concat, …).</item>
    /// <item><c>Unsafe</c> — the method has any unsafe evidence.</item>
    /// </list>
    /// Pure over the index's arrays so it is testable without a real assembly.
    /// </summary>
    public static Dictionary<int, MethodSignals> Collect(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<UnsafeEvidence> unsafeEvidence)
    {
        var allocations = new Dictionary<int, int>();
        var copies = new Dictionary<int, int>();

        foreach (var call in directCalls)
        {
            int caller = call.Caller.MetadataToken;
            if (call.Kind == CallKind.NewObject)
                allocations[caller] = allocations.GetValueOrDefault(caller) + 1;
            if (IsCopyApi(call.Callee))
                copies[caller] = copies.GetValueOrDefault(caller) + 1;
        }

        var unsafeMethods = new HashSet<int>();
        foreach (var evidence in unsafeEvidence)
            unsafeMethods.Add(evidence.Member.MetadataToken);

        var tokens = new HashSet<int>(allocations.Keys);
        tokens.UnionWith(copies.Keys);
        tokens.UnionWith(unsafeMethods);

        var result = new Dictionary<int, MethodSignals>(tokens.Count);
        foreach (var token in tokens)
            result[token] = new MethodSignals(
                allocations.GetValueOrDefault(token),
                copies.GetValueOrDefault(token),
                unsafeMethods.Contains(token));
        return result;
    }

    // Well-known copy/materialize APIs. Name-based, high-confidence members whose
    // dominant effect is producing a copy of existing data. Intentionally small;
    // grow deliberately to keep false positives low.
    static bool IsCopyApi(MemberRef callee)
        => callee.Kind != MemberKind.Unsupported
           && callee.Name is "ToArray" or "ToList" or "CopyTo" or "GetSubArray"
               or "Substring" or "Concat" or "Join";
}
