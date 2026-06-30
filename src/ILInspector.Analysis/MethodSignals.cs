using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Per-method analysis signals that describe the <em>kind</em> of work a method
/// does, complementing the scale/leverage cues (fanin/fanout/depth/loop). Most are
/// derived from the call index; the IL-scan-derived parts (array allocations,
/// throws, exception regions) are folded in from the body scan. The vocabulary
/// grows additively — each new field is a signal plus a projection case.
/// </summary>
/// <param name="Allocations">Heap allocations in the body: <c>newobj</c>, <c>newarr</c>, plus <c>box</c> of value types.</param>
/// <param name="Copies">Calls to well-known copy/materialize APIs (ToArray, Substring, …).</param>
/// <param name="Unsafe">The method has any unsafe evidence.</param>
/// <param name="Reflection">Calls into reflection-style APIs (System.Reflection, Activator, System.Linq.Expressions).</param>
/// <param name="Throws"><c>throw</c>/<c>rethrow</c> sites in the body.</param>
/// <param name="Catches">Exception-handling clauses with a catch/filter handler.</param>
/// <param name="Finallys"><c>finally</c>/<c>fault</c> handler clauses.</param>
/// <param name="EvidenceOffsets">IL offsets of the signal-bearing instructions, as compact receipts.</param>
/// <param name="ExceptionTypeNames">Distinct exception types constructed (<c>newobj</c> of a <c>*Exception</c> type) in the body.</param>
/// <param name="AllocInLoop">Whether the method allocates inside a loop (non-exception <c>newobj</c>, <c>newarr</c>, or <c>box</c> in a loop region) — the hot/repeated case.</param>
public sealed record MethodSignals(
    int Allocations,
    int Copies,
    bool Unsafe,
    int Reflection = 0,
    int Throws = 0,
    int Catches = 0,
    int Finallys = 0,
    ImmutableArray<int> EvidenceOffsets = default,
    ImmutableArray<string> ExceptionTypeNames = default,
    bool AllocInLoop = false)
{
    public static readonly MethodSignals None = new(0, 0, false);

    /// <summary>Signal-bearing IL offsets, normalized to an empty (never default) array.</summary>
    public ImmutableArray<int> Evidence => EvidenceOffsets.IsDefault ? [] : EvidenceOffsets;

    /// <summary>Constructed exception type names, normalized to an empty (never default) array.</summary>
    public ImmutableArray<string> ExceptionTypes => ExceptionTypeNames.IsDefault ? [] : ExceptionTypeNames;
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
    ImmutableArray<int> ThrowOffsets,
    int Boxes = 0,
    ImmutableArray<int> BoxOffsets = default,
    bool AllocInLoop = false,
    ImmutableArray<int> ThrowPathNewObjectOffsets = default);

public static class MethodSignalAnalysis
{
    // How many evidence IL offsets to retain per method. A compact receipt — enough
    // to point at the signal sites without dumping the whole body.
    const int MaxEvidenceOffsets = 12;

