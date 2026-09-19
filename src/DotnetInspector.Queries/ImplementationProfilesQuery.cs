using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>Typed result of collecting whole-assembly implementation profiles.</summary>
public abstract record ImplementationProfilesResult
{
    private ImplementationProfilesResult()
    {
    }

    public sealed record Available(
        ImmutableArray<MethodImplementationProfile> Profiles,
        ImmutableArray<OverloadCallRelationship> OverloadRelationships,
        ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
        ImmutableArray<AnalysisDiagnostic> Diagnostics)
        : ImplementationProfilesResult;

    public sealed record NoMetadata : ImplementationProfilesResult;

    public sealed record Failed(Exception Error)
        : ImplementationProfilesResult;
}

/// <summary>
/// Collects objective method-body measurements and overload-family call
/// relationships from an already-produced focused Analysis result.
/// </summary>
public static class ImplementationProfilesQuery
{
    public static InspectionQuery<ImplementationProfilesResult> Definition
        { get; } =
        new("Implementation profiles", InspectionCost.Unbounded);

    public static ImplementationProfilesResult Execute(
        LibraryImplementationProfileAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        try
        {
            if (!analysis.WasRequested)
            {
                throw new InvalidOperationException(
                    "Implementation profiles were not requested for this "
                    + "Analysis execution.");
            }

            return new ImplementationProfilesResult.Available(
                analysis.Profiles,
                analysis.OverloadRelationships,
                analysis.GeneratedFrameworkTypes,
                analysis.Receipt.Diagnostics);
        }
        catch (Exception ex)
        {
            return new ImplementationProfilesResult.Failed(ex);
        }
    }
}
