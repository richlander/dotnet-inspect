using ILInspector.Analysis.Planning;

namespace DotnetInspector.PerformanceOracles;

public static class MethodBodyAnalyzerPlans
{
    public static MethodBodyAnalyzerPlan ThrowPresence { get; } =
        MethodBodyAnalyzerPlanner.Plan(
            MethodBodyAnalyzerDeclarations.ThrowPresence);

    public static MethodBodyAnalyzerPlan ForwardShallow { get; } =
        MethodBodyAnalyzerPlanner.Plan(
            MethodBodyAnalyzerDeclarations.ThrowPresence,
            MethodBodyAnalyzerDeclarations.DirectCalls,
            MethodBodyAnalyzerDeclarations.Allocations);

    public static MethodBodyAnalyzerPlan Mixed { get; } =
        MethodBodyAnalyzerPlanner.Plan(
            MethodBodyAnalyzerDeclarations.ThrowPresence,
            MethodBodyAnalyzerDeclarations.DirectCalls,
            MethodBodyAnalyzerDeclarations.Allocations,
            MethodBodyAnalyzerDeclarations.StableGetter,
            MethodBodyAnalyzerDeclarations.BoundedFlow);
}
