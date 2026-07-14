using System.Collections.Immutable;

namespace ILInspector.Analysis;

public sealed record CallerLoopWitnessStep(
    MethodIdentity Caller,
    MethodIdentity Callee,
    int ILOffset,
    CallKind Kind,
    bool InLoop);

public sealed record CallerLoopEvidence(
    int Depth,
    ImmutableArray<CallerLoopWitnessStep> Witness)
{
    ImmutableArray<CallerLoopWitnessStep> _witness
        = ImmutableArrayValueEquality.RequireInitialized(Witness, nameof(Witness));

    public ImmutableArray<CallerLoopWitnessStep> Witness
    {
        get => _witness;
        init => _witness = ImmutableArrayValueEquality.RequireInitialized(value, nameof(Witness));
    }

    public bool Equals(CallerLoopEvidence? other)
        => other is not null
            && Depth == other.Depth
            && ImmutableArrayValueEquality.SequenceEqual(Witness, other.Witness);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Depth);
        ImmutableArrayValueEquality.AddToHash(ref hash, Witness);
        return hash.ToHashCode();
    }
}

public static class CallerLoopEvidenceAnalysis
{
    public static ImmutableDictionary<int, CallerLoopEvidence> FindNearest(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        int maxDepth = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);

        var methodByToken = methods.ToDictionary(static method => method.MetadataToken);
        var methodMap = MethodDefinitionMap.Create(methods);
        if (maxDepth == 1)
        {
            return directCalls
                .Where(static call => call.InLoop)
                .Where(IsInvocation)
                .Select(call => new GraphEdge(call, methodMap.Resolve(call)))
                .Where(static edge =>
                    edge.CalleeToken != 0
                    && edge.Call.Caller.MetadataToken != edge.CalleeToken)
                .OrderBy(edge => EdgeKey(edge.Call, methodByToken[edge.CalleeToken]), StringComparer.Ordinal)
                .GroupBy(static edge => edge.CalleeToken)
                .ToImmutableDictionary(
                    static group => group.Key,
                    group => WitnessState.Seed(group.First()).BuildEvidence(methodByToken));
        }

        var edges = directCalls
            .Where(IsInvocation)
            .Select(call => new GraphEdge(call, methodMap.Resolve(call)))
            .Where(static edge => edge.CalleeToken != 0)
            .OrderBy(edge => EdgeKey(edge.Call, methodByToken[edge.CalleeToken]), StringComparer.Ordinal)
            .ToArray();
        var adjacency = edges
            .GroupBy(static edge => edge.Call.Caller.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        var best = new Dictionary<int, WitnessState>();
        var seeds = new List<WitnessState>();
        foreach (var edge in edges.Where(static edge =>
            edge.Call.InLoop
            && edge.Call.Caller.MetadataToken != edge.CalleeToken))
        {
            if (best.ContainsKey(edge.CalleeToken))
                continue;
            var state = WitnessState.Seed(edge);
            best[edge.CalleeToken] = state;
            seeds.Add(state);
        }

        WitnessState[] frontier = [.. seeds];
        while (frontier.Length > 0)
        {
            var next = new Dictionary<int, WitnessState>();
            var orderedNext = new List<WitnessState>();
            foreach (var state in frontier)
            {
                if (state.Depth >= maxDepth
                    || !adjacency.TryGetValue(state.Token, out var outgoing))
                {
                    continue;
                }

                foreach (var edge in outgoing)
                {
                    if (best.ContainsKey(edge.CalleeToken))
                        continue;
                    if (edge.CalleeToken == state.LoopCallerToken)
                        continue;
                    if (next.ContainsKey(edge.CalleeToken))
                        continue;
                    var candidate = state.Append(edge);
                    next[edge.CalleeToken] = candidate;
                    orderedNext.Add(candidate);
                }
            }

            frontier = [.. orderedNext];
            foreach (var state in frontier)
                best[state.Token] = state;
        }

        return best.ToImmutableDictionary(
            static pair => pair.Key,
            pair => pair.Value.BuildEvidence(methodByToken));
    }

    static bool IsInvocation(DirectCall call)
        => call.Kind is CallKind.Call or CallKind.CallVirtual or CallKind.NewObject;

    static string EdgeKey(DirectCall call, MethodIdentity callee)
        => $"{MethodKey(call.Caller)}|{call.ILOffset:X8}|{MethodKey(callee)}|{call.Kind}";

    static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    sealed record GraphEdge(DirectCall Call, int CalleeToken);

    sealed record WitnessState(
        int Token,
        int Depth,
        int LoopCallerToken,
        GraphEdge Incoming,
        WitnessState? Previous)
    {
        public static WitnessState Seed(GraphEdge edge)
            => new(
                edge.CalleeToken,
                1,
                edge.Call.Caller.MetadataToken,
                edge,
                null);

        public WitnessState Append(GraphEdge edge)
            => new(
                edge.CalleeToken,
                Depth + 1,
                LoopCallerToken,
                edge,
                this);

        public CallerLoopEvidence BuildEvidence(IReadOnlyDictionary<int, MethodIdentity> methodByToken)
        {
            var reversed = new List<GraphEdge>(Depth);
            for (WitnessState? state = this; state is not null; state = state.Previous)
                reversed.Add(state.Incoming);
            reversed.Reverse();
            return new CallerLoopEvidence(
                Depth,
                [.. reversed.Select(edge => new CallerLoopWitnessStep(
                    edge.Call.Caller,
                    methodByToken[edge.CalleeToken],
                    edge.Call.ILOffset,
                    edge.Call.Kind,
                    edge.Call.InLoop))]);
        }
    }
}
