using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Per-method analysis signals that describe the <em>kind</em> of work a method
/// does, complementing the scale/leverage cues (fanin/fanout/depth/loop). Most are
/// derived from the call index; the IL-scan-derived parts (array allocations,
/// throws, exception regions) are folded in from the body scan. The vocabulary
/// grows additively — each new field is a signal plus a projection case.
/// </summary>
/// <param name="Allocations">Heap allocations in the body: <c>newobj</c> plus <c>newarr</c>.</param>
/// <param name="Copies">Calls to well-known copy/materialize APIs (ToArray, Substring, …).</param>
/// <param name="Unsafe">The method has any unsafe evidence.</param>
/// <param name="Reflection">Calls into reflection-style APIs (System.Reflection, Activator, System.Linq.Expressions).</param>
/// <param name="Throws"><c>throw</c>/<c>rethrow</c> sites in the body.</param>
/// <param name="Catches">Exception-handling clauses with a catch/filter handler.</param>
/// <param name="Finallys"><c>finally</c>/<c>fault</c> handler clauses.</param>
/// <param name="EvidenceOffsets">IL offsets of the signal-bearing instructions, as compact receipts.</param>
public sealed record MethodSignals(
    int Allocations,
    int Copies,
    bool Unsafe,
    int Reflection = 0,
    int Throws = 0,
    int Catches = 0,
    int Finallys = 0,
    ImmutableArray<int> EvidenceOffsets = default)
{
    public static readonly MethodSignals None = new(0, 0, false);

    /// <summary>Signal-bearing IL offsets, normalized to an empty (never default) array.</summary>
    public ImmutableArray<int> Evidence => EvidenceOffsets.IsDefault ? [] : EvidenceOffsets;
}

/// <summary>
/// IL-scan-derived signals for a single method body, collected during index build
/// (the call index alone cannot see <c>newarr</c>, <c>throw</c>, or exception
/// regions). Merged into <see cref="MethodSignals"/> by
/// <see cref="MethodSignalAnalysis.Collect"/>.
/// </summary>
public readonly record struct BodySignals(
    int Newarr,
    int Throws,
    int Catches,
    int Finallys,
    ImmutableArray<int> ArrayAllocOffsets,
    ImmutableArray<int> ThrowOffsets);

public static class MethodSignalAnalysis
{
    // How many evidence IL offsets to retain per method. A compact receipt — enough
    // to point at the signal sites without dumping the whole body.
    const int MaxEvidenceOffsets = 12;

