using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class MethodHeaderLeverageFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor HeaderCost =
        new("cost.method", AnnotationCategory.Cost, "method has call-graph leverage");

    public string Name => "method-header-leverage";
    public IReadOnlyList<string> Produces { get; } = ["cost.method"];
    public IReadOnlyList<string> DependsOn { get; } = ["call-site-cost"];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context) => [];

    public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        if (!assembly.LeverageByToken.TryGetValue(context.Imported.MetadataToken, out var leverage))
            return [];
        if (leverage is { RootReach: 0, DirectCallerCount: 0, LoopCallCount: 0 })
            return [];

        var parts = new List<string>();
        if (leverage.RootReach > 0)
            parts.Add($"root-reach {leverage.RootReach}");
        if (leverage.DirectCallerCount > 0)
            parts.Add($"direct-callers {leverage.DirectCallerCount}");
        if (leverage.LoopCallCount > 0)
            parts.Add($"loop-calls {leverage.LoopCallCount}");
        if (leverage.Fanout > 0)
            parts.Add($"fanout {leverage.Fanout}");

        return [new ResearchHeaderFact(HeaderCost, string.Join("; ", parts))];
    }
}
