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

    /// <summary>
    /// The selected root produced an overview. <paramref name="Root"/> is null
    /// only for source-less COFF metadata, which intentionally has no synthetic
    /// PE/RVA root identity.
    /// </summary>
    public sealed record Available(
        MetadataImageOverview Overview,
        MetadataRootInspection? Root = null) : MetadataImageResult;

    /// <summary>The image contains no managed metadata.</summary>
    public sealed record NoMetadata : MetadataImageResult;

    /// <summary>The explicitly selected metadata root is absent.</summary>
    public sealed record MissingRoot(MetadataRootKind Root) : MetadataImageResult;

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

    public static MetadataImageResult Execute(
        AssemblyInspectionSession session,
        MetadataRootKind root = MetadataRootKind.Cli)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            if (root == MetadataRootKind.ReadyToRunManifest)
            {
                MetadataRootInspection? selected = session.MetadataRoot(root);
                return selected is null
                    ? new MetadataImageResult.MissingRoot(root)
                    : new MetadataImageResult.Available(selected.Image(), selected);
            }

            MetadataImageOverview? overview = session.MetadataImage();
            if (overview is null)
                return new MetadataImageResult.NoMetadata();

            MetadataRootInspection? selectedRoot =
                overview.Headers.Cor is null
                    ? null
                    : session.MetadataRoot(root);
            return new MetadataImageResult.Available(overview, selectedRoot);
        }
        catch (Exception ex)
        {
            return new MetadataImageResult.Failed(ex);
        }
    }
}
