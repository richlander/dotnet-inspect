using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using Inspector.Findings;

namespace ILInspector.Research;

public sealed record CallSiteCostEvidence(
    MethodIdentity Callee,
    ResearchEvidenceLocation EvidenceLocation,
    MethodSignals Signals,
    MethodLeverage? Leverage,
    bool CallInLoop)
{
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Signals.AllocInLoop)
                parts.Add("alloc-loop");
            if (Signals.Reflection > 0)
                parts.Add("reflection");
            if (CallInLoop)
                parts.Add("call-in-loop");
            if (Leverage is not null)
            {
                if (Leverage.RootReach
                    >= CallSiteCostFactProducer.RootReachThreshold)
                {
                    parts.Add($"root-reach {Leverage.RootReach}");
                }
                if (Leverage.DirectCallerCount
                    >= CallSiteCostFactProducer.DirectCallerThreshold)
                {
                    parts.Add(
                        $"direct-callers {Leverage.DirectCallerCount}");
                }
                if (Leverage.LoopCallCount
                    >= CallSiteCostFactProducer.LoopCallThreshold)
                {
                    parts.Add($"loop-calls {Leverage.LoopCallCount}");
                }
            }

            string text = parts.Count == 0
                ? "notable"
                : string.Join("; ", parts);
            return $"callee {Callee.Name}: {text}";
        }
    }

    public static bool TryCreate(
        DirectCall call,
        ResearchAssemblyContext assembly,
        out CallSiteCostEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(assembly);

        if (!CallSiteSemanticsEvidence.TryResolveCallee(
                call,
                assembly,
                out MethodIdentity? callee)
            || callee is null)
        {
            evidence = null;
            return false;
        }

        MethodSignals signals = assembly.Signals.GetValueOrDefault(
            callee.MetadataToken,
            MethodSignals.None);
        assembly.LeverageByToken.TryGetValue(
            callee.MetadataToken,
            out MethodLeverage? leverage);
        if (!CallSiteCostFactProducer.IsHighValue(
                call,
                signals,
                leverage))
        {
            evidence = null;
            return false;
        }

        evidence = new CallSiteCostEvidence(
            callee,
            ResearchEvidenceLocation.ForMethod(callee),
            signals,
            leverage,
            call.InLoop);
        return true;
    }
}

sealed class CallSiteCostFactProducer : IResearchFactProducer
{
    internal const int RootReachThreshold = 100;
    internal const int DirectCallerThreshold = 20;
    internal const int LoopCallThreshold = 20;

    static readonly AnnotationDescriptor CalleeCost =
        new("cost.callee", AnnotationCategory.Cost, "callee carries notable cost signals");

    public string Name => "call-site-cost";
    public IReadOnlyList<string> Produces { get; } = ["cost.callee"];
    public IReadOnlyList<string> DependsOn { get; } = [];
    public ResearchFactRequirements Requirements { get; } =
        ResearchFactRequirements.ForAssembly(
            LibraryBodyAnalysisFeatures.Allocations);

    public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        var callSites = assembly.InspectCallSites(context.Imported.MetadataToken);
        if (callSites.IsEmpty)
            return [];

        var facts = new List<Finding<IAnnotation>>();
        foreach (var finding in callSites)
        {
            var call = finding.Payload;
            if (CallSiteCostEvidence.TryCreate(
                    call,
                    assembly,
                    out CallSiteCostEvidence? evidence)
                && evidence is not null)
            {
                facts.Add(ResearchFactFinding.Project(
                    finding,
                    new Annotation<CallSiteCostEvidence>(
                        CalleeCost,
                        call.ILOffset,
                        evidence,
                        Formatter: static item => item.Detail)));
            }
        }
        return facts;
    }

    internal static bool IsHighValue(
        DirectCall call,
        MethodSignals signals,
        MethodLeverage? leverage)
        => signals.AllocInLoop
           || signals.Reflection > 0
           || (call.InLoop && leverage is { LoopCallCount: >= LoopCallThreshold });
}
