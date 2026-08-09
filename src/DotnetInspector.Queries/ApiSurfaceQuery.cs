using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Inputs for extracting one assembly's API surface.</summary>
public sealed record ApiSurfaceQueryContext(
    AssemblyInspectionSession Session,
    bool IncludeAll,
    bool TypesOnly = false);

/// <summary>Typed result of extracting one assembly's API surface.</summary>
public abstract record ApiSurfaceResult
{
    private ApiSurfaceResult()
    {
    }

    /// <summary>The extracted API surface.</summary>
    public sealed record Available(ApiSurface Surface) : ApiSurfaceResult;

    /// <summary>API extraction failed.</summary>
    public sealed record Failed(Exception Error) : ApiSurfaceResult;
}

/// <summary>Extracts an API surface from an already-open assembly session.</summary>
public static class ApiSurfaceQuery
{
    public static InspectionQuery<ApiSurfaceResult> Definition { get; } =
        new("API surface", InspectionCost.NetworkFree);

    public static ApiSurfaceResult Execute(ApiSurfaceQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return new ApiSurfaceResult.Available(
                context.Session.ApiSurface(context.IncludeAll, context.TypesOnly));
        }
        catch (Exception ex)
        {
            return new ApiSurfaceResult.Failed(ex);
        }
    }
}