    /// <summary>
    /// Aggregates per-method signals keyed by the method's metadata token. The
    /// call-derived parts (object allocations, copies, reflection, unsafe) come from
    /// <paramref name="directCalls"/>/<paramref name="unsafeEvidence"/>; the
    /// IL-scan parts (array allocations, throws, exception regions) are merged from
    /// <paramref name="bodySignals"/>. Pure over its inputs so it is testable without
    /// a real assembly.
    /// </summary>
    public static Dictionary<int, MethodSignals> Collect(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<int, BodySignals>? bodySignals = null)
    {
        var allocations = new Dictionary<int, int>();
        var copies = new Dictionary<int, int>();
        var reflection = new Dictionary<int, int>();
        var evidence = new Dictionary<int, SortedSet<int>>();

        void AddEvidence(int token, int offset)
        {
            if (offset < 0)
                return;
            if (!evidence.TryGetValue(token, out var set))
                evidence[token] = set = [];
            set.Add(offset);
        }

        foreach (var call in directCalls)
        {
            int caller = call.Caller.MetadataToken;
            if (call.Kind == CallKind.NewObject)
            {
                allocations[caller] = allocations.GetValueOrDefault(caller) + 1;
                AddEvidence(caller, call.ILOffset);
            }
            if (call.Kind is CallKind.LoadFunction or CallKind.LoadVirtualFunction)
                AddEvidence(caller, call.ILOffset);
            if (IsCopyApi(call.Callee))
            {
                copies[caller] = copies.GetValueOrDefault(caller) + 1;
                AddEvidence(caller, call.ILOffset);
            }
            if (IsReflectionApi(call.Callee))
            {
                reflection[caller] = reflection.GetValueOrDefault(caller) + 1;
                AddEvidence(caller, call.ILOffset);
            }
        }

        var unsafeMethods = new HashSet<int>();
        foreach (var evidenceItem in unsafeEvidence)
            unsafeMethods.Add(evidenceItem.Member.MetadataToken);

        var tokens = new HashSet<int>(allocations.Keys);
        tokens.UnionWith(copies.Keys);
        tokens.UnionWith(reflection.Keys);
        tokens.UnionWith(unsafeMethods);
        if (bodySignals is not null)
            tokens.UnionWith(bodySignals.Keys);

        var result = new Dictionary<int, MethodSignals>(tokens.Count);
        foreach (var token in tokens)
        {
            BodySignals body = default;
            bodySignals?.TryGetValue(token, out body);

            foreach (var offset in NormalizeOffsets(body.ArrayAllocOffsets))
                AddEvidence(token, offset);
            foreach (var offset in NormalizeOffsets(body.ThrowOffsets))
                AddEvidence(token, offset);

            var offsets = evidence.TryGetValue(token, out var set)
                ? [.. set.Take(MaxEvidenceOffsets)]
                : ImmutableArray<int>.Empty;

            result[token] = new MethodSignals(
                allocations.GetValueOrDefault(token) + body.Newarr,
                copies.GetValueOrDefault(token),
                unsafeMethods.Contains(token),
                reflection.GetValueOrDefault(token),
                body.Throws,
                body.Catches,
                body.Finallys,
                offsets);
        }
        return result;
    }

    static ImmutableArray<int> NormalizeOffsets(ImmutableArray<int> offsets)
        => offsets.IsDefault ? [] : offsets;

    // Well-known copy/materialize APIs. Name-based, high-confidence members whose
    // dominant effect is producing a copy of existing data. Intentionally small;
    // grow deliberately to keep false positives low.
    static bool IsCopyApi(MemberRef callee)
        => callee.Kind != MemberKind.Unsupported
           && callee.Name is "ToArray" or "ToList" or "CopyTo" or "GetSubArray"
               or "Substring" or "Concat" or "Join";

    // Reflection-style APIs, identified by the callee's declaring namespace (and, for
    // System.Type, a curated member set). These are runtime metadata / dynamic-invocation
    // surfaces (System.Reflection), dynamic construction (System.Activator), expression
    // trees (System.Linq.Expressions), and the reflective members on System.Type — all
    // signals that a method does dynamic work.
    static bool IsReflectionApi(MemberRef callee)
    {
        if (callee.Kind == MemberKind.Unsupported)
            return false;
        var ns = callee.DeclaringType.Namespace;
        if (ns is "System.Reflection" || ns.StartsWith("System.Reflection.", StringComparison.Ordinal)
            || ns is "System.Linq.Expressions")
            return true;
        if (ns != "System")
            return false;
        return callee.DeclaringType.Name switch
        {
            "Activator" => true,
            // System.Type's reflective surface. A curated set, not a Get* prefix, so the
            // ubiquitous typeof lowering (Type.GetTypeFromHandle) and Type.GetHashCode
            // are not mistaken for reflection work. Static Type.GetType(string) resolves
            // a type by name and is reflection; object.GetType() is on System.Object, not
            // System.Type, so it never reaches here.
            "Type" => callee.Name is "GetType" or "GetMethod" or "GetMethods"
                or "GetProperty" or "GetProperties" or "GetField" or "GetFields"
                or "GetConstructor" or "GetConstructors" or "GetMember" or "GetMembers"
                or "GetInterface" or "GetInterfaces" or "GetNestedType" or "GetNestedTypes"
                or "GetEvent" or "GetEvents" or "GetCustomAttributes" or "InvokeMember"
                or "FindMembers" or "MakeGenericType" or "MakeArrayType" or "MakePointerType",
            _ => false,
        };
    }
}
