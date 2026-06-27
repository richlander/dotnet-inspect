using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// A method ranked by how much of the call graph leans on it. Leverage is led by
/// <see cref="DirectCallerCount"/> (distinct direct callers) — the methods most
/// worth drilling into first for perf or correctness triage — with outbound
/// shape (<see cref="Fanout"/>, <see cref="MaxDepth"/>, <see cref="LoopCallCount"/>)
/// as supporting cost signals.
/// </summary>
public sealed record MethodLeverage(
    MethodIdentity Method,
    int DirectCallerCount,
    int Fanout,
    int MaxDepth,
    int LoopCallCount,
    int RootReach = 0);

public static class MethodLeverageRanking
{
    /// <summary>
    /// The <paramref name="count"/> most-leveraged methods, ranked by distinct
    /// direct callers. Fanin/fanout/loop counts are measured across the whole
    /// index (a caller may live in any type), while <paramref name="scope"/>
    /// optionally restricts which methods are ranked (for example, members of a
    /// single selected type). Pure over the index's arrays so it is testable
    /// without a real assembly.
    /// </summary>
    /// <param name="maxDepth">
    /// Recursion guard for the longest-chain walk — a stack-overflow safety net for
    /// pathologically deep acyclic call chains, set well above real call depths so
    /// it does not cap the reported number in practice.
    /// </param>
    public static ImmutableArray<MethodLeverage> Top(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<MethodIdentity> methods,
        int count,
        Func<MethodIdentity, bool>? scope = null,
        int maxDepth = 64)
    {
        if (count <= 0)
            return [];

        var methodTokens = methods.Select(method => method.MetadataToken).ToHashSet();
        var methodMap = MethodDefinitionMap.Create(methods);

        // Distinct direct callers per callee token (fanin).
        var directCallers = new Dictionary<int, HashSet<int>>();
        // Outbound call sites, loop-region call sites, and intra-assembly callee
        // adjacency per caller token (fanout / loop / depth substrate).
        var fanout = new Dictionary<int, int>();
        var loopCalls = new Dictionary<int, int>();
        var adjacency = new Dictionary<int, HashSet<int>>();

        foreach (var call in directCalls)
        {
            int caller = call.Caller.MetadataToken;
            int callee = methodMap.Resolve(call);

            fanout[caller] = fanout.GetValueOrDefault(caller) + 1;
            if (call.InLoop)
                loopCalls[caller] = loopCalls.GetValueOrDefault(caller) + 1;

            if (callee != 0)
            {
                (directCallers.TryGetValue(callee, out var callers)
                    ? callers
                    : directCallers[callee] = new HashSet<int>()).Add(caller);
                (adjacency.TryGetValue(caller, out var callees)
                    ? callees
                    : adjacency[caller] = new HashSet<int>()).Add(callee);
            }
        }

        // Longest intra-assembly outbound call chain (in methods) from each node.
        // The on-stack set cuts cycle back-edges so recursion terminates. A value
        // is memoized only when its subtree cut no back-edge: a truncation depends
        // on the current path, so caching it would make the result order-dependent
        // (which root's ranking computed the node first). The depth guard is a
        // stack-overflow safety net for pathologically deep acyclic chains; when it
        // fires the value is treated as path-dependent and is not cached.
        var depthCache = new Dictionary<int, int>();
        var onStack = new HashSet<int>();

        (int Depth, bool CutCycle) LongestChain(int token, int guard)
        {
            if (depthCache.TryGetValue(token, out int cached))
                return (cached, false);
            if (!adjacency.TryGetValue(token, out var callees) || callees.Count == 0)
            {
                depthCache[token] = 1;
                return (1, false);
            }
            if (guard <= 0 || !onStack.Add(token))
                return (1, true);

            int best = 1;
            bool cut = false;
            foreach (int callee in callees)
            {
                var (childDepth, childCut) = LongestChain(callee, guard - 1);
                best = Math.Max(best, 1 + childDepth);
                cut |= childCut;
            }

            onStack.Remove(token);
            if (!cut)
                depthCache[token] = best;
            return (best, cut);
        }

        var rootReach = ComputeRootReach(methodTokens, adjacency);

        return methods
            .Where(method => scope is null || scope(method))
            .Select(method => new MethodLeverage(
                method,
                directCallers.TryGetValue(method.MetadataToken, out var callers) ? callers.Count : 0,
                fanout.GetValueOrDefault(method.MetadataToken),
                LongestChain(method.MetadataToken, maxDepth).Depth,
                loopCalls.GetValueOrDefault(method.MetadataToken),
                rootReach.GetValueOrDefault(method.MetadataToken)))
            .OrderByDescending(entry => entry.DirectCallerCount)
            .ThenByDescending(entry => entry.RootReach)
            .ThenByDescending(entry => entry.Fanout)
            .ThenByDescending(entry => entry.LoopCallCount)
            .ThenBy(entry => entry.Method.MetadataToken)
            .Take(count)
            .ToImmutableArray();
    }

