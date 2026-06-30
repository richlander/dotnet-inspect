using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class MethodHeaderLeverageFactProducer : IResearchFactProducer
{
    const int RootReachThreshold = 100;
    const int DirectCallerThreshold = 20;
    const int LoopCallThreshold = 20;

    static readonly AnnotationDescriptor HeaderCost =
        new("cost.method", AnnotationCategory.Cost, "method has call-graph leverage");

    public string Name => "method-header-leverage";
    public IReadOnlyList<string> Produces { get; } = ["cost.method"];
    public IReadOnlyList<string> DependsOn { get; } = [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context) => [];

    public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        if (!assembly.LeverageByToken.TryGetValue(context.Imported.MetadataToken, out var leverage))
            return [];
        var parts = new List<string>();
        if (leverage.RootReach >= RootReachThreshold)
            parts.Add($"root-reach {leverage.RootReach}");
        if (leverage.DirectCallerCount >= DirectCallerThreshold)
            parts.Add($"direct-callers {leverage.DirectCallerCount}");
        if (leverage.LoopCallCount >= LoopCallThreshold)
            parts.Add($"loop-calls {leverage.LoopCallCount}");
        if (parts.Count == 0)
            return [];

        return [new ResearchHeaderFact(HeaderCost, string.Join("; ", parts))];
    }
}
