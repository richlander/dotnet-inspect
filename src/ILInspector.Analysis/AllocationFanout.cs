using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Known IL-visible allocation impact for one method invocation. Once-path counts compose only
/// through exact call sites and allocation sites classified once on normally returning control
/// flow; every uncertain path remains separate.
/// </summary>
public sealed record AllocationFanoutSummary(
    MethodIdentity Method,
    int DirectSites,
    long OncePaths,
    long ConditionalPaths,
    long RepeatedPaths,
    long UnknownPaths,
    long CachedSites,
    long OpaquePaths,
    bool Saturated);

public static class AllocationFanout
{
    public static ImmutableArray<AllocationFanoutSummary> Analyze(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> allocationOccurrences)
    {
        if (methods.IsDefault)
            throw new ArgumentException("Method census must be initialized.", nameof(methods));
        if (directCalls.IsDefault)
            throw new ArgumentException("Call census must be initialized.", nameof(directCalls));

        var methodMap = MethodDefinitionMap.Create(methods);
        var edgesByCaller = directCalls
            .Where(IsInvocation)
            .Select(call => new Edge(call, methodMap.Resolve(call)))
            .GroupBy(static edge => edge.Call.Caller.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static edge => edge.Call.ILOffset)
                    .ThenBy(static edge => edge.TargetToken)
                    .ToArray());
        var recursiveComponents = FindRecursiveComponents(methods, edgesByCaller);
        int NodeForToken(int token)
            => recursiveComponents.TryGetValue(token, out int component)
                ? -component - 1
                : token;

