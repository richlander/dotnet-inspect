using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class SourceFileCollector
{
    public static async Task<List<SourceFileInfo>> CollectAsync(
        SourceLinkService service,
        string assemblyPath,
        VerboseLogger logger,
        HttpClient httpClient,
        bool includeAll = false,
        bool browsableUrls = false,
        string? typeFilter = null)
    {
        if (!service.HasPdb || !service.HasSourceLink)
            return [];

        var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll, typesOnly: true);
        if (api == null)
            return [];

        List<SourceFileInfo> rows = [];
        Dictionary<string, SourceLinkService> implementationServices = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var type in api.Types.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var typeDisplayName = MetadataTypeNameFormatter.FormatFullName(type);
                if (!string.IsNullOrWhiteSpace(typeFilter)
                    && !TypeMatcher.MatchesTypeFilter(type.FullName, typeFilter)
                    && !TypeMatcher.MatchesTypeFilter(typeDisplayName, typeFilter))
                    continue;

                var sourceInfo = service.ResolveTypeSource(type.FullName);
                if (sourceInfo == null)
                    sourceInfo = await ResolveForwardedTypeSourceAsync(type.FullName, service, implementationServices, httpClient, logger);

                if (sourceInfo == null)
                {
                    rows.Add(new SourceFileInfo(typeDisplayName, null));
                    continue;
                }

                rows.Add(new SourceFileInfo(typeDisplayName, SelectUrl(sourceInfo, browsableUrls)));

                foreach (var partial in sourceInfo.AdditionalSourceFiles)
                    rows.Add(new SourceFileInfo(typeDisplayName, SelectUrl(partial, browsableUrls)));
            }
        }
        finally
        {
            foreach (var implementationService in implementationServices.Values)
                implementationService.Dispose();
        }

        return rows;
    }

    public static async Task<List<SourceFileInfo>> CollectFromAssemblyAsync(
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        VerboseLogger logger,
        HttpClient httpClient,
        bool includeAll = false,
        bool browsableUrls = false,
        string? typeFilter = null)
    {
        using var service = SourceLinkService.Open(assemblyPath, logger.Log);
        await SourceEnricher.AcquirePdbAsync(
            service.Context,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            logger.Log);
        return await CollectAsync(service, assemblyPath, logger, httpClient, includeAll, browsableUrls, typeFilter);
    }

    private static async Task<SourceLinkResolver.TypeSourceInfo?> ResolveForwardedTypeSourceAsync(
        string typeName,
        SourceLinkService service,
        Dictionary<string, SourceLinkService> implementationServices,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var implementationPath = service.ResolveImplementationAssemblyPath(typeName);
        if (implementationPath == null)
            return null;

        if (!implementationServices.TryGetValue(implementationPath, out var implementationService))
        {
            implementationService = SourceLinkService.Open(implementationPath, logger.Log);
            await SourceEnricher.AcquirePdbAsync(
                implementationService.Context,
                httpClient,
                packageName: null,
                packageVersion: null,
                isPlatformAssembly: true,
                logger.Log);
            implementationServices[implementationPath] = implementationService;
        }

        return implementationService.HasPdb && implementationService.HasSourceLink
            ? implementationService.ResolveTypeSource(typeName)
            : null;
    }

    private static string? SelectUrl(SourceLinkResolver.TypeSourceInfo info, bool browsableUrls)
        => SelectUrl(info.GitHubBrowseUrl, info.SourceUrl, browsableUrls);

    private static string? SelectUrl(SourceLinkResolver.PartialSourceFile info, bool browsableUrls)
        => SelectUrl(info.GitHubBrowseUrl, info.SourceUrl, browsableUrls);

    private static string? SelectUrl(string? browseUrl, string? rawUrl, bool browsableUrls)
    {
        if (!browsableUrls)
            return rawUrl;

        var url = browseUrl ?? rawUrl;
        return url == null ? null : GitHubUrlResolver.ConvertRawToBlobUrl(url);
    }
}
