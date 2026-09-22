using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>Independent bounds for one local root-path search.</summary>
public sealed record LibraryBodyRootPathLimits(
    int MaximumDepth,
    int MaximumNodes,
    int MaximumEdges,
    int MaximumPaths)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumNodes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumEdges, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPaths, 1);
    }
}

/// <summary>One visible boundary on root-path absence or exhaustiveness.</summary>
public abstract record LibraryBodyRootPathBoundary
{
    private protected LibraryBodyRootPathBoundary()
    {
    }

    /// <summary>A body-analysis failure can hide a relevant local edge.</summary>
    public sealed record AnalysisIncomplete(int DiagnosticCount)
        : LibraryBodyRootPathBoundary;

    /// <summary>The input index omits bodies outside a caller-supplied scope.</summary>
    public sealed record PartialMethodEvidenceScope
        : LibraryBodyRootPathBoundary;

    /// <summary>Typed local call operands could not be resolved to MethodDefs.</summary>
    public sealed record UnresolvedLocalCalls(int Count)
        : LibraryBodyRootPathBoundary;

    /// <summary>Generated execution bodies could not be attributed to source methods.</summary>
    public sealed record UnattributedGeneratedBodies(int Count)
        : LibraryBodyRootPathBoundary;

    /// <summary>One destination search could continue beyond the requested depth.</summary>
    public sealed record DepthLimit(
        MetadataMethodAddress Destination,
        int MaximumDepth)
        : LibraryBodyRootPathBoundary;

    /// <summary>The operation attempted to admit another search state.</summary>
    public sealed record NodeBudget(
        MetadataMethodAddress Destination,
        int MaximumNodes)
        : LibraryBodyRootPathBoundary;

    /// <summary>The operation attempted to examine another logical edge.</summary>
    public sealed record EdgeBudget(
        MetadataMethodAddress Destination,
        int MaximumEdges)
        : LibraryBodyRootPathBoundary;

    /// <summary>An additional reachable root/destination pair was observed.</summary>
    public sealed record PathBudget(
        MetadataMethodAddress Root,
        MetadataMethodAddress Destination,
        int MaximumPaths)
        : LibraryBodyRootPathBoundary;
}

/// <summary>One local logical call edge and all of its physical call sites.</summary>
public sealed class LibraryBodyRootPathStep
{
    internal LibraryBodyRootPathStep(
        MethodIdentity caller,
        MethodIdentity callee,
        ImmutableArray<DirectCall> callSites)
    {
        Caller = caller;
        Callee = callee;
        CallSites = callSites;
    }

    public MethodIdentity Caller { get; }
    public MethodIdentity Callee { get; }
    public ImmutableArray<DirectCall> CallSites { get; }
}

/// <summary>
/// One shortest local path for an exact ordered root/destination pair.
/// </summary>
public sealed class LibraryBodyRootPathWitness
{
    internal LibraryBodyRootPathWitness(
        MethodIdentity root,
        MethodIdentity destination,
        ImmutableArray<LibraryBodyRootPathStep> steps)
    {
        Root = root;
        Destination = destination;
        Steps = steps;
    }

    public MethodIdentity Root { get; }
    public MethodIdentity Destination { get; }
    public ImmutableArray<LibraryBodyRootPathStep> Steps { get; }
    public int Depth => Steps.Length;
}

/// <summary>Bounded-work receipt for one local root-path search.</summary>
public sealed record LibraryBodyRootPathReceipt(
    int RequestedRoots,
    int RequestedDestinations,
    int DestinationSearches,
    int SearchNodes,
    int SearchedEdges,
    int ObservedReachablePairs,
    int ReturnedPaths);

/// <summary>
/// Positive shortest witnesses plus every boundary that limits negative use.
/// </summary>
public sealed record LibraryBodyRootPathResult(
    LibraryBodyModuleIdentity Module,
    ImmutableArray<LibraryBodyRootPathWitness> Witnesses,
    ImmutableArray<LibraryBodyRootPathBoundary> Boundaries,
    LibraryBodyRootPathReceipt Receipt)
{
    public bool IsComplete => Boundaries.IsEmpty;
}

