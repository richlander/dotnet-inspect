using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal sealed class ApiSurfaceEndpoint : IDisposable
{
    readonly Lazy<ApiSurface> _surface;
    readonly AssemblySet? _assemblySet;

    internal ApiSurfaceEndpoint(AssemblySet assemblySet, ApiSurface surface)
    {
        _assemblySet = assemblySet;
        _surface = new(() => surface);
    }

    /// <summary>
    /// An endpoint opened as a package endpoint scope. Its surface is
    /// projected from the scope's participants on first use; it has no
    /// extracted file paths, so only API views read it. The scope's owner
    /// disposes it.
    /// </summary>
    internal ApiSurfaceEndpoint(
        DotnetInspector.PackageQueries.PackageEndpointScope packageScope,
        Func<ApiSurface> surface)
    {
        PackageScope = packageScope;
        _surface = new(surface);
    }

    internal ApiSurfaceEndpoint(
        AssemblySet assemblySet,
        bool includeAll,
        VerboseLogger logger)
    {
        _assemblySet = assemblySet;
        _surface = new(() =>
            AssemblySetSurfaceBuilder.Build(assemblySet, includeAll, logger.Log)
                ?? throw new InvalidOperationException(
                    "Failed to extract API surface."));
    }

    /// <summary>The package endpoint scope this endpoint reads, if any.</summary>
    public DotnetInspector.PackageQueries.PackageEndpointScope? PackageScope { get; }

    public AssemblySet AssemblySet =>
        _assemblySet
        ?? throw new InvalidOperationException(
            "A package endpoint scope has no extracted assembly set; only API views read it.");

    /// <summary>The number of Libraries in this endpoint's population.</summary>
    public int LibraryCount =>
        PackageScope?.SurfaceParticipants.Length
        ?? AssemblySet.Assemblies.Count;

    public ApiSurface Surface => _surface.Value;

    public IReadOnlyList<string> Paths =>
        AssemblySet.Assemblies.Select(static entry => entry.Path).ToList();

    public void Dispose() => _assemblySet?.Dispose();
}

internal static class ApiSurfaceEndpointResolver
{
    public static async Task<(ApiSurfaceEndpoint? Endpoint, string? Error, bool AssembliesResolved)> ResolveAsync(
        HttpClient httpClient,
        AssemblySetRequest request,
        bool includeAll,
        VerboseLogger logger,
        bool deferSurfaceProjection = false)
    {
        var assemblySet = await AssemblySetResolver
            .CollectAsync(httpClient, request, logger.Log)
            .ConfigureAwait(false);
        return Resolve(
            assemblySet, includeAll, logger, deferSurfaceProjection);
    }

    public static (ApiSurfaceEndpoint? Endpoint, string? Error, bool AssembliesResolved) Resolve(
        PackageExtractionResult extracted,
        string? tfm,
        bool includeAll,
        VerboseLogger logger)
        => Resolve(
            AssemblySetResolver.CollectExtractedPackage(
                extracted, tfm, includeRuntimeAssemblies: true),
            includeAll, logger);

    private static (ApiSurfaceEndpoint? Endpoint, string? Error, bool AssembliesResolved) Resolve(
        AssemblySet assemblySet,
        bool includeAll,
        VerboseLogger logger,
        bool deferSurfaceProjection = false)
    {
        try
        {
            if (assemblySet.Assemblies.Count == 0)
            {
                string error = assemblySet.Diagnostics.Count > 0
                    ? string.Join("; ", assemblySet.Diagnostics.Select(static diagnostic => diagnostic.Message))
                    : "No assemblies were resolved.";
                assemblySet.Dispose();
                return (null, error, false);
            }

            AssemblySetDiagnosticWriter.Write(assemblySet);
            if (deferSurfaceProjection)
            {
                return (
                    new ApiSurfaceEndpoint(assemblySet, includeAll, logger),
                    null,
                    true);
            }
            var surface = AssemblySetSurfaceBuilder.Build(assemblySet, includeAll, logger.Log);
            if (surface is null)
            {
                assemblySet.Dispose();
                return (null, "Failed to extract API surface.", true);
            }

            return (new ApiSurfaceEndpoint(assemblySet, surface), null, true);
        }
        catch
        {
            assemblySet.Dispose();
            throw;
        }
    }
}
