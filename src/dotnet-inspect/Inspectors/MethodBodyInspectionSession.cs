using System.Collections.Immutable;
using System.IO;
using System.Linq;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Method-body inspection composition layer (see <c>docs/design/method-body-inspection.md</c>):
/// opens a <see cref="Analysis.LibraryBodyIndex"/> once and produces method- or coordinate-scoped
/// semantic facts, so the <c>member</c> and <c>library --il-offset</c> paths converge on one
/// producer instead of each re-opening the index and re-projecting facts.
///
/// This sits above <c>ILInspector.Analysis</c> (and, in later slices, Metadata / Decompiler /
/// Research); it is deliberately not part of the lower-level <c>DotnetInspector.Services</c>
/// package layer. Early slices open from a <c>dllPath</c>; the target is to consume an
/// <see cref="AssemblyInspectionSession"/> / <see cref="ResolvedAssemblyReference"/> once the
/// shared-PE-owner composition lands.
/// </summary>
public sealed class MethodBodyInspectionSession
{
    readonly Analysis.LibraryBodyIndex _index;

    MethodBodyInspectionSession(Analysis.LibraryBodyIndex index, string sourceName)
    {
        _index = index;
        SourceName = sourceName;
    }

    /// <summary>
    /// Short assembly name (file name without extension) this session was opened from. Used to
    /// attribute inbound caller edges to their originating assembly.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Test-only counter of index builds (one per <see cref="Open"/>). The "build the index once
    /// per command" invariant (#2139 perf: PRs #2187/#2199/#2210) is guarded by asserting this
    /// stays at 1 across a multi-section render; a new section that opens its own session would
    /// silently reintroduce a per-section rebuild.
    /// </summary>
    internal static int OpenCountForTests;

    /// <summary>
    /// Opens a session over an assembly's analysis body index. <paramref name="includeAllocations"/>
    /// and <paramref name="includeOpportunities"/> gate the two expensive whole-assembly analysis
    /// phases (escape-classified allocation occurrences and optimization opportunities); leave them
    /// on unless the caller knows no requested section consumes them (see
    /// <c>ApiOutputFormatter.AnalysisScopeFor</c>).
    /// </summary>
    public static MethodBodyInspectionSession Open(string assemblyPath, IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true, bool includeOpportunities = true)
    {
        System.Threading.Interlocked.Increment(ref OpenCountForTests);
        return new(Analysis.LibraryBodyIndex.Open(assemblyPath, resolver, includeAllocations, includeOpportunities), Path.GetFileNameWithoutExtension(assemblyPath));
    }

    /// <summary>
    /// Allocation facts for one method (<paramref name="methodToken"/>), optionally narrowed to a
    /// single IL coordinate (<paramref name="ilOffset"/>).
    /// </summary>
    public ImmutableArray<Analysis.AllocationFact> AllocationFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.AllocationFacts(_index.GetAllocationOccurrences(), methodToken, ilOffset);

    /// <summary>Safety facts for one method, optionally narrowed to a single IL coordinate.</summary>
    public ImmutableArray<Analysis.SafetyFact> SafetyFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.SafetyFacts(
            _index.GetUnsafeEvidenceByMember(), _index.GetUnsafetyOccurrences(), methodToken, ilOffset);

    /// <summary>Cost facts for one method, optionally narrowed to a single IL coordinate.</summary>
    public ImmutableArray<Analysis.CostFact> CostFacts(int methodToken, int? ilOffset = null)
        => Analysis.SemanticFactProjection.CostFacts(_index.GetDirectCallsByCaller(), methodToken, ilOffset);

    /// <summary>
    /// Direct call sites originating in one method (<paramref name="methodToken"/>). Empty when the
    /// method has no recorded calls. Grouped by caller token, equivalent to filtering the flat
    /// <c>DirectCalls</c> stream on <c>Caller.MetadataToken == methodToken</c>.
    /// </summary>
    public ImmutableArray<Analysis.DirectCall> DirectCalls(int methodToken)
        => _index.GetDirectCallsByCaller().TryGetValue(methodToken, out var calls)
            ? calls
            : ImmutableArray<Analysis.DirectCall>.Empty;

    /// <summary>
    /// Unsafe-operation evidence recorded for one method (<paramref name="methodToken"/>). Empty when
    /// the method has no evidence. Grouped by member token, equivalent to filtering the flat
    /// <c>UnsafeEvidence</c> stream on <c>Member.MetadataToken == methodToken</c>.
    /// </summary>
    public ImmutableArray<Analysis.UnsafeEvidence> UnsafeEvidence(int methodToken)
        => _index.GetUnsafeEvidenceByMember().TryGetValue(methodToken, out var evidence)
            ? evidence
            : ImmutableArray<Analysis.UnsafeEvidence>.Empty;

    /// <summary>Outbound call graph rooted at one method.</summary>
    public Analysis.CallTreeNode CallTree(int methodToken)
        => _index.BuildCallTree(methodToken);