/// <summary>
/// Finds bounded shortest paths from exact roots to exact destinations within
/// one focused local call-graph result.
/// </summary>
public static class LibraryBodyRootPathAnalysis
{
    public static LibraryBodyRootPathResult FindShortestPaths(
        LibraryCallGraphAnalysisResult callGraph,
        ImmutableArray<MetadataMethodAddress> roots,
        ImmutableArray<MetadataMethodAddress> destinations,
        LibraryBodyRootPathLimits limits)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(limits);
        callGraph.EnsureRequested();
        limits.Validate();

        Dictionary<int, MethodIdentity> methods = callGraph.DeclaredMethods
            .ToDictionary(static method => method.MetadataToken);
        ImmutableArray<MethodIdentity> normalizedRoots = Normalize(
            callGraph,
            methods,
            roots,
            nameof(roots));
        ImmutableArray<MethodIdentity> normalizedDestinations = Normalize(
            callGraph,
            methods,
            destinations,
            nameof(destinations));
        LibraryBodyLocalCallGraph graph =
            callGraph.RootPathGraph();
        IReadOnlyDictionary<
            int,
            ImmutableArray<LibraryBodyLocalCallEdge>> reverse =
            graph.Reverse;

        var witnesses =
            ImmutableArray.CreateBuilder<LibraryBodyRootPathWitness>();
        var boundaries =
            ImmutableArray.CreateBuilder<LibraryBodyRootPathBoundary>();
        if (!callGraph.HasFullMethodEvidenceScope)
        {
            boundaries.Add(
                new LibraryBodyRootPathBoundary
                    .PartialMethodEvidenceScope());
        }
        if (graph.UnresolvedLocalCalls > 0)
        {
            boundaries.Add(
                new LibraryBodyRootPathBoundary.UnresolvedLocalCalls(
                    graph.UnresolvedLocalCalls));
        }
        if (!callGraph.Diagnostics.IsEmpty)
        {
            boundaries.Add(
                new LibraryBodyRootPathBoundary.AnalysisIncomplete(
                    callGraph.Diagnostics.Length));
        }

        HashSet<int> rootTokens =
        [
            .. normalizedRoots.Select(static root => root.MetadataToken),
        ];
        int destinationSearches = 0;
        int searchNodes = 0;
        int searchedEdges = 0;
        int observedReachablePairs = 0;
        HashSet<int> reachedUnattributedGeneratedBodies = [];
        bool stopRemainingSearch = false;

