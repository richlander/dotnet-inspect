using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record ApiSurfaceEndpoint(
    AssemblySet AssemblySet,
    ApiSurface Surface) : IDisposable
{
    public IReadOnlyList<string> Paths =>
        AssemblySet.Assemblies.Select(static entry => entry.Path).ToList();

    public void Dispose() => AssemblySet.Dispose();
}

internal static class ApiSurfaceEndpointResolver
{
    public static async Task<(ApiSurfaceEndpoint? Endpoint, string? Error, bool AssembliesResolved)> ResolveAsync(
        HttpClient httpClient,
        AssemblySetRequest request,
        bool includeAll,
        VerboseLogger logger)
    {
        var assemblySet = await AssemblySetResolver
            .CollectAsync(httpClient, request, logger.Log)
            .ConfigureAwait(false);
        return Resolve(assemblySet, includeAll, logger);
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
        AssemblySet assemblySet, bool includeAll, VerboseLogger logger)
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