        var tokensByNode = methods
            .GroupBy(method => NodeForToken(method.MetadataToken))
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static method => method.MetadataToken).ToArray());
        var edgesByNode = edgesByCaller
            .SelectMany(static pair => pair.Value)
            .GroupBy(edge => NodeForToken(edge.Call.Caller.MetadataToken))
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());
        var impactByNode = new Dictionary<int, Impact>();

        Impact ImpactFor(int node)
        {
            if (impactByNode.TryGetValue(node, out var cached))
                return cached;

            var pending = new Stack<(int Node, bool Compose)>();
            pending.Push((node, false));
            while (pending.TryPop(out var item))
            {
                if (impactByNode.ContainsKey(item.Node))
                    continue;

                if (!item.Compose)
                {
                    pending.Push((item.Node, true));
                    if (edgesByNode.TryGetValue(item.Node, out var dependencies))
                    {
                        for (int i = dependencies.Length - 1; i >= 0; i--)
                        {
                            var dependency = dependencies[i];
                            int targetNode = dependency.TargetToken == 0
                                ? 0
                                : NodeForToken(dependency.TargetToken);
                            if (dependency.Call.ExactTarget
                                && targetNode != 0
                                && targetNode != item.Node
                                && !impactByNode.ContainsKey(targetNode))
                            {
                                pending.Push((targetNode, false));
                            }
                        }
                    }
                    continue;
                }

                var impact = new Impact();
                bool recursiveNode = item.Node < 0;
                foreach (int methodToken in tokensByNode[item.Node])
                {
                    var local = LocalImpact(
                        methodToken,
                        allocationOccurrences.TryGetValue(methodToken, out var occurrences)
                            ? occurrences
                            : []);
                    impact.Add(
                        local,
                        recursiveNode ? AllocationMultiplicity.Unknown : AllocationMultiplicity.Once);
                }

                if (edgesByNode.TryGetValue(item.Node, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        int targetNode = edge.TargetToken == 0
                            ? 0
                            : NodeForToken(edge.TargetToken);
                        if (!edge.Call.ExactTarget
                            || targetNode == 0
                            || targetNode == item.Node)
                        {
                            impact.AddOpaque();
                            continue;
                        }

                        impact.Add(
                            impactByNode[targetNode],
                            recursiveNode ? AllocationMultiplicity.Unknown : edge.Call.Multiplicity);
                    }
                }
                impactByNode[item.Node] = impact;
            }

            return impactByNode[node];
        }

        var summaries = ImmutableArray.CreateBuilder<AllocationFanoutSummary>();
        foreach (var method in methods.OrderBy(static method => method.MetadataToken))
        {
            var impact = ImpactFor(NodeForToken(method.MetadataToken));
            int directSites = DirectSiteCount(
                allocationOccurrences.TryGetValue(method.MetadataToken, out var occurrences)
                    ? occurrences
                    : []);
            if (directSites == 0 && !impact.HasKnownAllocation)
                continue;

            summaries.Add(new AllocationFanoutSummary(
                method,
                directSites,
                impact.OncePaths,
                impact.ConditionalPaths,
                impact.RepeatedPaths,
                impact.UnknownPaths,
                impact.CachedSites,
                impact.OpaquePaths,
                impact.Saturated));
        }
        return summaries.ToImmutable();
    }

    static Impact LocalImpact(int methodToken, ImmutableArray<AllocationOccurrence> occurrences)
    {
        var impact = new Impact();
        foreach (var occurrence in occurrences)
        {
            if (!IsHeapAllocation(occurrence))
                continue;
            if (occurrence.Frequency == AllocationFrequency.CachedOnce)
            {
                impact.AddCached(methodToken, occurrence.ILOffset);
                continue;
            }

            impact.AddLocal(occurrence.Multiplicity);
        }
        return impact;
    }

    static int DirectSiteCount(ImmutableArray<AllocationOccurrence> occurrences)
        => occurrences.Count(IsHeapAllocation);

    static bool IsHeapAllocation(AllocationOccurrence occurrence)
        => occurrence.CountsAsHeapAllocation || occurrence.Kind == AllocationKind.Enumerator;

    static bool IsInvocation(DirectCall call)
        => call.Kind is CallKind.Call or CallKind.CallVirtual or CallKind.NewObject or CallKind.CallIndirect;

    static Dictionary<int, int> FindRecursiveComponents(
        ImmutableArray<MethodIdentity> methods,
        IReadOnlyDictionary<int, Edge[]> edgesByCaller)
    {
        var adjacency = methods.ToDictionary(
            static method => method.MetadataToken,
            method => edgesByCaller.TryGetValue(method.MetadataToken, out var edges)
                ? edges
                    .Where(static edge => edge.Call.ExactTarget && edge.TargetToken != 0)
                    .Select(static edge => edge.TargetToken)
                    .Distinct()
                    .ToArray()
                : []);
        var reverse = methods.ToDictionary(
            static method => method.MetadataToken,
            static _ => new List<int>());
        foreach (var (caller, targets) in adjacency)
        {
            foreach (int target in targets)
                reverse[target].Add(caller);
        }

        var visited = new HashSet<int>();
        var finishingOrder = new List<int>(methods.Length);
        foreach (var method in methods)
        {
            if (visited.Contains(method.MetadataToken))
                continue;

            var pending = new Stack<(int Token, bool Finish)>();
            pending.Push((method.MetadataToken, false));
            while (pending.TryPop(out var item))
            {
                if (item.Finish)
                {
                    finishingOrder.Add(item.Token);
                    continue;
                }
                if (!visited.Add(item.Token))
                    continue;

                pending.Push((item.Token, true));
                var targets = adjacency[item.Token];
                for (int i = targets.Length - 1; i >= 0; i--)
                    pending.Push((targets[i], false));
            }
        }

        var recursiveComponents = new Dictionary<int, int>();
        int nextComponent = 0;
        visited.Clear();
        for (int i = finishingOrder.Count - 1; i >= 0; i--)
        {
            int start = finishingOrder[i];
            if (!visited.Add(start))
                continue;
            var component = new List<int>();
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out int member))
            {
                component.Add(member);
                foreach (int caller in reverse[member])
                {
                    if (visited.Add(caller))
                        pending.Push(caller);
                }
            }

            if (component.Count > 1 || HasExactSelfEdge(component[0]))
            {
                int componentId = nextComponent++;
                foreach (int token in component)
                    recursiveComponents[token] = componentId;
            }
        }
        return recursiveComponents;

        bool HasExactSelfEdge(int token)
            => adjacency[token].Contains(token);
    }

    sealed record Edge(DirectCall Call, int TargetToken);

    struct Impact
    {
        HashSet<long>? _cachedSiteKeys;

        public long OncePaths;
        public long ConditionalPaths;
        public long RepeatedPaths;
        public long UnknownPaths;
        public long OpaquePaths;
        public bool Saturated;

        public readonly long CachedSites => _cachedSiteKeys?.Count ?? 0;

        public readonly bool HasKnownAllocation =>
            OncePaths > 0
            || ConditionalPaths > 0
            || RepeatedPaths > 0
            || UnknownPaths > 0
            || CachedSites > 0;

        public void AddLocal(AllocationMultiplicity multiplicity)
        {
            switch (multiplicity)
            {
                case AllocationMultiplicity.Once:
                    AddTo(ref OncePaths, 1);
                    break;
                case AllocationMultiplicity.Conditional:
                    AddTo(ref ConditionalPaths, 1);
                    break;
                case AllocationMultiplicity.Loop:
                    AddTo(ref RepeatedPaths, 1);
                    break;
                default:
                    AddTo(ref UnknownPaths, 1);
                    break;
            }
        }

        public void AddCached(int methodToken, int ilOffset)
        {
            _cachedSiteKeys ??= [];
            _cachedSiteKeys.Add(((long)(uint)methodToken << 32) | (uint)ilOffset);
        }

        public void AddOpaque() => AddTo(ref OpaquePaths, 1);

        public void Add(Impact callee, AllocationMultiplicity callMultiplicity)
        {
            Saturated |= callee.Saturated;
            if (callee._cachedSiteKeys is not null)
            {
                _cachedSiteKeys ??= [];
                _cachedSiteKeys.UnionWith(callee._cachedSiteKeys);
            }
            AddTo(ref OpaquePaths, callee.OpaquePaths);
            switch (callMultiplicity)
            {
                case AllocationMultiplicity.Once:
                    AddTo(ref OncePaths, callee.OncePaths);
                    AddTo(ref ConditionalPaths, callee.ConditionalPaths);
                    AddTo(ref RepeatedPaths, callee.RepeatedPaths);
                    AddTo(ref UnknownPaths, callee.UnknownPaths);
                    break;
                case AllocationMultiplicity.Conditional:
                    AddTo(ref ConditionalPaths, Sum(callee.OncePaths, callee.ConditionalPaths));
                    AddTo(ref RepeatedPaths, callee.RepeatedPaths);
                    AddTo(ref UnknownPaths, callee.UnknownPaths);
                    break;
                case AllocationMultiplicity.Loop:
                    AddTo(ref RepeatedPaths, Sum(callee.OncePaths, callee.ConditionalPaths, callee.RepeatedPaths));
                    AddTo(ref UnknownPaths, callee.UnknownPaths);
                    break;
                default:
                    AddTo(ref UnknownPaths, Sum(
                        callee.OncePaths,
                        callee.ConditionalPaths,
                        callee.RepeatedPaths,
                        callee.UnknownPaths));
                    break;
            }
        }

        long Sum(params long[] values)
        {
            long result = 0;
            foreach (long value in values)
            {
                if (long.MaxValue - result < value)
                {
                    Saturated = true;
                    return long.MaxValue;
                }
                result += value;
            }
            return result;
        }

        void AddTo(ref long target, long value)
        {
            if (long.MaxValue - target < value)
            {
                target = long.MaxValue;
                Saturated = true;
                return;
            }
            target += value;
        }
    }
}
