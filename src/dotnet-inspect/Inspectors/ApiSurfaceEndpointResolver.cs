using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record ApiSurfaceEndpoint(
    AssemblySet AssemblySet,
    ApiSurface Surface) : IDisposable
{
    public IReadOnlyList<string> Paths =>
        AssemblySet.Assemblies.Select(static entry => entry.Path).ToList();

    public IReadOnlyList<ResolvedAssemblyReference> AssemblyReferences
    {
        get;
        init;
    } = [];

    public IReadOnlyList<AssemblySetAcquisitionFailure>
        AcquisitionFailures
    {
        get;
        init;
    } = [];

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
            IReadOnlyList<AssemblySetEntry> entries =
                assemblySet.Assemblies;
            string? packageName = entries.Count > 0
                && entries.All(static entry =>
                    entry.SourceKind
                        == AssemblySetSourceKind.Package)
                && entries.All(entry => string.Equals(
                    entry.Source,
                    entries[0].Source,
                    StringComparison.Ordinal))
                    ? entries[0].Source
                    : null;
            List<string?> tfms = entries
                .Select(static entry => entry.Tfm)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? tfm = tfms.Count == 1 ? tfms[0] : null;
            using var resolution =
                new AssemblySetResolutionSession(
                    assemblySet,
                    logger.Log);
            ApiSurface? surface = resolution.BuildApiSurface(
                includeAll,
                packageName,
                tfm,
                logger.Log);
            if (surface is null)
            {
                assemblySet.Dispose();
                return (null, "Failed to extract API surface.", true);
            }

            return (
                new ApiSurfaceEndpoint(assemblySet, surface)
                {
                    AssemblyReferences =
                        resolution.AssemblyReferences,
                    AcquisitionFailures =
                        resolution.AcquisitionFailures,
                },
                null,
                true);
        }
        catch
        {
            assemblySet.Dispose();
            throw;
        }
    }

}
