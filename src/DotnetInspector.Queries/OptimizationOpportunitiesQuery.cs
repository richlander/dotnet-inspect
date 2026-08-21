using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading whole-assembly optimization evidence.</summary>
public abstract record OptimizationOpportunitiesResult
{
    private OptimizationOpportunitiesResult()
    {
    }

    /// <summary>
    /// Raw optimization opportunities, generated-framework type evidence, and any
    /// per-method diagnostics reported by the analysis.
    /// </summary>
    public sealed record Available(
        ImmutableArray<OptimizationOpportunity> Opportunities,
        ImmutableArray<OptimizationOpportunity> AllocationFanoutOpportunities,
        ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
        ImmutableArray<AnalysisDiagnostic> Diagnostics)
        : OptimizationOpportunitiesResult;

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : OptimizationOpportunitiesResult;

    /// <summary>The query failed while acquiring or reading whole-assembly analysis.</summary>
    public sealed record Failed(Exception Error) : OptimizationOpportunitiesResult;
}

/// <summary>Reads optimization evidence from an already-acquired whole-assembly body index.</summary>
public static class OptimizationOpportunitiesQuery
{
    public static InspectionQuery<OptimizationOpportunitiesResult> Definition { get; } =
        new("Optimization opportunities", InspectionCost.Unbounded);

    public static OptimizationOpportunitiesResult Execute(
        LibraryBodyIndex index,
        bool includeAllocationFanout)
    {
        ArgumentNullException.ThrowIfNull(index);

        try
        {
            return new OptimizationOpportunitiesResult.Available(
                index.OptimizationOpportunities,
                includeAllocationFanout
                    ? index.AllocationFanoutOpportunities
                    : [],
                index.GeneratedFrameworkTypes.ToImmutableHashSet(),
                index.Diagnostics);
        }
        catch (Exception ex)
        {
            return new OptimizationOpportunitiesResult.Failed(ex);
        }
    }
}
