using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using DotnetInspect.Cli.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal static class SourceFileCollector
{
    public static Task<List<SourceFileInfo>> CollectAsync(
        SourceLinkService service,
        string assemblyPath,
        bool includeAll = false,
        bool preferRenderedUrls = false,
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

            SourceLinkResolver.TypeSourceDocument? defaultDocument =
                TypeSourceDocumentSelection.SelectDefault(sourceInfo);
            if (defaultDocument is null)
            {
                rows.Add(new SourceFileInfo(typeDisplayName, null));
                continue;
            }

            rows.Add(new SourceFileInfo(
                typeDisplayName,
                SelectUrl(defaultDocument, preferRenderedUrls)));

            foreach (SourceLinkResolver.TypeSourceDocument document
                in sourceInfo.Documents)
            {
                if (ReferenceEquals(document, defaultDocument))
                    continue;

                rows.Add(new SourceFileInfo(
                    typeDisplayName,
                    SelectUrl(document, preferRenderedUrls)));
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
        bool preferRenderedUrls = false,
        string? typeFilter = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        using var service = SourceLinkService.Open(assemblyPath, logger.Log);
        await SourceEnricher.AcquirePdbAsync(
            service.Context,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            logger.Log,
            sourceOptions: sourceOptions);
        return await CollectAsync(
            service,
            assemblyPath,
            includeAll,
            preferRenderedUrls,
            typeFilter);
    }

    private static string? SelectUrl(
        SourceLinkResolver.TypeSourceDocument info,
        bool preferRenderedUrls)
        => SelectUrl(info.GitHubBrowseUrl, info.SourceUrl, preferRenderedUrls);

    private static string? SelectUrl(string? browseUrl, string? rawUrl, bool preferRenderedUrls)
    {
        if (!preferRenderedUrls)
            return rawUrl;

        var url = browseUrl ?? rawUrl;
        return url == null ? null : GitHubUrlResolver.ConvertRawToBlobUrl(url);
    }
}
