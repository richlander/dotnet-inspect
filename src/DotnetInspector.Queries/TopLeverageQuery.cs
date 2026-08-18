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

/// <summary>Ranks leverage from an already-acquired whole-assembly body index.</summary>
public static class TopLeverageQuery
{
    public static InspectionQuery<TopLeverageResult> Definition { get; } =
        new("Top leverage", InspectionCost.Unbounded);

    public static TopLeverageResult Execute(LibraryBodyIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        try
        {
            return new TopLeverageResult.Available(
                index.TopLeverage(int.MaxValue),
                index.GeneratedFrameworkTypes.ToImmutableHashSet(),
                index.Diagnostics);
        }
        catch (Exception ex)
        {
            return new TopLeverageResult.Failed(ex);
        }
    }
}
