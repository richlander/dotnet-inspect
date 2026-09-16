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
/// relationships from an already-acquired whole-assembly body index.
/// </summary>
public static class ImplementationProfilesQuery
{
    public static InspectionQuery<ImplementationProfilesResult> Definition
        { get; } =
        new("Implementation profiles", InspectionCost.Unbounded);

    public static ImplementationProfilesResult Execute(
        LibraryBodyIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        try
        {
            return new ImplementationProfilesResult.Available(
                index.ImplementationProfiles(),
                index.OverloadRelationships(),
                index.GeneratedFrameworkTypes.ToImmutableHashSet(),
                index.Diagnostics);
        }
        catch (Exception ex)
        {
            return new ImplementationProfilesResult.Failed(ex);
        }
    }
}
