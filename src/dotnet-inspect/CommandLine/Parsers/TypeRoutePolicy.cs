using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

internal readonly record struct TypeRoutePolicyResult(
    string? OriginalTypeQuery,
    string? PlatformPrefixQuery,
    bool AllowPlatformPrefixFallback)
{
    public TypeOptions ApplyTo(TypeOptions options) => options with
    {
        OriginalTypeQuery = OriginalTypeQuery,
        PlatformPrefixQuery = PlatformPrefixQuery,
        AllowPlatformPrefixFallback = AllowPlatformPrefixFallback
    };
}

internal static class TypeRoutePolicy
{
    public static TypeRoutePolicyResult Resolve(
        string[] args,
        bool hasExplicitSource,
        SourceResolver.ResolvedSource source)
    {
        return new TypeRoutePolicyResult(
            GetOriginalTypeQuery(args, hasExplicitSource, source.TypeName),
            GetPlatformPrefixQuery(args, hasExplicitSource, source),
            IsImplicitPlatformPrefixQuery(args, hasExplicitSource));
    }

    public static TypeRoutePolicyResult ResolveImplicit(
        string query,
        SourceResolver.ResolvedSource source)
        => Resolve([query], hasExplicitSource: false, source);

    private static string? GetOriginalTypeQuery(string[] args, bool hasExplicitSource, string? resolvedTypeName)
    {
        if (hasExplicitSource)
            return args.FirstOrDefault();

        return args.Length switch
        {
            1 => args[0],
            >= 2 => args[1],
            _ => resolvedTypeName
        };
    }

    private static string? GetPlatformPrefixQuery(
        string[] args,
        bool hasExplicitSource,
        SourceResolver.ResolvedSource source)
    {
        if (hasExplicitSource || args.Length != 1)
            return null;

        var query = args[0];
        return source is { TypeName: null, PlatformAssembly: null, AssemblyPath: null }
               && source.PackagePath != null
               && IsImplicitPlatformPrefixQuery(args, hasExplicitSource)
            ? query
            : null;
    }

    private static bool IsImplicitPlatformPrefixQuery(string[] args, bool hasExplicitSource)
        => !hasExplicitSource
           && args.Length == 1
           && PlatformResolver.IsPlatformCandidate(args[0]);
}
