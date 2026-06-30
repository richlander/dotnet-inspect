using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class CallSiteCostFactProducer : IResearchFactProducer
{
    const int RootReachThreshold = 100;
    const int DirectCallerThreshold = 20;
    const int LoopCallThreshold = 20;

    static readonly AnnotationDescriptor CalleeCost =
        new("cost.callee", AnnotationCategory.Cost, "callee carries notable cost signals");

    public string Name => "call-site-cost";
    public IReadOnlyList<string> Produces { get; } = ["cost.callee"];
    public IReadOnlyList<string> DependsOn { get; } = ["allocation-occurrences"];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        if (!assembly.CallsByCaller.TryGetValue(context.Imported.MetadataToken, out var calls))
            return [];

        var facts = new List<Annotation>();
        foreach (var call in calls)
        {
            int calleeToken = ResolveCallee(call, assembly);
            if (calleeToken == 0)
                continue;

            var signals = assembly.Signals.GetValueOrDefault(calleeToken, MethodSignals.None);
            assembly.LeverageByToken.TryGetValue(calleeToken, out var leverage);
            if (!IsHighValue(signals, leverage))
                continue;
            facts.Add(new Annotation(CalleeCost, call.ILOffset, Detail(call.Callee, signals, leverage, call.InLoop)));
        }
        return facts;
    }

    static int ResolveCallee(DirectCall call, ResearchAssemblyContext assembly)
        => assembly.Signals.ContainsKey(call.CalleeDefinitionToken) || assembly.LeverageByToken.ContainsKey(call.CalleeDefinitionToken)
            ? call.CalleeDefinitionToken
            : 0;

    static bool IsHighValue(MethodSignals signals, MethodLeverage? leverage)
        => signals.AllocInLoop
           || !signals.ExceptionTypes.IsEmpty
           || signals.Reflection > 0
           || signals.Unsafe
           || leverage is { RootReach: >= RootReachThreshold }
           || leverage is { DirectCallerCount: >= DirectCallerThreshold }
           || leverage is { LoopCallCount: >= LoopCallThreshold };

    static string Detail(MemberRef callee, MethodSignals signals, MethodLeverage? leverage, bool callInLoop)
    {
        var parts = new List<string>();
        if (signals.AllocInLoop)
            parts.Add("alloc-loop");
        if (!signals.ExceptionTypes.IsEmpty)
            parts.Add($"throws {string.Join("/", signals.ExceptionTypes.Take(2))}");
        if (signals.Reflection > 0)
            parts.Add("reflection");
        if (signals.Unsafe)
            parts.Add("unsafe");
        if (callInLoop)
            parts.Add("call-in-loop");
        if (leverage is not null)
        {
            if (leverage.RootReach > 0)
                parts.Add($"root-reach {leverage.RootReach}");
            if (leverage.DirectCallerCount > 0)
                parts.Add($"direct-callers {leverage.DirectCallerCount}");
            if (leverage.LoopCallCount > 0)
                parts.Add($"loop-calls {leverage.LoopCallCount}");
        }

        var text = parts.Count == 0 ? "notable" : string.Join("; ", parts);
        return $"callee {callee.Name}: {text}";
    }
}
