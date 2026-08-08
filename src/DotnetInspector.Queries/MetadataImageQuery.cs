using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Typed result of inspecting image-level metadata facts.
/// </summary>
public abstract record MetadataImageResult
{
    private MetadataImageResult()
    {
    }

    /// <summary>The image contains managed metadata and produced an overview.</summary>
    public sealed record Available(MetadataImageOverview Overview) : MetadataImageResult;

    /// <summary>The image contains no managed metadata.</summary>
    public sealed record NoMetadata : MetadataImageResult;

    /// <summary>The query failed while reading the image.</summary>
    public sealed record Failed(Exception Error) : MetadataImageResult;
}

/// <summary>
/// Produces the image-level metadata overview from an already-open assembly session.
/// </summary>
public static class MetadataImageQuery
{
    public static InspectionQuery<MetadataImageResult> Definition { get; } =
        new("Metadata image", InspectionCost.NetworkFree);

    public static MetadataImageResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return session.MetadataImage() is { } overview
                ? new MetadataImageResult.Available(overview)
                : new MetadataImageResult.NoMetadata();
        }
        catch (Exception ex)
        {
            return new MetadataImageResult.Failed(ex);
        }
    }
}
