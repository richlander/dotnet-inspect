using System.Collections.Immutable;
using System.IO;
using System.Linq;
using DotnetInspector.Services;
using ILInspector.CallGraph;
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
        string sourceName,
        IAssemblyBindingPolicy bindingPolicy)
    {
        BodyIndex = index;
        Assembly = assembly;
        SourceName = sourceName;
        BindingPolicy = bindingPolicy;
    }

    /// <summary>
    /// Neutral Analysis index built for this command's requested capabilities and body scope.
    /// Consumers query it directly instead of growing a parallel forwarding surface here.
    /// </summary>
    public Analysis.LibraryBodyIndex BodyIndex { get; }

    public ResolvedAssemblyReference Assembly { get; }

    internal IAssemblyBindingPolicy BindingPolicy { get; }

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
    /// <see cref="ApiAnalysisInspection.AnalysisScopeFor"/>).
    /// <paramref name="includeAsyncSiblingOpportunities"/> selects only the
    /// allocation-independent sync-call-in-async producer.
    /// <paramref name="bodyScope"/>, when non-null,
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
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null,
        bool includeAsyncSiblingOpportunities = false)
    {
        var features = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= Analysis.LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
        {
            features |=
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        }
        if (includeAsyncSiblingOpportunities)
        {
            features |= Analysis.LibraryBodyAnalysisFeatures
                .AsyncSiblingOpportunities;
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
            Path.GetFileNameWithoutExtension(assemblyPath),
            BindingPolicyFor(resolver));
    }

    internal static MethodBodyInspectionSession OpenWithPrefetchedImage(
        string assemblyPath,
        PdbContext context,
        Analysis.LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        ResolvedAssemblyReference? assembly = null)
        => OpenWithPrefetchedImage(
            assemblyPath,
            context.GetPrefetchedImage(),
            features,
            resolver,
            assembly);

    internal static MethodBodyInspectionSession OpenWithPrefetchedImage(
        string assemblyPath,
        ImmutableArray<byte> image,
        Analysis.LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        ResolvedAssemblyReference? assembly = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<Analysis.TypeRef, bool>? bodyTypeScope = null)
    {
        System.Threading.Interlocked.Increment(ref OpenCountForTests);
        return new(
            Analysis.LibraryBodyIndex.OpenFromPrefetchedImage(
                assemblyPath,
                image,
                features,
                resolver,
                bodyScope,
                bodyTypeScope),
            assembly
                ?? ResolvedAssemblyReference.CreateFromPath(
                    assemblyPath,
                    AssemblyResolutionProvenance.Local(
                        "prefetched method body inspection")),
            Path.GetFileNameWithoutExtension(assemblyPath),
            BindingPolicyFor(resolver));
    }

    /// <summary>
    /// Inbound caller graph rooted at one method, extended across sibling caller-scope
    /// <paramref name="scopes"/> (opened as their own sessions).
    ///
    /// A <see langword="null"/> <paramref name="scopes"/> selects the same-assembly-only reverse
    /// graph. A non-null list creates one catalog scope over the target and supplied sessions;
    /// an empty list therefore preserves the requested catalog identity and ordering domain
    /// without adding another assembly.
    /// </summary>
    public Analysis.CallTreeNode CallerTree(
        int methodToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes) =>
        CallerTree(methodToken, scopes, out _);

    public Analysis.CallTreeNode CallerTree(
        int methodToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes,
        out Analysis.CatalogCallGraphDiagnostics diagnostics)
    {
        if (scopes is null)
        {
            diagnostics = Analysis.CatalogCallGraphDiagnostics.Empty;
            return BodyIndex.BuildCallerTree(methodToken);
        }

        using Analysis.CatalogCallGraphScope scope =
            CreateCallGraphScope([this, .. scopes]);
        Analysis.CallTreeNode tree =
            BodyIndex.BuildCallerTree(methodToken, scope);
        diagnostics = scope.Diagnostics;
        return scope.Detach(tree);
    }

    internal Analysis.CallTreeNode CallerTree(
        int methodToken,
        Analysis.CatalogCallGraphScope scope) =>
        BodyIndex.BuildCallerTree(methodToken, scope);

    /// <summary>
    /// Builds one bidirectional projection over direction-specific assembly
    /// scopes.
    /// </summary>
    public CallGraphProjection CallGraph(
        int methodToken,
        IReadOnlyList<MethodBodyInspectionSession>? callerScopes,
        IReadOnlyList<MethodBodyInspectionSession>? calleeScopes,
        out Analysis.CatalogCallGraphDiagnostics diagnostics)
    {
        if (callerScopes is not null || calleeScopes is not null)
        {
            callerScopes ??= [];
            calleeScopes ??= [];
        }

        Analysis.CallTreeNode callerRoot = CallerTree(
            methodToken,
            callerScopes,
            out Analysis.CatalogCallGraphDiagnostics callerDiagnostics);
        Analysis.CallTreeNode calleeRoot = CalleeTree(
            methodToken,
            calleeScopes,
            out Analysis.CatalogCallGraphDiagnostics calleeDiagnostics);
        diagnostics = new Analysis.CatalogCallGraphDiagnostics(
            callerDiagnostics.IncompleteNodeCount
                + calleeDiagnostics.IncompleteNodeCount,
            callerDiagnostics.IncompleteEdgeCount
                + calleeDiagnostics.IncompleteEdgeCount,
            callerDiagnostics.BindingIdentityConflictCount
                + calleeDiagnostics.BindingIdentityConflictCount);
        return CallGraphProjection.Create(callerRoot, calleeRoot);
    }

    Analysis.CallTreeNode CalleeTree(
        int methodToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes,
        out Analysis.CatalogCallGraphDiagnostics diagnostics)
    {
        if (scopes is null)
        {
            diagnostics = Analysis.CatalogCallGraphDiagnostics.Empty;
            return BodyIndex.BuildCallTree(methodToken);
        }

        using Analysis.CatalogCallGraphScope scope =
            CreateCallGraphScope([this, .. scopes]);
        Analysis.CallTreeNode tree =
            BodyIndex.BuildCallTree(methodToken, scope);
        diagnostics = scope.Diagnostics;
        return scope.Detach(tree);
    }

    internal static Analysis.CatalogCallGraphScope CreateCallGraphScope(
        IReadOnlyList<MethodBodyInspectionSession> sessions)
    {
        MethodBodyInspectionSession[] participants =
            CanonicalParticipants(sessions);
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            participants.Select(
                session => (
                    session.Assembly,
                    session.BindingPolicy)));
        return new Analysis.CatalogCallGraphScope(
            policy,
            participants.Select(
                session =>
                    new Analysis.CatalogCallGraphParticipant(
                        session.BodyIndex,
                        session.Assembly)));
    }

    static MethodBodyInspectionSession[] CanonicalParticipants(
        IReadOnlyList<MethodBodyInspectionSession> sessions) =>
        sessions
            .GroupBy(session => (
                session.Assembly.Identity,
                session.BodyIndex.DeclaredMethods.FirstOrDefault()
                    ?.ModuleVersionId ?? Guid.Empty))
            .Select(group => group.First())
            .ToArray();

    /// <summary>
    /// Inbound call edges targeting one method (<paramref name="targetToken"/>), each tagged with the
    /// <see cref="SourceName"/> of the assembly it originates in.
    ///
    /// Same-assembly edges match either the exact callee-definition token or a structural pattern
    /// built from the target's identity (the pattern adds MemberRef-form references, including
    /// abstract/interface members that have no body). When <paramref name="scopes"/> is non-empty and
    /// a pattern is available, each scope session is scanned for cross-assembly callers using
    /// complete catalog member correspondence (operand tokens are assembly-local, so only
    /// generation-scoped definition currency can establish a cross-assembly match).
    /// Results are unsorted and undeduplicated; the caller owns presentation ordering.
    /// </summary>
    public ImmutableArray<CallerEdge> CallerEdges(
        int targetToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes = null,
        Analysis.CallerResolutionPlan? declaringTypeResolution = null)
    {
        var selected = BodyIndex.DeclaredMethods.FirstOrDefault(
            method => method.MetadataToken == targetToken);
        var pattern = selected is { } identity
            ? Analysis.MemberPattern.Method(identity)
            : null;

        var edges = ImmutableArray.CreateBuilder<CallerEdge>();

        foreach (var call in BodyIndex.DirectCalls)
        {
            if (call.CalleeDefinitionToken == targetToken || (pattern is not null && pattern.Matches(call.Callee)))
                edges.Add(new CallerEdge(SourceName, call));
        }

        if (pattern is not null && scopes is { Count: > 0 })
        {
            MethodBodyInspectionSession[] participants =
                CanonicalParticipants([this, .. scopes]);
            var policy = new SourceRelativeAssemblyGroupBindingPolicy(
                participants.Select(
                    participant => (
                        participant.Assembly,
                        participant.BindingPolicy)));
            var target = new Analysis.CatalogCallGraphParticipant(
                participants[0].BodyIndex,
                participants[0].Assembly);
            var sources = participants
                .Skip(1)
                .Select(
                    participant =>
                        new Analysis.CatalogCallGraphParticipant(
                            participant.BodyIndex,
                            participant.Assembly))
                .ToArray();
            var sourceNames =
                new Dictionary<Analysis.LibraryBodyIndex, string>(
                    ReferenceEqualityComparer.Instance);
            foreach (MethodBodyInspectionSession participant
                in participants.Skip(1))
            {
                sourceNames.Add(
                    participant.BodyIndex,
                    participant.SourceName);
            }
            foreach (Analysis.CatalogDirectCaller match
                in Analysis.CatalogDirectCallerQuery.Find(
                    policy,
                    target,
                    targetToken,
                    sources,
                    declaringTypeResolution))
            {
                edges.Add(
                    new CallerEdge(
                        sourceNames[match.Participant.Index],
                        match.Call));
            }
        }

        return edges.ToImmutable();
    }

    static IAssemblyBindingPolicy BindingPolicyFor(
        IAssemblyReferenceResolver? resolver) =>
        resolver switch
        {
            IAssemblyBindingPolicy policy => policy,
            not null => new AssemblyReferenceBindingPolicy(resolver),
            null => NoResolverAssemblyBindingPolicy.Instance,
        };
}

/// <summary>An inbound call edge targeting a selected member, tagged with its originating assembly.</summary>
public readonly record struct CallerEdge(string Source, Analysis.DirectCall Call);
