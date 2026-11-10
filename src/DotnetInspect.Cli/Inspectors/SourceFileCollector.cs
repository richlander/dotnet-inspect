using DotnetInspect.Cli.Models;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
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

    private static string? SelectUrl(
        SourceLinkResolver.TypeSourceDocument info,
        bool preferRenderedUrls)
        => preferRenderedUrls && info.SourceUrl is { } url
            ? GitHubUrlResolver.ConvertRawToBlobUrl(url)
            : info.SourceUrl;
}
