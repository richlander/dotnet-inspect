using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>Typed result of ranking whole-assembly call-graph leverage.</summary>
public abstract record TopLeverageResult
{
    private TopLeverageResult()
    {
    }

    /// <summary>
    /// The ranked method set, generated-framework type evidence, and any per-method
    /// diagnostics reported by the analysis.
    /// </summary>
    public sealed record Available(
        ImmutableArray<MethodLeverage> Methods,
        ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
        ImmutableArray<AnalysisDiagnostic> Diagnostics) : TopLeverageResult;

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : TopLeverageResult;

    /// <summary>The query failed while acquiring or reading whole-assembly analysis.</summary>
    public sealed record Failed(Exception Error) : TopLeverageResult;
}

/// <summary>Reads leverage from an already-produced focused Analysis result.</summary>
public static class TopLeverageQuery
{
    public static InspectionQuery<TopLeverageResult> Definition { get; } =
        new("Top leverage", InspectionCost.Unbounded);

    public static TopLeverageResult Execute(
        LibraryLeverageAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        try
        {
            if (!analysis.WasRequested)
            {
                throw new InvalidOperationException(
                    "Leverage analysis was not requested for this Analysis "
                    + "execution.");
            }

            return new TopLeverageResult.Available(
                analysis.Top(int.MaxValue),
                analysis.GeneratedFrameworkTypes,
                analysis.Receipt.Diagnostics);
        }
        catch (Exception ex)
        {
            return new TopLeverageResult.Failed(ex);
        }
    }
}
