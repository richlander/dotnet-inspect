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
    int LoopCallCount);

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
    public static ImmutableArray<MethodLeverage> Top(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<MethodIdentity> methods,
        int count,
        Func<MethodIdentity, bool>? scope = null,
        int maxDepth = 16)
    {
        if (count <= 0)
            return [];

        var methodTokens = new HashSet<int>();
        var tokenByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            methodTokens.Add(method.MetadataToken);
            tokenByKey.TryAdd(Key(method.DeclaringType, method.Name, method.ParameterTypes), method.MetadataToken);
        }

        int Resolve(DirectCall call)
        {
            if (methodTokens.Contains(call.OperandToken))
                return call.OperandToken;
            if (call.Callee.Kind == MemberKind.Unsupported)
                return 0;
            return tokenByKey.TryGetValue(Key(call.Callee.DeclaringType, call.Callee.Name, call.Callee.ParameterTypes), out int token)
                ? token
                : 0;
        }

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
            int callee = Resolve(call);

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

        // Longest intra-assembly outbound chain from each method, memoized and
        // bounded by maxDepth. The on-stack set breaks cycles (recursion through a
        // back-edge does not extend depth), keeping recursive methods finite.
        var depthCache = new Dictionary<int, int>();
        var onStack = new HashSet<int>();

        int Depth(int token, int budget)
        {
            if (budget <= 0)
                return 1;
            if (depthCache.TryGetValue(token, out int cached))
                return cached;
            if (!adjacency.TryGetValue(token, out var callees) || callees.Count == 0)
                return 1;
            if (!onStack.Add(token))
                return 1;

            int best = 1;
            foreach (int callee in callees)
                best = Math.Max(best, 1 + Depth(callee, budget - 1));

            onStack.Remove(token);
            // Cache only when measured to the full remaining budget, so a value
            // truncated by a shallower budget is never reused for a deeper query.
            if (budget >= maxDepth)
                depthCache[token] = best;
            return best;
        }

        return methods
            .Where(method => scope is null || scope(method))
            .Select(method => new MethodLeverage(
                method,
                directCallers.TryGetValue(method.MetadataToken, out var callers) ? callers.Count : 0,
                fanout.GetValueOrDefault(method.MetadataToken),
                Depth(method.MetadataToken, maxDepth),
                loopCalls.GetValueOrDefault(method.MetadataToken)))
            .OrderByDescending(entry => entry.DirectCallerCount)
            .ThenByDescending(entry => entry.Fanout)
            .ThenByDescending(entry => entry.LoopCallCount)
            .ThenBy(entry => entry.Method.MetadataToken)
            .Take(count)
            .ToImmutableArray();
    }

    static string Key(TypeRef declaringType, string name, ImmutableArray<TypeRef> parameterTypes)
        => $"{declaringType.ToQualifiedDisplayString()}|{name}|{string.Join(",", parameterTypes.Select(type => type.ToQualifiedDisplayString()))}";
}
