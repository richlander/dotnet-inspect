using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal enum TypeFindIfMissStatus
{
    None,
    Found,
    Ambiguous
}

internal sealed record TypeFindIfMissResult(
    TypeFindIfMissStatus Status,
    string Query,
    TypeFindResult? Match,
    IReadOnlyList<TypeFindResult> Matches)
{
    public static TypeFindIfMissResult None(string query) => new(TypeFindIfMissStatus.None, query, null, []);
    public static TypeFindIfMissResult Found(string query, TypeFindResult match) => new(TypeFindIfMissStatus.Found, query, match, [match]);
    public static TypeFindIfMissResult Ambiguous(string query, IReadOnlyList<TypeFindResult> matches) => new(TypeFindIfMissStatus.Ambiguous, query, null, matches);
}

internal static class TypeFindIfMissResolver
{
    public static bool LooksLikeSimpleTypeQuery(string? query)
        => query is { Length: > 0 }
           && char.IsUpper(query[0])
           && !query.Contains('.')
           && !query.Contains('*')
           && !query.Contains('?')
           && !query.Contains('<')
           && !query.Contains('`')
           && !query.Contains('@')
           && !query.Contains('/')
           && !query.Contains('\\');

    public static async Task<TypeFindIfMissResult> ResolvePlatformAsync(
        string? query,
        bool includeAll,
        NuGetSourceOptions? sourceOptions,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!LooksLikeSimpleTypeQuery(query))
            return TypeFindIfMissResult.None(query ?? "");

        List<string> tempDirs = [];
        try
        {
            var findOptions = new FindOptions
            {
                Pattern = query!,
                PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames,
                IncludeAll = includeAll,
                SourceOptions = sourceOptions
            };
            var results = await TypeSearchService.FindTypesAsync(
                findOptions,
                [query!],
                logger,
                tempDirs,
                httpClient);

            var exactMatches = results
                .Where(r => r.Match == MatchKind.Exact)
                .DistinctBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exactSimpleNameMatches = exactMatches
                .Where(r => string.Equals(TypeMatcher.GetSimpleName(r.FullName), query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var candidateMatches = exactSimpleNameMatches.Count > 0 ? exactSimpleNameMatches : exactMatches;

            return candidateMatches.Count switch
            {
                0 => TypeFindIfMissResult.None(query!),
                1 => TypeFindIfMissResult.Found(query!, candidateMatches[0]),
                _ => TypeFindIfMissResult.Ambiguous(query!, candidateMatches)
            };
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }
}