        foreach (MethodIdentity destination in normalizedDestinations)
        {
            if (searchNodes == limits.MaximumNodes)
            {
                boundaries.Add(
                    new LibraryBodyRootPathBoundary.NodeBudget(
                        Address(destination),
                        limits.MaximumNodes));
                break;
            }

            destinationSearches++;
            searchNodes++;
            var distance = new Dictionary<int, int>
            {
                [destination.MetadataToken] = 0,
            };
            var nextByCaller =
                new Dictionary<int, LibraryBodyLocalCallEdge>();
            List<int> frontier = [destination.MetadataToken];
            int reachedRoots =
                rootTokens.Contains(destination.MetadataToken) ? 1 : 0;
            bool depthLimited = false;

            while (frontier.Count > 0)
            {
                if (reachedRoots == rootTokens.Count)
                    break;

                var nextFrontier = new List<int>();
                foreach (int current in frontier)
                {
                    if (graph.UnattributedGeneratedBodyTokens.Contains(
                            current))
                    {
                        reachedUnattributedGeneratedBodies.Add(current);
                    }
                    if (!reverse.TryGetValue(
                            current,
                            out ImmutableArray<
                                LibraryBodyLocalCallEdge> incoming))
                    {
                        continue;
                    }

                    int nextDepth = distance[current] + 1;
                    foreach (LibraryBodyLocalCallEdge edge in incoming)
                    {
                        if (searchedEdges == limits.MaximumEdges)
                        {
                            boundaries.Add(
                                new LibraryBodyRootPathBoundary.EdgeBudget(
                                    Address(destination),
                                    limits.MaximumEdges));
                            stopRemainingSearch = true;
                            break;
                        }
                        searchedEdges++;

                        int callerToken = edge.Caller.MetadataToken;
                        if (distance.ContainsKey(callerToken))
                            continue;
                        if (nextDepth > limits.MaximumDepth)
                        {
                            depthLimited = true;
                            continue;
                        }
                        if (searchNodes == limits.MaximumNodes)
                        {
                            boundaries.Add(
                                new LibraryBodyRootPathBoundary.NodeBudget(
                                    Address(destination),
                                    limits.MaximumNodes));
                            stopRemainingSearch = true;
                            break;
                        }

                        searchNodes++;
                        distance.Add(callerToken, nextDepth);
                        nextByCaller.Add(callerToken, edge);
                        nextFrontier.Add(callerToken);
                        if (graph.UnattributedGeneratedBodyTokens.Contains(
                                callerToken))
                        {
                            reachedUnattributedGeneratedBodies.Add(
                                callerToken);
                        }
                        if (rootTokens.Contains(callerToken))
                            reachedRoots++;
                    }

                    if (stopRemainingSearch)
                        break;
                }

                if (stopRemainingSearch)
                    break;
                nextFrontier.Sort();
                frontier = nextFrontier;
            }

            if (depthLimited)
            {
                boundaries.Add(
                    new LibraryBodyRootPathBoundary.DepthLimit(
                        Address(destination),
                        limits.MaximumDepth));
            }

            foreach (int rootToken in distance.Keys
                .Where(rootTokens.Contains)
                .Order())
            {
                observedReachablePairs++;
                if (witnesses.Count == limits.MaximumPaths)
                {
                    boundaries.Add(
                        new LibraryBodyRootPathBoundary.PathBudget(
                            Address(methods[rootToken]),
                            Address(destination),
                            limits.MaximumPaths));
                    stopRemainingSearch = true;
                    break;
                }

                witnesses.Add(
                    BuildWitness(
                        methods[rootToken],
                        destination,
                        nextByCaller));
            }

            if (stopRemainingSearch)
                break;
        }

        if (reachedUnattributedGeneratedBodies.Count > 0)
        {
            boundaries.Add(
                new LibraryBodyRootPathBoundary
                    .UnattributedGeneratedBodies(
                        reachedUnattributedGeneratedBodies.Count));
        }

