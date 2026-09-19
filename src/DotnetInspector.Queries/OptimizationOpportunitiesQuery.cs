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
    /// Completed optimization opportunities, generated-framework type evidence,
    /// and any per-method diagnostics reported by the analysis.
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

/// <summary>Reads an already-produced focused optimization Analysis result.</summary>
public static class OptimizationOpportunitiesQuery
{
    public static InspectionQuery<OptimizationOpportunitiesResult> Definition { get; } =
        new("Optimization opportunities", InspectionCost.Unbounded);

    public static OptimizationOpportunitiesResult Execute(
        LibraryOptimizationAnalysisResult analysis,
        bool includeAllocationFanout)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        try
        {
            if (!analysis.WasRequested)
            {
                throw new InvalidOperationException(
                    "Optimization opportunities were not requested for this "
                    + "Analysis execution.");
            }

            return new OptimizationOpportunitiesResult.Available(
                analysis.Opportunities,
                includeAllocationFanout
                    ? analysis.AllocationFanoutOpportunities
                    : [],
                analysis.GeneratedFrameworkTypes,
                analysis.Receipt.Diagnostics);
        }
        catch (Exception ex)
        {
            return new OptimizationOpportunitiesResult.Failed(ex);
        }
    }
}
