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

    public TypeOptions ApplyTo(TypeOptions options)
    {
        var match = Match ?? throw new InvalidOperationException("Cannot apply a non-found type route.");
        return options with
        {
            TypeName = match.FullName,
            PackagePath = null,
            PlatformAssembly = match.Library,
            PlatformFramework = match.Source,
            OriginalTypeQuery = match.FullName,
            PlatformPrefixQuery = null,
            AllowPlatformPrefixFallback = false
        };
    }

    public MemberOptions ApplyTo(MemberOptions options)
    {
        var match = Match ?? throw new InvalidOperationException("Cannot apply a non-found type route.");
        return options with
        {
            TypeName = match.FullName,
            PackagePath = null,
            PlatformAssembly = match.Library,
            PlatformFramework = match.Source
        };
    }

    public SourceOptions ApplyTo(SourceOptions options)
    {
        var match = Match ?? throw new InvalidOperationException("Cannot apply a non-found type route.");
        return options with
        {
            TypeName = match.FullName,
            PackagePath = null,
            PlatformAssembly = match.Library,
            PlatformFramework = match.Source
        };
    }

    public int WriteAmbiguousError()
    {
        Console.Error.WriteLine($"Error: Type '{Query}' matched multiple platform types. Use `find {Query} --platform` to choose a source library.");
        return 1;
    }
}

internal sealed record TypeMemberFindIfMissResult(
    TypeFindIfMissStatus Status,
    string Query,
    string TypeQuery,
    string MemberName,
    int? OverloadIndex,
    TypeFindIfMissResult TypeResolution)
{
    public static TypeMemberFindIfMissResult None(string query) =>
        new(TypeFindIfMissStatus.None, query, "", "", null, TypeFindIfMissResult.None(query));

    public static TypeMemberFindIfMissResult FromTypeResolution(
        string query,
        string typeQuery,
        string memberName,
        int? overloadIndex,
        TypeFindIfMissResult typeResolution) =>
        new(typeResolution.Status, query, typeQuery, memberName, overloadIndex, typeResolution);

    public MemberOptions ApplyTo(MemberOptions options)
    {
        var applied = TypeResolution.ApplyTo(options);
        return applied with
        {
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MemberName },
            OverloadIndex = OverloadIndex
        };
    }

    public SourceOptions ApplyTo(SourceOptions options)
    {
        var applied = TypeResolution.ApplyTo(options);
        return applied with
        {
            MemberName = MemberName,
            OverloadIndex = OverloadIndex
        };
    }

    public int WriteAmbiguousError() => TypeResolution.WriteAmbiguousError();
}

internal static class TypeFindIfMissResolver
{
    public static bool LooksLikeSimpleTypeQuery(string? query)
        => query is { Length: > 0 }
           && char.IsUpper(query[0])
           && !query.Contains('*')
           && !query.Contains('?')
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
            var results = await TypeSearchService.CollectTypesAsync(
                findOptions,
                query,
                logger,
                tempDirs,
                httpClient);

            var exactMatches = results
                .Select(r => new TypeFindResult
                {
                    Pattern = query!,
                    Type = r.TypeName,
                    Namespace = r.Namespace ?? "",
                    FullName = r.FullName,
                    Kind = r.Kind,
                    Library = r.Assembly ?? "",
                    Source = r.Source ?? "",
                    SourceVersion = r.SourceVersion,
                    Match = MatchKind.Exact
                })
                .DistinctBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedQuery = TypeMatcher.Normalize(query!);
            var exactDisplayNameMatches = exactMatches
                .Where(r => string.Equals(TypeMatcher.Normalize(r.Type), normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var exactSimpleNameMatches = exactMatches
                .Where(r => string.Equals(TypeMatcher.GetSimpleName(r.FullName), query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var candidateMatches = exactDisplayNameMatches.Count > 0 ? exactDisplayNameMatches
                : exactSimpleNameMatches.Count > 0 ? exactSimpleNameMatches
                : exactMatches;

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

    public static async Task<TypeMemberFindIfMissResult> ResolvePlatformMemberAsync(
        string? query,
        bool includeAll,
        NuGetSourceOptions? sourceOptions,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!TrySplitMemberQuery(query, out var typeQuery, out var memberSelector))
            return TypeMemberFindIfMissResult.None(query ?? "");

        var (memberName, overloadIndex) = ParseOverloadShorthand(memberSelector);
        var typeResolution = await ResolvePlatformAsync(typeQuery, includeAll, sourceOptions, httpClient, logger);
        return TypeMemberFindIfMissResult.FromTypeResolution(
            query!, typeQuery, memberName, overloadIndex, typeResolution);
    }

    private static bool TrySplitMemberQuery(string? query, out string typeQuery, out string memberSelector)
    {
        typeQuery = "";
        memberSelector = "";

        if (string.IsNullOrWhiteSpace(query) || query.Contains('*') || query.Contains('?')
            || query.Contains('@') || query.Contains('/') || query.Contains('\\'))
            return false;

        var lastDot = query.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == query.Length - 1)
            return false;

        typeQuery = query[..lastDot];
        memberSelector = query[(lastDot + 1)..];
        return true;
    }

    private static (string Name, int? Index) ParseOverloadShorthand(string value)
    {
        var colonIdx = value.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < value.Length - 1 &&
            int.TryParse(value[(colonIdx + 1)..], out var idx) && idx > 0)
        {
            return (TypeMatcher.NormalizeMemberName(value[..colonIdx]), idx);
        }

        return (TypeMatcher.NormalizeMemberName(value), null);
    }
}
