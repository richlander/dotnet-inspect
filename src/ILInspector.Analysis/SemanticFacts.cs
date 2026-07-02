using System.Collections.Immutable;

namespace ILInspector.Analysis;

public sealed record AllocationFact(
    MethodIdentity Method,
    int ILOffset,
    string AllocationKind,
    string? AllocatedType,
    bool CountedAsHeap,
    string Frequency,
    bool InLoop,
    string Escape,
    int? EstimatedSizeBytes,
    string? SizeTier,
    string Evidence);

public sealed record SafetyFact(
    MethodIdentity Method,
    int? ILOffset,
    string SafetyKind,
    string Operation,
    string Requirement,
    string Evidence);

public sealed record CostFact(
    MethodIdentity Method,
    int ILOffset,
    string CostKind,
    string Operation,
    bool InLoop,
    string Evidence);

public static class SemanticFactProjection
{
    public static ImmutableArray<AllocationFact> AllocationFacts(
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> occurrences,
        int methodToken,
        int? ilOffset = null)
        => occurrences.TryGetValue(methodToken, out var methodOccurrences)
            ? ProjectAllocationFacts(methodOccurrences, ilOffset)
            : [];

    public static ImmutableArray<AllocationFact> AllocationFacts(
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> occurrences,
        Func<MethodIdentity, bool>? methodScope = null,
        int? ilOffset = null)
        => ProjectAllocationFacts(
            occurrences.Values.SelectMany(group => group)
            .Where(occurrence => methodScope is null || methodScope(occurrence.Method))
            .ToArray(),
            ilOffset);

    static ImmutableArray<AllocationFact> ProjectAllocationFacts(
        IEnumerable<AllocationOccurrence> occurrences,
        int? ilOffset)
        => [.. occurrences
            .Where(occurrence => ilOffset is null || occurrence.ILOffset == ilOffset)
            .OrderBy(occurrence => occurrence.Method.MetadataToken)
            .ThenBy(occurrence => occurrence.ILOffset)
            .Select(occurrence => new AllocationFact(
                occurrence.Method,
                occurrence.ILOffset,
                FormatAllocationKind(occurrence.Kind),
                occurrence.AllocatedType?.ToQualifiedDisplayString(),
                occurrence.CountsAsHeapAllocation,
                FormatFrequency(occurrence.Frequency),
                occurrence.InLoop,
                FormatEscape(occurrence.Escape),
                occurrence.EstimatedSizeBytes,
                occurrence.SizeTier == AllocationSizeTier.Unknown ? null : occurrence.SizeTier.ToString(),
                occurrence.Detail ?? FormatSource(occurrence.Source)))];

    public static ImmutableArray<SafetyFact> SafetyFacts(
        IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>> unsafeEvidence,
        IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> occurrences,
        int methodToken,
        int? ilOffset = null)
        => SafetyFacts(
            unsafeEvidence.TryGetValue(methodToken, out var methodEvidence) ? methodEvidence : [],
            occurrences.TryGetValue(methodToken, out var methodOccurrences) ? methodOccurrences : [],
            ilOffset);

    public static ImmutableArray<SafetyFact> SafetyFacts(
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> occurrences,
        Func<MethodIdentity, bool>? methodScope = null,
        int? ilOffset = null)
        => SafetyFacts(
            unsafeEvidence.Where(evidence => methodScope is null || methodScope(evidence.Member)),
            occurrences.Values.SelectMany(group => group)
                .Where(occurrence => methodScope is null || methodScope(occurrence.Method)),
            ilOffset);

    static ImmutableArray<SafetyFact> SafetyFacts(
        IEnumerable<UnsafeEvidence> unsafeEvidence,
        IEnumerable<UnsafetyOccurrence> occurrences,
        int? ilOffset)
    {
        var operationRows = occurrences
            .Where(occurrence => ilOffset is null || occurrence.ILOffset == ilOffset)
            .Select(occurrence => new SafetyFact(
                occurrence.Method,
                occurrence.ILOffset,
                FormatUnsafetyKind(occurrence.Kind),
                occurrence.Detail ?? FormatUnsafetyKind(occurrence.Kind),
                "requires unsafe",
                FormatUnsafetyKind(occurrence.Kind)));

        var coveredOperations = operationRows
            .Where(row => row.ILOffset is not null)
            .Select(row => (row.Method.MetadataToken, row.ILOffset))
            .ToHashSet();

        var unsafeCallRows = unsafeEvidence
            .Where(evidence => ilOffset is null || evidence.ILOffset == ilOffset)
            .Where(evidence => evidence.ILOffset is null
                || !coveredOperations.Contains((evidence.Member.MetadataToken, evidence.ILOffset.Value)))
            .Select(evidence => new SafetyFact(
                evidence.Member,
                evidence.ILOffset,
                evidence.Reason,
                evidence.Detail,
                "requires unsafe",
                evidence.Kind));

        return [.. operationRows.Concat(unsafeCallRows)
            .OrderBy(row => row.Method.MetadataToken)
            .ThenBy(row => row.ILOffset)
            .ThenBy(row => row.SafetyKind, StringComparer.Ordinal)];
    }

