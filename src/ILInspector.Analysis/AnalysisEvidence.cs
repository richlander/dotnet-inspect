using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using ILInspector.Evidence;

namespace ILInspector.Analysis;

/// <summary>
/// Adapts Analysis' single-version audit facts onto the shared evidence row envelope.
/// Analysis is mostly a fact producer: each finding is emitted as a
/// <see cref="EvidenceRowKind.Present"/> row, with no cross-version matcher involved.
/// </summary>
public static class AnalysisEvidence
{
    public static readonly EvidenceDescriptor ResourceLeakDescriptor = new("resource.leak", "Resource leak");

    public static AnalysisEvidenceResult DescribeAssembly(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return Describe(LibraryBodyIndex.Open(path));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return AnalysisEvidenceResult.Failed(ex.Message);
        }
    }

    public static AnalysisEvidenceResult Describe(LibraryBodyIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return DescribeCore(index, methodScope: null);
    }

    public static AnalysisEvidenceResult DescribeMethod(LibraryBodyIndex index, int methodToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        return DescribeCore(index, method => method.MetadataToken == methodToken);
    }

    static AnalysisEvidenceResult DescribeCore(LibraryBodyIndex index, Func<MethodIdentity, bool>? methodScope)
    {
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        var warnings = ImmutableArray.CreateBuilder<string>();

        AddRowsFailClosed(warnings, "leak triage", () => AddLeakTriageRows(rows, index, methodScope));
        AddRowsFailClosed(warnings, "allocation occurrences", () => AddAllocationRows(rows, index, methodScope));
        AddRowsFailClosed(warnings, "method signals", () => AddMethodSignalRows(rows, index, methodScope));
        AddRowsFailClosed(warnings, "semantic facts", () => AddSemanticFactRows(rows, index, methodScope));
        AddRowsFailClosed(warnings, "optimization opportunities", () => AddOptimizationOpportunityRows(rows, index, methodScope));

        return new AnalysisEvidenceResult(AssignAuditIndices(rows), warnings.ToImmutable(), Failure: null);
    }

    static void AddRowsFailClosed(ImmutableArray<string>.Builder warnings, string name, Action addRows)
    {
        try
        {
            addRows();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            warnings.Add($"{name} evidence suppressed: {ex.Message}");
        }
    }

    static void AddLeakTriageRows(
        ImmutableArray<EvidenceRow>.Builder rows,
        LibraryBodyIndex index,
        Func<MethodIdentity, bool>? methodScope)
    {
        if (string.IsNullOrEmpty(index.Path))
            return;

        var result = LeakTriageAnalyzer.AnalyzeAssemblyDetailed(index.Path);
        foreach (var finding in result.Findings
            .Where(finding => InScope(finding.Method, methodScope))
            .OrderBy(finding => finding.Method.MetadataToken)
            .ThenBy(finding => finding.RentOffset)
            .ThenBy(finding => finding.ILOffset ?? -1)
            .ThenBy(finding => finding.Shape, StringComparer.Ordinal))
        {
            var subject = Subject(finding.Method);
            string identity = FindingKey(subject.Key, ResourceLeakDescriptor.Id, finding.Shape, finding.RentOffset, finding.ILOffset);
            rows.Add(new EvidenceRow(
                subject,
                ResourceLeakDescriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(finding.ILOffset ?? finding.RentOffset)),
                Detail: $"{finding.Shape}: {finding.Evidence} Severity: {finding.Severity}.",
                Payload: finding));
        }
    }

    static void AddAllocationRows(
        ImmutableArray<EvidenceRow>.Builder rows,
        LibraryBodyIndex index,
        Func<MethodIdentity, bool>? methodScope)
    {
        foreach (var occurrence in index.GetAllocationOccurrences()
            .Values
            .SelectMany(static occurrences => occurrences)
            .Where(occurrence => InScope(occurrence.Method, methodScope))
            .OrderBy(occurrence => occurrence.Method.MetadataToken)
            .ThenBy(occurrence => occurrence.ILOffset)
            .ThenBy(occurrence => occurrence.AnnotationId, StringComparer.Ordinal))
        {
            var descriptor = AllocationDescriptor(occurrence);
            var subject = Subject(occurrence.Method);
            string identity = FindingKey(subject.Key, descriptor.Id, occurrence.Kind.ToString(), occurrence.ILOffset, occurrence.OperandToken);
            rows.Add(new EvidenceRow(
                subject,
                descriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(occurrence.ILOffset)),
                Detail: AllocationDetail(occurrence),
                Payload: occurrence));
        }
    }

    static void AddMethodSignalRows(
        ImmutableArray<EvidenceRow>.Builder rows,
        LibraryBodyIndex index,
        Func<MethodIdentity, bool>? methodScope)
    {
        var methodsByToken = index.Methods.ToDictionary(method => method.MetadataToken);
        foreach (var (token, signals) in index.GetMethodSignals().OrderBy(pair => pair.Key))
        {
            if (!methodsByToken.TryGetValue(token, out var method) || !InScope(method, methodScope))
                continue;

            if (signals.Allocations > 0)
                AddSignalRow(rows, method, "signal.alloc", "Allocation signal", signals.Allocations, "method allocates", signals);
            if (signals.AllocInLoop)
                AddSignalRow(rows, method, "signal.alloc-in-loop", "Allocation-in-loop signal", 1, "method allocates inside a loop", signals);
            if (signals.Copies > 0)
                AddSignalRow(rows, method, "signal.copy", "Copy/materialization signal", signals.Copies, "method copies or materializes data", signals);
            if (signals.Unsafe)
                AddSignalRow(rows, method, "signal.unsafe", "Unsafe signal", 1, "method has unsafe evidence", signals);
            if (signals.Reflection > 0)
                AddSignalRow(rows, method, "signal.reflection", "Reflection signal", signals.Reflection, "method calls reflection-style APIs", signals);
            if (signals.Throws > 0)
                AddSignalRow(rows, method, "signal.throw", "Throw signal", signals.Throws, "method throws", signals);
            if (signals.Catches > 0)
                AddSignalRow(rows, method, "signal.catch", "Catch signal", signals.Catches, "method catches exceptions", signals);
            if (signals.Finallys > 0)
                AddSignalRow(rows, method, "signal.finally", "Finally signal", signals.Finallys, "method has finally/fault cleanup", signals);
        }
    }

    static void AddSignalRow(
        ImmutableArray<EvidenceRow>.Builder rows,
        MethodIdentity method,
        string descriptorId,
        string title,
        int count,
        string detail,
        MethodSignals signals)
    {
        var descriptor = new EvidenceDescriptor(descriptorId, title);
        var subject = Subject(method);
        string identity = FindingKey(subject.Key, descriptor.Id);
        rows.Add(new EvidenceRow(
            subject,
            descriptor,
            EvidenceRowKind.Present,
            new EvidenceAnchor(identity),
            Detail: $"{detail}; count={count}; evidence={FormatOffsets(signals.Evidence)}",
            Payload: new AnalysisSignalFact(method, descriptor.Id, count, signals.Evidence, signals)));
    }

    static void AddSemanticFactRows(
        ImmutableArray<EvidenceRow>.Builder rows,
        LibraryBodyIndex index,
        Func<MethodIdentity, bool>? methodScope)
    {
        foreach (var fact in SemanticFactProjection.AllocationFacts(index.GetAllocationOccurrences(), methodScope)
            .OrderBy(fact => fact.Method.MetadataToken)
            .ThenBy(fact => fact.ILOffset)
            .ThenBy(fact => fact.AllocationKind, StringComparer.Ordinal))
        {
            var descriptor = new EvidenceDescriptor("semantic.allocation", "Allocation semantic fact");
            var subject = Subject(fact.Method);
            string identity = FindingKey(subject.Key, descriptor.Id, fact.AllocationKind, fact.ILOffset, fact.Evidence);
            rows.Add(new EvidenceRow(
                subject,
                descriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(fact.ILOffset)),
                Detail: $"{fact.AllocationKind} allocation; heap={fact.CountedAsHeap}; inLoop={fact.InLoop}; escape={fact.Escape}; evidence={fact.Evidence}",
                Payload: fact));
        }

        var unsafeEvidence = index.GetUnsafeEvidenceByMember().Values.SelectMany(group => group).ToImmutableArray();
        foreach (var fact in SemanticFactProjection.SafetyFacts(unsafeEvidence, index.GetUnsafetyOccurrences(), methodScope)
            .OrderBy(fact => fact.Method.MetadataToken)
            .ThenBy(fact => fact.ILOffset ?? -1)
            .ThenBy(fact => fact.SafetyKind, StringComparer.Ordinal)
            .ThenBy(fact => fact.Operation, StringComparer.Ordinal))
        {
            var descriptor = new EvidenceDescriptor("semantic.safety", "Safety semantic fact");
            var subject = Subject(fact.Method);
            string identity = FindingKey(subject.Key, descriptor.Id, fact.SafetyKind, fact.Operation, fact.ILOffset, fact.Evidence);
            rows.Add(new EvidenceRow(
                subject,
                descriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(fact.ILOffset)),
                Detail: $"{fact.SafetyKind}: {fact.Operation}; {fact.Requirement}; evidence={fact.Evidence}",
                Payload: fact));
        }

        foreach (var fact in SemanticFactProjection.CostFacts(index.DirectCalls, methodScope)
            .OrderBy(fact => fact.Method.MetadataToken)
            .ThenBy(fact => fact.ILOffset)
            .ThenBy(fact => fact.CostKind, StringComparer.Ordinal)
            .ThenBy(fact => fact.Operation, StringComparer.Ordinal))
        {
            var descriptor = new EvidenceDescriptor("semantic.cost", "Cost semantic fact");
            var subject = Subject(fact.Method);
            string identity = FindingKey(subject.Key, descriptor.Id, fact.CostKind, fact.Operation, fact.ILOffset, fact.Evidence);
            rows.Add(new EvidenceRow(
                subject,
                descriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(fact.ILOffset)),
                Detail: $"{fact.CostKind}: {fact.Operation}; inLoop={fact.InLoop}; evidence={fact.Evidence}",
                Payload: fact));
        }
    }

    static void AddOptimizationOpportunityRows(
        ImmutableArray<EvidenceRow>.Builder rows,
        LibraryBodyIndex index,
        Func<MethodIdentity, bool>? methodScope)
    {
        foreach (var opportunity in index.OptimizationOpportunities
            .Where(opportunity => InScope(opportunity.Method, methodScope))
            .OrderBy(opportunity => opportunity.Method.MetadataToken)
            .ThenBy(opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(opportunity => opportunity.Shape, StringComparer.Ordinal))
        {
            var descriptor = new EvidenceDescriptor($"optimization.{Slug(opportunity.Shape)}", "Optimization opportunity");
            var subject = Subject(opportunity.Method);
            string identity = FindingKey(subject.Key, descriptor.Id, opportunity.ILOffset, opportunity.Evidence);
            rows.Add(new EvidenceRow(
                subject,
                descriptor,
                EvidenceRowKind.Present,
                new EvidenceAnchor(identity, ScopeKey: OffsetScope(opportunity.ILOffset)),
                Detail: $"{opportunity.Shape}: {opportunity.Evidence} Fix: {opportunity.SafeFixDirection}. Confidence: {opportunity.Confidence}.",
                Payload: opportunity));
        }
    }

    static ImmutableArray<EvidenceRow> AssignAuditIndices(ImmutableArray<EvidenceRow>.Builder rows)
    {
        var result = ImmutableArray.CreateBuilder<EvidenceRow>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            result.Add(row with { Anchor = row.Anchor with { NewIndex = i } });
        }

        return result.MoveToImmutable();
    }

    static EvidenceDescriptor AllocationDescriptor(AllocationOccurrence occurrence)
    {
        if (occurrence.InLoop && occurrence.CountsAsHeapAllocation)
            return new EvidenceDescriptor("alloc.in-loop", "Allocation in loop");

        return occurrence.AnnotationId switch
        {
            "alloc.box" => new EvidenceDescriptor("alloc.box", "Box allocation"),
            "alloc.array" => new EvidenceDescriptor("alloc.array", "Array allocation"),
            "alloc.closure" => new EvidenceDescriptor("alloc.closure", "Closure allocation"),
            "alloc.statemachine" => new EvidenceDescriptor("alloc.statemachine", "State-machine allocation"),
            "alloc.delegate" => new EvidenceDescriptor("alloc.delegate", "Delegate allocation"),
            "alloc.enumerator" => new EvidenceDescriptor("alloc.enumerator", "Enumerator allocation"),
            _ => new EvidenceDescriptor("alloc.new", "Object allocation"),
        };
    }

    static string AllocationDetail(AllocationOccurrence occurrence)
    {
        var type = occurrence.AllocatedType?.ToQualifiedDisplayString() ?? occurrence.RuntimeAllocationType ?? "unknown type";
        var loop = occurrence.InLoop ? "in loop" : "not in loop";
        var heap = occurrence.CountsAsHeapAllocation ? "heap" : "non-heap";
        return $"{occurrence.Kind} allocation at IL_{occurrence.ILOffset:X4} ({type}); {heap}; {loop}; escape={occurrence.Escape}; multiplicity={occurrence.Multiplicity}.";
    }

    static EvidenceSubject Subject(MethodIdentity method) => new(MethodKey(method), MethodDisplay(method));

    public static string MethodKey(MethodIdentity method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return string.Join("|",
            method.AssemblyName,
            GenericMemberIdentity.KeyFragment(method.DeclaringType),
            method.Name,
            method.GenericArity.ToString(CultureInfo.InvariantCulture),
            method.IsExtension ? "extension" : "member",
            string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment)),
            GenericMemberIdentity.KeyFragment(method.ReturnType));
    }

    static string MethodDisplay(MethodIdentity method)
    {
        var parameters = string.Join(", ", method.ParameterTypes.Select(parameter => parameter.ToQualifiedDisplayString()));
        return $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({parameters})";
    }

    static string FindingKey(string subjectKey, string descriptorId, params object?[] parts)
    {
        var builder = new StringBuilder(subjectKey.Length + descriptorId.Length + 32);
        builder.Append(subjectKey).Append('|').Append(descriptorId);
        foreach (var part in parts)
            builder.Append('|').Append(part switch
            {
                null => "<null>",
                int value => value.ToString(CultureInfo.InvariantCulture),
                _ => Convert.ToString(part, CultureInfo.InvariantCulture),
            });
        return builder.ToString();
    }

    static bool InScope(MethodIdentity method, Func<MethodIdentity, bool>? methodScope)
        => methodScope is null || methodScope(method);

    static string? OffsetScope(int? ilOffset)
        => ilOffset is null ? null : $"IL_{ilOffset.Value:X4}";

    static string FormatOffsets(ImmutableArray<int> offsets)
        => offsets.IsDefaultOrEmpty
            ? "none"
            : string.Join(",", offsets.Select(offset => $"IL_{offset:X4}"));

    static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool lastWasSeparator = false;
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    static bool IsRecoverable(Exception ex)
        => ex is IOException
            or BadImageFormatException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException;
}

public sealed record AnalysisSignalFact(
    MethodIdentity Method,
    string Signal,
    int Count,
    ImmutableArray<int> EvidenceOffsets,
    MethodSignals Signals);

public sealed record AnalysisEvidenceResult(
    ImmutableArray<EvidenceRow> Rows,
    ImmutableArray<string> Warnings,
    string? Failure)
{
    public bool Succeeded => Failure is null;

    public static AnalysisEvidenceResult Failed(string failure)
        => new([], [], failure);
}