    // Root reach: how many distinct roots (methods with no in-assembly caller — entry
    // points, public API, tests, event/command handlers) transitively reach each method
    // through the forward graph. It is the inbound *scale* multiplier beyond direct fanin:
    // a method with a single direct caller can still sit under many roots. Computed by a
    // bounded forward BFS from each root; a global visit budget keeps it tractable on very
    // large assemblies (where the count degrades to a lower bound).
    static Dictionary<int, int> ComputeRootReach(
        HashSet<int> methodTokens,
        IReadOnlyDictionary<int, HashSet<int>> adjacency)
    {
        const long VisitBudget = 6_000_000;
        var rootReach = new Dictionary<int, int>();
        var reached = new HashSet<int>();
        var queue = new Queue<int>();
        long budget = VisitBudget;

        var (components, componentByToken) = StronglyConnectedComponents(methodTokens, adjacency);
        var hasExternalIncoming = new bool[components.Count];
        foreach (var (caller, callees) in adjacency)
        {
            if (!componentByToken.TryGetValue(caller, out int callerComponent))
                continue;
            foreach (int callee in callees)
            {
                if (componentByToken.TryGetValue(callee, out int calleeComponent)
                    && callerComponent != calleeComponent)
                {
                    hasExternalIncoming[calleeComponent] = true;
                }
            }
        }

        // Deterministic order so a budget cut-off is reproducible.
        foreach (var componentIndex in Enumerable.Range(0, components.Count)
            .Where(i => !hasExternalIncoming[i])
            .OrderBy(i => components[i][0]))
        {
            if (budget <= 0)
                break;
            reached.Clear();
            queue.Clear();
            foreach (int root in components[componentIndex])
            {
                reached.Add(root);
                queue.Enqueue(root);
            }
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                rootReach[node] = rootReach.GetValueOrDefault(node) + 1;
                if (--budget <= 0)
                    break;
                if (adjacency.TryGetValue(node, out var callees))
                {
                    foreach (int callee in callees)
                        if (reached.Add(callee))
                            queue.Enqueue(callee);
                }
            }
        }

        return rootReach;
    }

    static (List<int[]> Components, Dictionary<int, int> ComponentByToken) StronglyConnectedComponents(
        HashSet<int> methodTokens,
        IReadOnlyDictionary<int, HashSet<int>> adjacency)
    {
        var reverse = new Dictionary<int, List<int>>();
        foreach (var (caller, callees) in adjacency)
        {
            if (!methodTokens.Contains(caller))
                continue;
            foreach (int callee in callees)
            {
                if (!methodTokens.Contains(callee))
                    continue;
                (reverse.TryGetValue(callee, out var callers)
                    ? callers
                    : reverse[callee] = []).Add(caller);
            }
        }

        foreach (var callers in reverse.Values)
            callers.Sort();

        var entered = new HashSet<int>();
        var exited = new HashSet<int>();
        var finishOrder = new List<int>(methodTokens.Count);
        var visitStack = new Stack<(int Token, bool Exit)>();

        foreach (int token in methodTokens.OrderBy(t => t))
        {
            if (entered.Contains(token))
                continue;
            visitStack.Push((token, Exit: false));
            while (visitStack.Count > 0)
            {
                var (current, exit) = visitStack.Pop();
                if (exit)
                {
                    if (exited.Add(current))
                        finishOrder.Add(current);
                    continue;
                }
                if (!entered.Add(current))
                    continue;

                visitStack.Push((current, Exit: true));
                if (!adjacency.TryGetValue(current, out var callees))
                    continue;
                foreach (int callee in callees.OrderByDescending(t => t))
                {
                    if (methodTokens.Contains(callee) && !entered.Contains(callee))
                        visitStack.Push((callee, Exit: false));
                }
            }
        }

        var components = new List<int[]>();
        var componentByToken = new Dictionary<int, int>();

        foreach (int root in finishOrder.AsEnumerable().Reverse())
        {
            if (componentByToken.ContainsKey(root))
                continue;

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(root);
            componentByToken[root] = components.Count;
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                component.Add(current);
                if (!reverse.TryGetValue(current, out var callers))
                    continue;
                for (int i = callers.Count - 1; i >= 0; i--)
                {
                    int caller = callers[i];
                    if (!componentByToken.ContainsKey(caller))
                    {
                        componentByToken[caller] = components.Count;
                        stack.Push(caller);
                    }
                }
            }

            component.Sort();
            components.Add(component.ToArray());
        }

        return (components, componentByToken);
    }
}
