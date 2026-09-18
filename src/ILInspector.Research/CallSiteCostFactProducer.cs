using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using Inspector.Findings;

namespace ILInspector.Research;

public enum CallSiteCostEvidenceInputKind
{
    AllocationInLoop,
    Reflection,
    CallInLoop,
    RootReach,
    DirectCallers,
    LoopCalls,
}

public sealed record CallSiteCostEvidenceInput(
    CallSiteCostEvidenceInputKind Kind,
    int? Value = null);

public sealed record CallSiteCostEvidence(
    MethodIdentity Callee,
    ResearchEvidenceLocation EvidenceLocation,
    MethodSignals Signals,
    MethodLeverage? Leverage,
    bool CallInLoop)
{
    public IReadOnlyList<CallSiteCostEvidenceInput> AggregateInputs
    {
        get
        {
            var inputs = new List<CallSiteCostEvidenceInput>();
            if (Signals.AllocInLoop)
            {
                inputs.Add(new(
                    CallSiteCostEvidenceInputKind.AllocationInLoop));
            }
            if (Signals.Reflection > 0)
            {
                inputs.Add(new(
                    CallSiteCostEvidenceInputKind.Reflection,
                    Signals.Reflection));
            }
            if (CallInLoop)
            {
                inputs.Add(new(
                    CallSiteCostEvidenceInputKind.CallInLoop));
            }
            if (Leverage is not null)
            {
                if (Leverage.RootReach
                    >= CallSiteCostFactProducer.RootReachThreshold)
                {
                    inputs.Add(new(
                        CallSiteCostEvidenceInputKind.RootReach,
                        Leverage.RootReach));
                }
                if (Leverage.DirectCallerCount
                    >= CallSiteCostFactProducer.DirectCallerThreshold)
                {
                    inputs.Add(new(
                        CallSiteCostEvidenceInputKind.DirectCallers,
                        Leverage.DirectCallerCount));
                }
                if (Leverage.LoopCallCount
                    >= CallSiteCostFactProducer.LoopCallThreshold)
                {
                    inputs.Add(new(
                        CallSiteCostEvidenceInputKind.LoopCalls,
                        Leverage.LoopCallCount));
                }
            }
            return inputs;
        }
    }

    public string Detail
    {
        get
        {
            string[] parts =
            [
                .. AggregateInputs.Select(static input =>
                    input.Kind switch
                    {
                        CallSiteCostEvidenceInputKind.AllocationInLoop =>
                            "alloc-loop",
                        CallSiteCostEvidenceInputKind.Reflection =>
                            "reflection",
                        CallSiteCostEvidenceInputKind.CallInLoop =>
                            "call-in-loop",
                        CallSiteCostEvidenceInputKind.RootReach =>
                            $"root-reach {input.Value}",
                        CallSiteCostEvidenceInputKind.DirectCallers =>
                            $"direct-callers {input.Value}",
                        CallSiteCostEvidenceInputKind.LoopCalls =>
                            $"loop-calls {input.Value}",
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(input)),
                    }),
            ];
            string text = parts.Length == 0
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
    public IReadOnlyList<string> Produces { get; } = [CalleeCost.Id];
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
