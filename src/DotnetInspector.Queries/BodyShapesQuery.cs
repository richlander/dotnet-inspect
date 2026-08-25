using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries;

/// <summary>Typed result of one exact rendered-syntax search.</summary>
public abstract record BodyShapesResult
{
    private BodyShapesResult()
    {
    }

    /// <summary>The complete search result, including explicit per-body failures.</summary>
    public sealed record Available(BodyShapeSearchResult Search) : BodyShapesResult;

    /// <summary>The image contains no managed metadata and therefore has no searchable bodies.</summary>
    public sealed record NoMetadata : BodyShapesResult;

    /// <summary>A conditionally composed producer could not provide the candidate scope.</summary>
    public sealed record DependencyUnavailable : BodyShapesResult;

    /// <summary>The search failed before it could produce a complete result.</summary>
    public sealed record Failed(Exception Error) : BodyShapesResult;
}

/// <summary>Searches one exact rendered-syntax kind in already-acquired assembly content.</summary>
public static class BodyShapesQuery
{
    public static InspectionQuery<BodyShapesResult> Definition { get; } =
        new("Body shapes", InspectionCost.Unbounded);

    public static BodyShapesResult Execute(
        MetadataSource source,
        string kind,
        IReadOnlySet<int>? methodTokens = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        try
        {
            BodyShapeSearchResult search = methodTokens is null
                ? BodyShapeSearch.Search(source, kind)
                : BodyShapeSearch.Search(source, kind, methodTokens);
            return new BodyShapesResult.Available(search);
        }
        catch (Exception ex)
        {
            return new BodyShapesResult.Failed(ex);
        }
    }
}
