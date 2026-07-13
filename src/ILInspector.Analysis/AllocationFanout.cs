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
        var impactByToken = new Dictionary<int, Impact>();

        Impact ImpactFor(int token)
        {
            if (impactByToken.TryGetValue(token, out var cached))
                return cached;

            var pending = new Stack<(int Token, bool Compose)>();
            pending.Push((token, false));
            while (pending.TryPop(out var item))
            {
                if (impactByToken.ContainsKey(item.Token))
                    continue;

                if (!item.Compose)
                {
                    pending.Push((item.Token, true));
                    if (edgesByCaller.TryGetValue(item.Token, out var dependencies))
                    {
                        for (int i = dependencies.Length - 1; i >= 0; i--)
                        {
                            var dependency = dependencies[i];
                            if (dependency.Call.ExactTarget
                                && dependency.TargetToken != 0
                                && !InSameRecursiveComponent(
                                    item.Token,
                                    dependency.TargetToken,
                                    recursiveComponents)
                                && !impactByToken.ContainsKey(dependency.TargetToken))
                            {
                                pending.Push((dependency.TargetToken, false));
                            }
                        }
                    }
                    continue;
                }

                var impact = LocalImpact(
                    item.Token,
                    allocationOccurrences.TryGetValue(item.Token, out var occurrences)
                        ? occurrences
                        : []);
                if (edgesByCaller.TryGetValue(item.Token, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        if (!edge.Call.ExactTarget
                            || edge.TargetToken == 0
                            || InSameRecursiveComponent(
                                item.Token,
                                edge.TargetToken,
                                recursiveComponents))
                        {
                            impact.AddOpaque();
                            continue;
                        }

                        impact.Add(impactByToken[edge.TargetToken], edge.Call.Multiplicity);
                    }
                }
                impactByToken[item.Token] = impact;
            }

            return impactByToken[token];
        }

        var summaries = ImmutableArray.CreateBuilder<AllocationFanoutSummary>();
        foreach (var method in methods.OrderBy(static method => method.MetadataToken))
        {
            var impact = ImpactFor(method.MetadataToken);
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

    static bool InSameRecursiveComponent(
        int callerToken,
        int targetToken,
        IReadOnlyDictionary<int, int> recursiveComponents)
        => recursiveComponents.TryGetValue(callerToken, out int callerComponent)
            && recursiveComponents.TryGetValue(targetToken, out int targetComponent)
            && callerComponent == targetComponent;

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