    /// <summary>
    /// Inbound caller graph rooted at one method. When <paramref name="scopeIndexes"/> is
    /// non-empty the reverse graph is extended across those caller-scope assemblies.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(int methodToken, IReadOnlyList<Analysis.LibraryBodyIndex>? scopeIndexes = null)
        => scopeIndexes is { Count: > 0 }
            ? _index.BuildCallerTree(methodToken, scopeIndexes)
            : _index.BuildCallerTree(methodToken);

    /// <summary>
    /// Inbound caller graph rooted at one method, extended across sibling caller-scope
    /// <paramref name="scopes"/> (opened as their own sessions). Empty scopes fall back to the
    /// same-assembly-only reverse graph.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(int methodToken, IReadOnlyList<MethodBodyInspectionSession> scopes)
        => scopes is { Count: > 0 }
            ? _index.BuildCallerTree(methodToken, scopes.Select(s => s._index).ToArray())
            : _index.BuildCallerTree(methodToken);

    /// <summary>
    /// Inbound call edges targeting one method (<paramref name="targetToken"/>), each tagged with the
    /// <see cref="SourceName"/> of the assembly it originates in.
    ///
    /// Same-assembly edges match either the exact callee-definition token or a structural pattern
    /// built from the target's identity (the pattern adds MemberRef-form references, including
    /// abstract/interface members that have no body). When <paramref name="scopes"/> is non-empty and
    /// a pattern is available, each scope session is scanned for cross-assembly callers using
    /// generic-normalized matching (operand tokens are assembly-local, so only the pattern applies).
    /// Results are unsorted and undeduplicated; the caller owns presentation ordering.
    /// </summary>
    public ImmutableArray<CallerEdge> CallerEdges(
        int targetToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes = null)
    {
        var selected = _index.Methods.FirstOrDefault(m => m.MetadataToken == targetToken);
        var pattern = selected is { } identity
            ? Analysis.MemberPattern.Method(identity.DeclaringType, identity.Name, identity.ParameterTypes)
            : null;

        var edges = ImmutableArray.CreateBuilder<CallerEdge>();

        foreach (var call in _index.DirectCalls)
        {
            if (call.CalleeDefinitionToken == targetToken || (pattern is not null && pattern.Matches(call.Callee)))
                edges.Add(new CallerEdge(SourceName, call));
        }

        if (pattern is not null && scopes is { Count: > 0 })
        {
            foreach (var scope in scopes)
            {
                foreach (var call in scope._index.DirectCalls)
                {
                    if (pattern.MatchesCrossAssembly(call.Callee))
                        edges.Add(new CallerEdge(scope.SourceName, call));
                }
            }
        }

        return edges.ToImmutable();
    }

    // --- Type/collection-scoped producer surface (the type/library analysis sections) ---

    /// <summary>All unsafe-operation evidence in the assembly (unfiltered); the caller narrows by type.</summary>
    public ImmutableArray<Analysis.UnsafeEvidence> UnsafeEvidence()
        => _index.UnsafeEvidence;

    /// <summary>
    /// Called-type summaries aggregated over calls originating in methods matching
    /// <paramref name="callerScope"/>.
    /// </summary>
    public ImmutableArray<Analysis.CalledTypeSummary> CalledTypes(Func<Analysis.MethodIdentity, bool> callerScope)
        => _index.CalledTypes(callerScope);

    /// <summary>All performance-triage optimization opportunities in the assembly (unfiltered).</summary>
    public ImmutableArray<Analysis.OptimizationOpportunity> OptimizationOpportunities
        => _index.OptimizationOpportunities;

    /// <summary>Qualified names of types recognized as generated framework/protobuf/gRPC implementation detail.</summary>
    public IReadOnlySet<string> GeneratedFrameworkTypeNames
        => _index.GeneratedFrameworkTypeNames;

    /// <summary>Leverage ranking (highest first) of up to <paramref name="count"/> methods matching <paramref name="scope"/>.</summary>
    public ImmutableArray<Analysis.MethodLeverage> TopLeverage(int count, Func<Analysis.MethodIdentity, bool> scope)
        => _index.TopLeverage(count, scope);

    /// <summary>Leverage ranking (highest first) of up to <paramref name="count"/> methods across the whole assembly.</summary>
    public ImmutableArray<Analysis.MethodLeverage> TopLeverage(int count)
        => _index.TopLeverage(count);

    /// <summary>All method identities defined in the assembly.</summary>
    public ImmutableArray<Analysis.MethodIdentity> Methods
        => _index.Methods;

    /// <summary>Per-method body/call signals keyed by metadata token.</summary>
    public IReadOnlyDictionary<int, Analysis.MethodSignals> MethodSignalsByToken
        => _index.GetMethodSignals();

    /// <summary>Diagnostics recorded while analyzing method bodies (e.g. bodies skipped by the analyzer).</summary>
    public ImmutableArray<Analysis.AnalysisDiagnostic> Diagnostics
        => _index.Diagnostics;
}

/// <summary>An inbound call edge targeting a selected member, tagged with its originating assembly.</summary>
public readonly record struct CallerEdge(string Source, Analysis.DirectCall Call);
