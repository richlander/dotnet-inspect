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
    MethodBodyInspectionSession(
        Analysis.LibraryBodyIndex index,
        ResolvedAssemblyReference assembly,
        string sourceName)
    {
        BodyIndex = index;
        Assembly = assembly;
        SourceName = sourceName;
    }

    /// <summary>
    /// Neutral Analysis index built for this command's requested capabilities and body scope.
    /// Consumers query it directly instead of growing a parallel forwarding surface here.
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex { get; }

    public ResolvedAssemblyReference Assembly { get; }

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
    /// <see cref="ApiAnalysisInspection.AnalysisScopeFor"/>). <paramref name="bodyScope"/>, when non-null,
    /// restricts body decoding to the given method tokens (a single-member "targeted" build); it is
    /// only valid when every requested section's facts are local to those members (Calls / Unsafe
    /// Operations / Allocation-Safety-Cost facts) — reverse/aggregate sections require a full build.
    /// </summary>
    public static MethodBodyInspectionSession Open(string assemblyPath, IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true, bool includeOpportunities = true, IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        var features = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= Analysis.LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
            features |= Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        var assembly = ResolvedAssemblyReference.CreateFromPath(
            assemblyPath,
            AssemblyResolutionProvenance.Local("method body inspection"));
        return OpenWithFeatures(
            assembly,
            features,
            resolver,
            bodyScope,
            bodyTypeScope);
    }

    internal static MethodBodyInspectionSession Open(
        ResolvedAssemblyReference assembly,
        IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true,
        bool includeOpportunities = true,
        IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        var features = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= Analysis.LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
        {
            features |=
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        }

        return OpenWithFeatures(
            assembly,
            features,
            resolver,
            bodyScope,
            bodyTypeScope);
    }

    internal static MethodBodyInspectionSession OpenWithFeatures(
        ResolvedAssemblyReference assembly,
        Analysis.LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        string assemblyPath = assembly.Path
            ?? throw new ArgumentException(
                "Method-body inspection requires a path-backed assembly.",
                nameof(assembly));
        System.Threading.Interlocked.Increment(ref OpenCountForTests);
        return new(
            Analysis.LibraryBodyIndex.Open(
                assemblyPath,
                features,
                resolver,
                bodyScope,
                bodyTypeScope),
            assembly,
            Path.GetFileNameWithoutExtension(assemblyPath));
    }

    internal static MethodBodyInspectionSession OpenWithPrefetchedImage(
        string assemblyPath,
        PdbContext context,
        Analysis.LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null)
    {
        System.Threading.Interlocked.Increment(ref OpenCountForTests);
        return new(
            Analysis.LibraryBodyIndex.OpenFromPrefetchedImage(
                assemblyPath,
                context.GetPrefetchedImage(),
                features,
                resolver),
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    "prefetched method body inspection")),
            Path.GetFileNameWithoutExtension(assemblyPath));
    }

    /// <summary>
    /// Inbound caller graph rooted at one method, extended across sibling caller-scope
    /// <paramref name="scopes"/> (opened as their own sessions).
    ///
    /// A <see langword="null"/> <paramref name="scopes"/> means no cross-assembly scope was
    /// requested and selects the same-assembly-only reverse graph. A non-null but empty list means
    /// a scope was requested and contributed no assemblies; that still takes the cross-assembly
    /// builder, because the two builders key the reverse graph differently and the result must not
    /// depend on how many scope assemblies happened to survive filtering.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(int methodToken, IReadOnlyList<MethodBodyInspectionSession>? scopes)
        => scopes is null
            ? BodyIndex.BuildCallerTree(methodToken)
            : BodyIndex.BuildCallerTree(methodToken, scopes.Select(s => s.BodyIndex).ToArray());

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
        IReadOnlyList<MethodBodyInspectionSession>? scopes = null,
        Analysis.CallerResolutionPlan? resolutionPlan = null)
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
            ArgumentNullException.ThrowIfNull(resolutionPlan);
            foreach (var scope in scopes)
            {
                foreach (var call in scope.BodyIndex.DirectCalls)
                {
                    Analysis.TypeRef declaringType =
                        Analysis.GenericMemberIdentity.OpenDeclaringType(
                            call.Callee.DeclaringType);
                    if (resolutionPlan.GetRelation(
                            scope.Assembly,
                            declaringType)
                            is Analysis.CandidateTypeRelation.SameDefinition
                        && pattern.MatchesResolvedCrossAssembly(call.Callee))
                    {
                        edges.Add(new CallerEdge(scope.SourceName, call));
                    }
                }
            }
        }

        return edges.ToImmutable();
    }
}

/// <summary>An inbound call edge targeting a selected member, tagged with its originating assembly.</summary>
public readonly record struct CallerEdge(string Source, Analysis.DirectCall Call);
