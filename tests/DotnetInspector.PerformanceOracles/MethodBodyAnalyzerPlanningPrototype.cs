using System.Collections.Immutable;

namespace DotnetInspector.PerformanceOracles;

public enum MethodBodyInstructionAccess
{
    ForwardOnly,
    RetainedPrefix,
}

public enum MethodBodyInstructionDetail
{
    OpcodeAndExtent,
    SelectiveOperands,
}

public readonly record struct MethodBodyAnalyzerDemand
{
    public MethodBodyAnalyzerDemand(
        MethodBodyInstructionAccess access,
        MethodBodyInstructionDetail detail)
    {
        if ((uint)access
            > (uint)MethodBodyInstructionAccess.RetainedPrefix)
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
        if ((uint)detail
            > (uint)MethodBodyInstructionDetail.SelectiveOperands)
        {
            throw new ArgumentOutOfRangeException(nameof(detail));
        }
        Access = access;
        Detail = detail;
    }

    public MethodBodyInstructionAccess Access { get; }

    public MethodBodyInstructionDetail Detail { get; }

    public MethodBodyAnalyzerDemand Join(MethodBodyAnalyzerDemand other) =>
        new(
            (MethodBodyInstructionAccess)Math.Max(
                (int)Access,
                (int)other.Access),
            (MethodBodyInstructionDetail)Math.Max(
                (int)Detail,
                (int)other.Detail));
}

public sealed record MethodBodyAnalyzerDeclaration
{
    public MethodBodyAnalyzerDeclaration(
        string identity,
        MethodBodyAnalyzerDemand demand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
        Demand = demand;
    }

    public string Identity { get; }

    public MethodBodyAnalyzerDemand Demand { get; }
}

public enum MethodBodyInstructionSourceKind
{
    NoRetentionStream,
    LazyRetainedSequence,
}

public sealed class MethodBodyAnalyzerPlan
{
    internal MethodBodyAnalyzerPlan(
        ImmutableArray<MethodBodyAnalyzerDeclaration> analyzers,
        MethodBodyAnalyzerDemand demand,
        MethodBodyInstructionSourceKind source)
    {
        Analyzers = analyzers;
        Demand = demand;
        Source = source;
    }

    public ImmutableArray<MethodBodyAnalyzerDeclaration> Analyzers { get; }

    public MethodBodyAnalyzerDemand Demand { get; }

    public MethodBodyInstructionSourceKind Source { get; }
}

public static class MethodBodyAnalyzerPlanner
{
    public static MethodBodyAnalyzerPlan Plan(
        params MethodBodyAnalyzerDeclaration[] analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        if (analyzers.Length == 0)
        {
            throw new ArgumentException(
                "At least one Method-body analyzer is required.",
                nameof(analyzers));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        var frozen =
            ImmutableArray.CreateBuilder<MethodBodyAnalyzerDeclaration>(
                analyzers.Length);
        MethodBodyAnalyzerDemand demand = default;
        for (int i = 0; i < analyzers.Length; i++)
        {
            MethodBodyAnalyzerDeclaration analyzer =
                analyzers[i]
                ?? throw new ArgumentException(
                    "A Method-body analyzer cannot be null.",
                    nameof(analyzers));
            if (!identities.Add(analyzer.Identity))
            {
                throw new ArgumentException(
                    $"Method-body analyzer '{analyzer.Identity}' was "
                    + "requested more than once.",
                    nameof(analyzers));
            }

            frozen.Add(analyzer);
            demand = i == 0
                ? analyzer.Demand
                : demand.Join(analyzer.Demand);
        }

        MethodBodyInstructionSourceKind source =
            SupportsNoRetentionStream(demand)
                ? MethodBodyInstructionSourceKind.NoRetentionStream
                : MethodBodyInstructionSourceKind.LazyRetainedSequence;
        return new(frozen.MoveToImmutable(), demand, source);
    }

    static bool SupportsNoRetentionStream(MethodBodyAnalyzerDemand demand) =>
        demand.Access == MethodBodyInstructionAccess.ForwardOnly
        && demand.Detail == MethodBodyInstructionDetail.OpcodeAndExtent;
}

public static class MethodBodyAnalyzerDeclarations
{
    public static MethodBodyAnalyzerDeclaration ThrowPresence { get; } =
        new(
            "ThrowPresence",
            new(
                MethodBodyInstructionAccess.ForwardOnly,
                MethodBodyInstructionDetail.OpcodeAndExtent));

    public static MethodBodyAnalyzerDeclaration DirectCalls { get; } =
        new(
            "DirectCalls",
            new(
                MethodBodyInstructionAccess.ForwardOnly,
                MethodBodyInstructionDetail.OpcodeAndExtent));

    public static MethodBodyAnalyzerDeclaration Allocations { get; } =
        new(
            "Allocations",
            new(
                MethodBodyInstructionAccess.ForwardOnly,
                MethodBodyInstructionDetail.OpcodeAndExtent));

    public static MethodBodyAnalyzerDeclaration StableGetter { get; } =
        new(
            "StableGetter",
            new(
                MethodBodyInstructionAccess.ForwardOnly,
                MethodBodyInstructionDetail.SelectiveOperands));

    public static MethodBodyAnalyzerDeclaration BoundedFlow { get; } =
        new(
            "BoundedFlow",
            new(
                MethodBodyInstructionAccess.RetainedPrefix,
                MethodBodyInstructionDetail.SelectiveOperands));
}

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