    public static ImmutableArray<CostFact> CostFacts(
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>> directCalls,
        int methodToken,
        int? ilOffset = null)
        => ProjectCostFacts(
            directCalls.TryGetValue(methodToken, out var methodCalls) ? methodCalls : [],
            ilOffset);

    public static ImmutableArray<CostFact> CostFacts(
        ImmutableArray<DirectCall> directCalls,
        Func<MethodIdentity, bool>? methodScope = null,
        int? ilOffset = null)
        => ProjectCostFacts(
            directCalls
            .Where(call => methodScope is null || methodScope(call.Caller))
            .ToArray(),
            ilOffset);

    static ImmutableArray<CostFact> ProjectCostFacts(
        IEnumerable<DirectCall> directCalls,
        int? ilOffset)
        => [.. directCalls
            .Where(call => ilOffset is null || call.ILOffset == ilOffset)
            .Select(ToCostFact)
            .Where(fact => fact is not null)
            .Select(fact => fact!)
            .OrderBy(fact => fact.Method.MetadataToken)
            .ThenBy(fact => fact.ILOffset)
            .ThenBy(fact => fact.CostKind, StringComparer.Ordinal)];

    static CostFact? ToCostFact(DirectCall call)
    {
        var kind = call.Kind switch
        {
            CallKind.CallVirtual => "virtual dispatch",
            CallKind.LoadFunction or CallKind.LoadVirtualFunction => "delegate/function pointer",
            CallKind.CallIndirect => "function pointer call",
            _ => null
        };
        if (kind is null)
            return null;

        return new CostFact(
            call.Caller,
            call.ILOffset,
            kind,
            FormatMember(call.Callee),
            call.InLoop,
            string.IsNullOrEmpty(call.Opcode) ? FormatCallKind(call.Kind) : call.Opcode);
    }

    static string FormatAllocationKind(AllocationKind kind) => kind.ToString().ToLowerInvariant();
    static string FormatFrequency(AllocationFrequency frequency) => frequency switch
    {
        AllocationFrequency.CachedOnce => "cached once",
        AllocationFrequency.PerIteration => "per iteration",
        _ => "always"
    };
    static string FormatEscape(AllocationEscape escape) => escape switch
    {
        AllocationEscape.LocalOnly => "local-only",
        AllocationEscape.ThrowPath => "throw path",
        AllocationEscape.Escapes => "escapes",
        _ => "unknown"
    };
    static string FormatSource(AllocationFactSource source) => source switch
    {
        AllocationFactSource.Newarr => "newarr",
        AllocationFactSource.Box => "box",
        AllocationFactSource.GetEnumeratorCall => "GetEnumerator",
        _ => "newobj"
    };
    static string FormatUnsafetyKind(UnsafetyKind kind) => kind switch
    {
        UnsafetyKind.CallIndirect => "calli",
        UnsafetyKind.StackAlloc => "stackalloc",
        _ => "dereference"
    };
    static string FormatCallKind(CallKind kind) => kind switch
    {
        CallKind.CallVirtual => "callvirt",
        CallKind.NewObject => "newobj",
        CallKind.LoadFunction => "ldftn",
        CallKind.LoadVirtualFunction => "ldvirtftn",
        CallKind.CallIndirect => "calli",
        _ => "call"
    };
    static string FormatMember(MemberRef member)
    {
        if (member.Kind == MemberKind.Unsupported)
            return member.DeclaringType.ToDisplayString();
        var typeArgs = member.TypeArguments.Length == 0
            ? ""
            : $"<{string.Join(", ", member.TypeArguments.Select(t => t.ToQualifiedDisplayString()))}>";
        return $"{member.DeclaringType.ToQualifiedDisplayString()}.{member.Name}{typeArgs}({string.Join(", ", member.ParameterTypes.Select(p => p.ToQualifiedDisplayString()))})";
    }
}
