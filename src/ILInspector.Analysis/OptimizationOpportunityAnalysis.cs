namespace ILInspector.Analysis;

internal static partial class OptimizationOpportunityAnalysis
{
    internal const int DelegateHotRootReach = 10;
    const int WeightNotableSizeBytes = 1024;

    internal static string AdjustDelegateConfidenceForReach(
        string shape,
        bool inLoop,
        string confidence,
        int rootReach)
        => !inLoop
            && confidence == "low"
            && rootReach >= DelegateHotRootReach
            && shape is
                "capturing-delegate"
                or "instance-method-group-delegate"
                    ? "medium"
                    : confidence;

    internal static OptimizationOpportunity AddFallbackMetadata(
        OptimizationOpportunity opportunity)
    {
        var runtimeAllocation =
            opportunity.RuntimeAllocationType
            ?? FallbackRuntimeAllocationType(opportunity);
        var pathContext =
            opportunity.PathContext ?? FallbackPathContext(opportunity);
        var weight = ComputeWeight(opportunity, runtimeAllocation);
        return runtimeAllocation != opportunity.RuntimeAllocationType
            || pathContext != opportunity.PathContext
            || weight != opportunity.Weight
                ? opportunity with
                {
                    RuntimeAllocationType = runtimeAllocation,
                    PathContext = pathContext,
                    Weight = weight,
                }
                : opportunity;
    }

    internal static string FormatPathContext(
        AllocationPathContext context)
        => context switch
        {
            AllocationPathContext.Branch => "branch",
            AllocationPathContext.SwitchArm => "switch arm",
            AllocationPathContext.LoopBody => "loop body",
            AllocationPathContext.ErrorPath => "error path",
            _ => "straight-line",
        };

    internal static string? FormatPathConfidence(
        AllocationPathConfidence confidence)
        => confidence switch
        {
            AllocationPathConfidence.DominatesReturn =>
                "dominates-return",
            AllocationPathConfidence.BehindBranch => "behind-branch",
            _ => null,
        };

    internal static string? FormatPostDominance(
        AllocationPostDominance postDominance)
        => postDominance switch
        {
            AllocationPostDominance.ReturnPostDominates =>
                "return-post-dominates",
            _ => null,
        };

    internal static string? FormatMultiplicity(
        AllocationMultiplicity multiplicity)
        => multiplicity switch
        {
            AllocationMultiplicity.Once => "once",
            AllocationMultiplicity.Conditional => "conditional",
            AllocationMultiplicity.Loop => "loop",
            _ => null,
        };

    static string? ComputeWeight(
        OptimizationOpportunity opportunity,
        string? runtimeAllocation)
    {
        if (runtimeAllocation is null)
            return null;

        bool loop = opportunity.Multiplicity == "loop"
            || (opportunity.Multiplicity is null && opportunity.InLoop);
        bool hotReach =
            opportunity.RootReach >= DelegateHotRootReach;
        bool notableSize = opportunity.EstimatedSizeBytes is { } size
            && size >= WeightNotableSizeBytes;

        if (loop && (hotReach || notableSize))
            return "high";
        if (loop || (hotReach && notableSize))
            return "medium";
        return "low";
    }

    static string? FallbackRuntimeAllocationType(
        OptimizationOpportunity opportunity)
        => opportunity.Shape switch
        {
            "allocation-hotspot" => "newobj/newarr/box",
            "async-state-machine" => "state machine",
            "box-value-type" => "boxed T",
            "capturing-delegate" => "delegate/display class",
            "instance-method-group-delegate" => "delegate",
            "enumerator-allocation" => "enumerator",
            "materialize-in-loop"
                when opportunity.Evidence.Contains(
                    ".ToArray",
                    StringComparison.Ordinal) => "T[]",
            "materialize-in-loop"
                when opportunity.Evidence.Contains(
                    ".ToList",
                    StringComparison.Ordinal) =>
                    "System.Collections.Generic.List<T>",
            "small-array"
                or "stackalloc-candidate"
                or "span-to-array-copy" => "T[]",
            "string-build-in-loop" => "System.String",
            "temporary-byte-array-copy" => "System.Byte[]",
            _ => null,
        };

    static string? FallbackPathContext(
        OptimizationOpportunity opportunity)
    {
        if (opportunity.ColdPath)
        {
            return FormatPathContext(
                AllocationPathContext.ErrorPath);
        }
        return opportunity.InLoop
            ? FormatPathContext(AllocationPathContext.LoopBody)
            : null;
    }
}
