using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class SourceFileCollector
{
    public static Task<List<SourceFileInfo>> CollectAsync(
        SourceLinkService service,
        string assemblyPath,
        bool includeAll = false,
        bool browsableUrls = false,
        string? typeFilter = null)
    {
        if (!service.HasPdb || !service.HasSourceLink)
            return Task.FromResult<List<SourceFileInfo>>([]);

        var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll, typesOnly: true);
        if (api == null)
            return Task.FromResult<List<SourceFileInfo>>([]);

        List<SourceFileInfo> rows = [];
        foreach (var type in api.Types.OrderBy(
            t => t.FullName,
            StringComparer.Ordinal))
        {
            var typeDisplayName =
                MetadataTypeNameFormatter.FormatFullName(type);
            if (!string.IsNullOrWhiteSpace(typeFilter)
                && !TypeMatcher.MatchesTypeFilter(type.FullName, typeFilter)
                && !TypeMatcher.MatchesTypeFilter(typeDisplayName, typeFilter))
            {
                continue;
            }

            var sourceInfo = service.ResolveTypeSource(type.FullName);
            if (sourceInfo == null)
            {
                rows.Add(new SourceFileInfo(typeDisplayName, null));
                continue;
            }

            rows.Add(new SourceFileInfo(
                typeDisplayName,
                SelectUrl(sourceInfo, browsableUrls)));

            foreach (var partial in sourceInfo.AdditionalSourceFiles)
            {
                rows.Add(new SourceFileInfo(
                    typeDisplayName,
                    SelectUrl(partial, browsableUrls)));
            }
        }

        return Task.FromResult(rows);
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
        return await CollectAsync(
            service,
            assemblyPath,
            includeAll,
            browsableUrls,
            typeFilter);
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
