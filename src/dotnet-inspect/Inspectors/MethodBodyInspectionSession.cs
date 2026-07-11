using System.Collections.Immutable;
using System.IO;
using System.Linq;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Method-body inspection composition layer (see <c>docs/design/method-body-inspection.md</c>):
/// opens a <see cref="Analysis.LibraryBodyIndex"/> once with command-selected capabilities and
/// body scope, and composes caller data across source-attributed assembly sessions.
///
/// This sits above <c>ILInspector.Analysis</c> (and, in later slices, Metadata / Decompiler /
/// Research); it is deliberately not part of the lower-level <c>DotnetInspector.Services</c>
/// package layer. Early slices open from a <c>dllPath</c>; the target is to consume an
/// <see cref="AssemblyInspectionSession"/> / <see cref="ResolvedAssemblyReference"/> once the
/// shared-PE-owner composition lands.
/// </summary>
public sealed class MethodBodyInspectionSession
{
    MethodBodyInspectionSession(Analysis.LibraryBodyIndex index, string sourceName)
    {
        BodyIndex = index;
        SourceName = sourceName;
    }

    /// <summary>
    /// Neutral Analysis index built for this command's requested capabilities and body scope.
    /// Consumers query it directly instead of growing a parallel forwarding surface here.
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex { get; }

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
    /// <c>ApiOutputFormatter.AnalysisScopeFor</c>). <paramref name="bodyScope"/>, when non-null,
    /// restricts body decoding to the given method tokens (a single-member "targeted" build); it is
    /// only valid when every requested section's facts are local to those members (Calls / Unsafe
    /// Operations / Allocation-Safety-Cost facts) — reverse/aggregate sections require a full build.
    /// </summary>
    public static MethodBodyInspectionSession Open(string assemblyPath, IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true, bool includeOpportunities = true, IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        System.Threading.Interlocked.Increment(ref OpenCountForTests);
        return new(Analysis.LibraryBodyIndex.Open(assemblyPath, resolver, includeAllocations, includeOpportunities, bodyScope, bodyTypeScope), Path.GetFileNameWithoutExtension(assemblyPath));
    }

    /// <summary>
    /// Inbound caller graph rooted at one method, extended across sibling caller-scope
    /// <paramref name="scopes"/> (opened as their own sessions). Empty scopes fall back to the
    /// same-assembly-only reverse graph.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(int methodToken, IReadOnlyList<MethodBodyInspectionSession> scopes)
        => scopes is { Count: > 0 }
            ? BodyIndex.BuildCallerTree(methodToken, scopes.Select(s => s.BodyIndex).ToArray())
            : BodyIndex.BuildCallerTree(methodToken);

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
        var selected = BodyIndex.Methods.FirstOrDefault(m => m.MetadataToken == targetToken);
        var pattern = selected is { } identity
            ? Analysis.MemberPattern.Method(identity.DeclaringType, identity.Name, identity.ParameterTypes)
            : null;

        var edges = ImmutableArray.CreateBuilder<CallerEdge>();

        foreach (var call in BodyIndex.DirectCalls)
        {
            if (call.CalleeDefinitionToken == targetToken || (pattern is not null && pattern.Matches(call.Callee)))
                edges.Add(new CallerEdge(SourceName, call));
        }

        if (pattern is not null && scopes is { Count: > 0 })
        {
            foreach (var scope in scopes)
            {
                foreach (var call in scope.BodyIndex.DirectCalls)
                {
                    if (pattern.MatchesCrossAssembly(call.Callee))
                        edges.Add(new CallerEdge(scope.SourceName, call));
                }
            }
        }

        return edges.ToImmutable();
    }
}

/// <summary>An inbound call edge targeting a selected member, tagged with its originating assembly.</summary>
public readonly record struct CallerEdge(string Source, Analysis.DirectCall Call);