    /// <summary>
    /// Aggregates per-method signals keyed by the method's metadata token. The
    /// call-derived parts (object allocations, copies, reflection, unsafe) come from
    /// <paramref name="directCalls"/>/<paramref name="unsafeEvidence"/>; the
    /// IL-scan parts (allocation occurrences, throws, exception regions) are merged from
    /// <paramref name="allocationOccurrences"/> and <paramref name="bodySignals"/>.
    /// Pure over its inputs so it is testable without
    /// a real assembly.
    /// </summary>
    public static Dictionary<int, MethodSignals> Collect(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<int, BodySignals>? bodySignals = null,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>>? allocationOccurrences = null,
        IReadOnlyDictionary<(string Namespace, string Name), bool>? inAssemblyTypeIsException = null,
        IReadOnlySet<int>? nonHeapNewObjOperandTokens = null)
    {
        var allocations = new Dictionary<int, int>();
        var copies = new Dictionary<int, int>();
        var reflection = new Dictionary<int, int>();
        var exceptionTypes = new Dictionary<int, SortedSet<string>>();
        var evidence = new Dictionary<int, SortedSet<int>>();
        var newObjInLoop = new HashSet<int>();
        // In-loop exception `newobj` IL offsets per caller. Exception construction
        // is excluded from the hot-loop bit only when it is a throw-path
        // allocation; a retained exception built in a loop is classified below
        // (once throw offsets are known) as a real in-loop allocation.
        var exceptionNewObjInLoop = new Dictionary<int, List<int>>();

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
                bool isExceptionType = IsExceptionType(call.Callee.DeclaringType, inAssemblyTypeIsException);
                if (isExceptionType)
                {
                    if (!exceptionTypes.TryGetValue(caller, out var set))
                        exceptionTypes[caller] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(ExceptionTypeName(call.Callee.DeclaringType));

                    // Track in-loop exception construction so a retained (non-throw)
                    // exception allocation can be re-classified as hot once throw
                    // offsets are available.
                    if (call.InLoop)
                    {
                        if (!exceptionNewObjInLoop.TryGetValue(caller, out var offsets))
                            exceptionNewObjInLoop[caller] = offsets = [];
                        offsets.Add(call.ILOffset);
                    }
                }

                if (allocationOccurrences is not null)
                    continue;
                // A `newobj` of a value type (struct/enum) constructs in place and does NOT
                // allocate on the heap, so it must not count toward the allocation signal
                // (#1804). The non-heap set is classified during Build, where the metadata
                // reader is available (in-assembly value types, and cross-assembly generic
                // structs via the TypeSpec signature blob). A cross-assembly non-generic user
                // struct is unresolvable single-assembly and is intentionally NOT in the set,
                // so it remains an owned false positive at the no-referenced-assembly boundary.
                if (nonHeapNewObjOperandTokens is not null && nonHeapNewObjOperandTokens.Contains(call.OperandToken))
                    continue;
                allocations[caller] = allocations.GetValueOrDefault(caller) + 1;
                AddEvidence(caller, call.ILOffset);
                if (!isExceptionType && call.InLoop)
                {
                    // Steady-state (non-exception) object construction inside a loop is
                    // the hot/repeated allocation; error-path construction is excluded.
                    newObjInLoop.Add(caller);
                }
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
        if (allocationOccurrences is not null)
        {
            foreach (var (token, occurrences) in allocationOccurrences)
            {
                if (occurrences.Length == 0)
                    continue;
                tokens.Add(token);
                foreach (var occurrence in occurrences)
                {
                    if (!occurrence.CountsAsHeapAllocation)
                        continue;
                    allocations[token] = allocations.GetValueOrDefault(token) + 1;
                    AddEvidence(token, occurrence.ILOffset);
                    if (occurrence.InLoop && occurrence.Escape != AllocationEscape.ThrowPath)
                        newObjInLoop.Add(token);
                }
            }
        }

        var result = new Dictionary<int, MethodSignals>(tokens.Count);
        foreach (var token in tokens)
        {
            BodySignals body = default;
            bodySignals?.TryGetValue(token, out body);

            if (allocationOccurrences is null)
            {
                foreach (var offset in NormalizeOffsets(body.ArrayAllocOffsets))
                    AddEvidence(token, offset);
                foreach (var offset in NormalizeOffsets(body.BoxOffsets))
                    AddEvidence(token, offset);
            }
            foreach (var offset in NormalizeOffsets(body.ThrowOffsets))
                AddEvidence(token, offset);

            var offsets = evidence.TryGetValue(token, out var set)
                ? [.. set.Take(MaxEvidenceOffsets)]
                : ImmutableArray<int>.Empty;
            var exceptions = exceptionTypes.TryGetValue(token, out var names)
                ? names.ToImmutableArray()
                : ImmutableArray<string>.Empty;

            // An in-loop exception allocation is hot unless the body scan proves
            // the `newobj` flows straight to a `throw` (the steady-state vs
            // error-path distinction). Any unproven consumer means the exception
            // may be retained.
            bool retainedExceptionInLoop = false;
            if (exceptionNewObjInLoop.TryGetValue(token, out var exceptionLoopOffsets))
            {
                var throwPathNewObjectOffsets = body.ThrowPathNewObjectOffsets.IsDefault
                    ? null
                    : new HashSet<int>(body.ThrowPathNewObjectOffsets);
                foreach (var offset in exceptionLoopOffsets)
                {
                    if (throwPathNewObjectOffsets is null || !throwPathNewObjectOffsets.Contains(offset))
                    {
                        retainedExceptionInLoop = true;
                        break;
                    }
                }
            }

            result[token] = new MethodSignals(
                allocations.GetValueOrDefault(token) + (allocationOccurrences is null ? body.Newarr + body.Boxes : 0),
                copies.GetValueOrDefault(token),
                unsafeMethods.Contains(token),
                reflection.GetValueOrDefault(token),
                body.Throws,
                body.Catches,
                body.Finallys,
                offsets,
                exceptions,
                newObjInLoop.Contains(token) || body.AllocInLoop || retainedExceptionInLoop);
        }
        return result;
    }

    static ImmutableArray<int> NormalizeOffsets(ImmutableArray<int> offsets)
        => offsets.IsDefault ? [] : offsets;

