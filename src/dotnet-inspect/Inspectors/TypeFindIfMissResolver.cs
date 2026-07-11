using DotnetInspector.CommandLine;
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
    string MemberSelector,
    string MemberName,
    int? OverloadIndex,
    int? GenericArity,
    TypeFindIfMissResult TypeResolution)
{
    public static TypeMemberFindIfMissResult None(string query) =>
        new(TypeFindIfMissStatus.None, query, "", "", "", null, null, TypeFindIfMissResult.None(query));

    public static TypeMemberFindIfMissResult FromTypeResolution(
        string query,
        string typeQuery,
        string memberSelector,
        string memberName,
        int? overloadIndex,
        int? genericArity,
        TypeFindIfMissResult typeResolution) =>
        new(typeResolution.Status, query, typeQuery, memberSelector, memberName, overloadIndex, genericArity, typeResolution);

    public MemberOptions ApplyTo(MemberOptions options)
    {
        var applied = TypeResolution.ApplyTo(options);
        return applied with
        {
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MemberName },
            OverloadIndex = OverloadIndex,
            MemberGenericArity = GenericArity
        };
    }

    public int WriteAmbiguousError() => TypeResolution.WriteAmbiguousError();
}

internal static class TypeFindIfMissResolver
{
    public static bool LooksLikeSimpleTypeQuery(string? query)
        => query is { Length: > 0 }
           && (char.IsUpper(query[0]) || LooksLikePrimitiveKeyword(query))
           && !query.Contains('*')
           && !query.Contains('?')
           && !query.Contains('@')
           && !query.Contains('/')
           && !query.Contains('\\');

    private static bool LooksLikePrimitiveKeyword(string query) =>
        PrimitiveTypeNames.TryToClrFullName(query.Trim().ToLowerInvariant(), out _);

    public static async Task<TypeFindIfMissResult> ResolvePlatformAsync(
        string? query,
        bool includeAll,
        NuGetSourceOptions? sourceOptions,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!LooksLikeSimpleTypeQuery(query))
            return TypeFindIfMissResult.None(query ?? "");

        var normalizedQuery = TypeMatcher.Normalize(query!);
        var findOptions = new FindOptions
        {
            Pattern = normalizedQuery,
            PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames,
            IncludeAll = includeAll,
            SourceOptions = sourceOptions
        };
        var results = await TypeSearchService.CollectTypesAsync(
            findOptions,
            normalizedQuery,
            logger,
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

    public static async Task<TypeMemberFindIfMissResult> ResolvePlatformMemberAsync(
        string? query,
        bool includeAll,
        NuGetSourceOptions? sourceOptions,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!TrySplitMemberQuery(query, out var typeQuery, out var memberSelector))
            return TypeMemberFindIfMissResult.None(query ?? "");

        var selector = MemberTargetSelector.Parse(memberSelector);
        var typeResolution = await ResolvePlatformAsync(typeQuery, includeAll, sourceOptions, httpClient, logger);
        return TypeMemberFindIfMissResult.FromTypeResolution(
            query!, typeQuery, memberSelector, selector.Name, selector.OverloadIndex, selector.GenericArity, typeResolution);
    }

    private static bool TrySplitMemberQuery(string? query, out string typeQuery, out string memberSelector)
    {
        typeQuery = "";
        memberSelector = "";

        if (string.IsNullOrWhiteSpace(query) || query.Contains('*') || query.Contains('?')
            || query.Contains('@') || query.Contains('/') || query.Contains('\\'))
            return false;

        var lastDot = FqnParser.LastTopLevelDot(query);
        if (lastDot <= 0 || lastDot == query.Length - 1)
            return false;

        typeQuery = query[..lastDot];
        memberSelector = query[(lastDot + 1)..];
        var memberName = MemberTargetSelector.Parse(memberSelector).Name;
        if (typeQuery.EndsWith(".", StringComparison.Ordinal) &&
            memberName.Equals(".ctor", StringComparison.OrdinalIgnoreCase))
        {
            typeQuery = typeQuery.TrimEnd(['.']);
        }

        return true;
    }
}