        return new LibraryBodyRootPathResult(
            callGraph.ModuleIdentity,
            witnesses.ToImmutable(),
            boundaries.ToImmutable(),
            new LibraryBodyRootPathReceipt(
                normalizedRoots.Length,
                normalizedDestinations.Length,
                destinationSearches,
                searchNodes,
                searchedEdges,
                observedReachablePairs,
                witnesses.Count));
    }

    static ImmutableArray<MethodIdentity> Normalize(
        LibraryCallGraphAnalysisResult callGraph,
        IReadOnlyDictionary<int, MethodIdentity> methods,
        ImmutableArray<MetadataMethodAddress> addresses,
        string parameterName)
    {
        if (addresses.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one exact method address is required.",
                parameterName);
        }

        var normalized = new Dictionary<int, MethodIdentity>();
        foreach (MetadataMethodAddress address in addresses)
        {
            if (address.ModuleVersionId
                != callGraph.ModuleIdentity.ModuleVersionId)
            {
                throw new ArgumentException(
                    "Method address belongs to a different module.",
                    parameterName);
            }
            if (!methods.TryGetValue(
                    address.Token,
                    out MethodIdentity? method))
            {
                throw new ArgumentException(
                    $"Method address 0x{address.Token:X8} is not declared "
                        + "by the selected call-graph result.",
                    parameterName);
            }
            normalized.TryAdd(address.Token, method);
        }

        return
        [
            .. normalized.Values.OrderBy(
                static method => method.MetadataToken),
        ];
    }

    internal static LibraryBodyLocalCallGraph BuildLocalGraph(
        LibraryCallGraphAnalysisResult analysis)
    {
        Dictionary<int, MethodIdentity> methods = analysis.DeclaredMethods
            .ToDictionary(static method => method.MetadataToken);
        MethodDefinitionMap methodMap =
            analysis.DeclaredMethodMap;
        var callSites = new Dictionary<
            (int Caller, int Callee),
            List<DirectCall>>();
        int unresolvedLocalCalls = 0;
        HashSet<int> unattributedGeneratedBodyTokens = [];
        foreach (DirectCall call in analysis.DirectCalls)
        {
            if (call.Kind is not (
                CallKind.Call
                or CallKind.CallVirtual
                or CallKind.NewObject))
            {
                continue;
            }
            if (call.Caller == call.EvidenceMethod
                && CompilerGeneratedNames
                    .RequiresDeclaredOwner(call.Caller)
                && analysis.ResolveDeclaredMethod(call.Caller) is null)
            {
                unattributedGeneratedBodyTokens.Add(
                    call.Caller.MetadataToken);
            }

            int calleeToken = methodMap.Resolve(call);
            if (calleeToken == 0)
            {
                if (methodMap.CouldResolveToCurrentModule(call))
                    unresolvedLocalCalls++;
                continue;
            }
            if (!methods.ContainsKey(call.Caller.MetadataToken))
            {
                throw new InvalidOperationException(
                    $"Body index call at IL_0x{call.ILOffset:X4} names "
                        + $"undeclared caller 0x{call.Caller.MetadataToken:X8}.");
            }

            var key = (call.Caller.MetadataToken, calleeToken);
            if (!callSites.TryGetValue(key, out List<DirectCall>? sites))
                callSites.Add(key, sites = []);
            sites.Add(call);
        }

        return new LibraryBodyLocalCallGraph(
            callSites
                .Select(pair =>
                    new LibraryBodyLocalCallEdge(
                        methods[pair.Key.Caller],
                        methods[pair.Key.Callee],
                        [
                            .. pair.Value
                                .OrderBy(static call =>
                                    call.EvidenceMethod.MetadataToken)
                                .ThenBy(static call => call.ILOffset)
                                .ThenBy(static call => call.OperandToken)
                                .ThenBy(static call => call.Kind),
                        ]))
                .GroupBy(static edge => edge.Callee.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .OrderBy(static edge => edge.Caller.MetadataToken)
                        .ToImmutableArray()),
            unresolvedLocalCalls,
            unattributedGeneratedBodyTokens);
    }

    static LibraryBodyRootPathWitness BuildWitness(
        MethodIdentity root,
        MethodIdentity destination,
        IReadOnlyDictionary<
            int,
            LibraryBodyLocalCallEdge> nextByCaller)
    {
        var steps =
            ImmutableArray.CreateBuilder<LibraryBodyRootPathStep>();
        int current = root.MetadataToken;
        while (current != destination.MetadataToken)
        {
            LibraryBodyLocalCallEdge edge = nextByCaller[current];
            steps.Add(
                new LibraryBodyRootPathStep(
                    edge.Caller,
                    edge.Callee,
                    edge.CallSites));
            current = edge.Callee.MetadataToken;
        }

        return new LibraryBodyRootPathWitness(
            root,
            destination,
            steps.ToImmutable());
    }

    static MetadataMethodAddress Address(MethodIdentity method) =>
        new(
            method.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(
                method.MetadataToken & 0x00FFFFFF));
}

internal sealed record LibraryBodyLocalCallEdge(
    MethodIdentity Caller,
    MethodIdentity Callee,
    ImmutableArray<DirectCall> CallSites);

internal sealed record LibraryBodyLocalCallGraph(
    IReadOnlyDictionary<
        int,
        ImmutableArray<LibraryBodyLocalCallEdge>> Reverse,
    int UnresolvedLocalCalls,
    IReadOnlySet<int> UnattributedGeneratedBodyTokens);