    // Well-known copy/materialize APIs. Exact framework identities whose dominant
    // effect is producing a copy of existing data. Intentionally small; grow
    // deliberately to keep false positives low.
    static bool IsCopyApi(MemberRef callee)
    {
        if (callee.Kind == MemberKind.Unsupported)
            return false;

        if (callee.Name is "ToArray" or "ToList"
            && IsFrameworkType(callee.DeclaringType, "System.Linq", "System.Linq", "Enumerable"))
            return true;

        if (callee.Name == "ToArray"
            && (IsSpanLike(callee.DeclaringType)
                || IsFrameworkType(callee.DeclaringType, "System.Collections", "System.Collections.Generic", "List`1")))
            return true;

        if (callee.Name == "CopyTo"
            && (IsSpanLike(callee.DeclaringType)
                || IsFrameworkType(callee.DeclaringType, TypeRef.CoreLibrary, "System", "Array")))
            return true;

        if (callee.Name == "GetSubArray"
            && IsCoreLibraryType(callee.DeclaringType, "System.Runtime.CompilerServices", "RuntimeHelpers"))
            return true;

        if (callee.Name == "Substring"
            && IsCoreLibraryType(callee.DeclaringType, "System", "String"))
            return true;

        if (callee.Name is "Concat" or "Join"
            && IsCoreLibraryType(callee.DeclaringType, "System", "String"))
            return true;

        return false;
    }

    static bool IsSpanLike(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return IsFrameworkType(definition, TypeRef.CoreLibrary, "System", "Span`1")
            || IsFrameworkType(definition, TypeRef.CoreLibrary, "System", "ReadOnlySpan`1");
    }

    static bool IsCoreLibraryType(TypeRef? type, string ns, string name)
        => IsFrameworkType(type, TypeRef.CoreLibrary, ns, name);

    static bool IsFrameworkType(TypeRef? type, string assembly, string ns, string name)
    {
        var definition = type is { Kind: TypeRefKind.GenericInstance } ? type.ElementType : type;
        return definition is { Kind: TypeRefKind.Definition, Assembly: var typeAssembly, Namespace: var typeNamespace, Name: var typeName }
            && definition.TrustedFrameworkAssembly
            && FrameworkIdentity.MatchesFrameworkAssembly(typeAssembly, assembly)
            && typeNamespace == ns
            && typeName == name;
    }

    // The display name recorded for a constructed exception. A constructed generic
    // exception is a GenericInstance whose own Name is empty, so use the underlying
    // definition's name (e.g. "MyException`1") rather than emitting a blank entry.
    static string ExceptionTypeName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element ? element.Name : type.Name;

    // A constructed type is treated as an exception when it actually derives from
    // System.Exception. For types defined in the inspected assembly the base chain
    // is resolved authoritatively (see LibraryBodyIndex's in-assembly exception map),
    // so a non-exception `*Exception` lookalike is not counted. For external types,
    // whose base chains cannot be walked without loading other assemblies, fall back
    // to the conservative simple-name suffix (a `corelib` exception almost always
    // ends with "Exception"). The map is keyed by (namespace, name); a null map (the
    // pure-unit-test path with no metadata) keeps the legacy suffix behavior.
    static bool IsExceptionType(TypeRef type, IReadOnlyDictionary<(string Namespace, string Name), bool>? inAssemblyTypeIsException)
    {
        if (type.Kind == TypeRefKind.Unsupported)
            return false;
        // A constructed generic exception (rare) carries its definition in ElementType;
        // classify by that so the (namespace, name) key matches the type definition.
        var definition = type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element ? element : type;
        if (inAssemblyTypeIsException is not null
            && inAssemblyTypeIsException.TryGetValue((definition.Namespace, definition.Name), out bool derivesFromException))
            return derivesFromException;
        return definition.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

    // Reflection-style APIs, identified by framework declaring-type identity (and, for
    // System.Type, a curated member set). These are runtime metadata / dynamic-invocation
    // surfaces (System.Reflection), dynamic construction (System.Activator), expression
    // trees (System.Linq.Expressions), and the reflective members on System.Type — all
    // signals that a method does dynamic work.
    static bool IsReflectionApi(MemberRef callee)
    {
        if (callee.Kind == MemberKind.Unsupported)
            return false;
        if (FrameworkIdentity.IsKnownFrameworkNamespacePrefix(callee.DeclaringType, "System.Reflection", "System.Reflection")
            || FrameworkIdentity.IsKnownFrameworkNamespace(callee.DeclaringType, "System.Linq.Expressions", "System.Linq.Expressions"))
            return true;
        if (!FrameworkIdentity.IsCoreLibraryType(callee.DeclaringType, "System", "Activator")
            && !FrameworkIdentity.IsCoreLibraryType(callee.DeclaringType, "System", "Type"))
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
