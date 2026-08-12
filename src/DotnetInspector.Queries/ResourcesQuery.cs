using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading an assembly's manifest resources.</summary>
public abstract record ResourcesResult
{
    private ResourcesResult()
    {
    }

    /// <summary>The manifest resources, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<ManifestResourceInfo> Resources) : ResourcesResult;

    /// <summary>The query failed while reading manifest resources.</summary>
    public sealed record Failed(Exception Error) : ResourcesResult;
}

/// <summary>Reads manifest resources from an already-open assembly session.</summary>
public static class ResourcesQuery
{
    public static InspectionQuery<ResourcesResult> Definition { get; } =
        new("Resources", InspectionCost.NetworkFree);

    public static ResourcesResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new ResourcesResult.Available(
                session.Resources().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new ResourcesResult.Failed(ex);
        }
    }
}
