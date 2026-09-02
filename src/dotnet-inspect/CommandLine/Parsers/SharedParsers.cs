using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Shared parsing helpers for command option transformations.
/// These methods extract common patterns from command SetAction lambdas
/// to enable unit testing and reduce duplication.
/// </summary>
public static class SharedParsers
{
    public sealed record SourceSelection(
        string[] Args,
        string? ExplicitPackage,
        string? ExplicitAssembly,
        string? ExplicitPlatform,
        bool IsLibrarySelector,
        bool HasExplicitSource,
        SourceResolver.ResolvedSource Source);

    public sealed record SourceSelectionInputs(
        string[] Args,
        string? ExplicitPackage,
        string? ExplicitAssembly,
        string? ExplicitPlatform,
        bool IsLibrarySelector,
        bool HasExplicitSource);

    public static SourceSelectionInputs ReadSourceSelectionInputs(
        ParseResult parseResult,
        Argument<string[]> argsArg,
        Option<string?> packageOption,
        Option<string?> assemblyOption,
        Option<string?> platformOption)
    {
        var argsValue = parseResult.GetValue(argsArg) ?? [];
        var explicitPackage = parseResult.GetValue(packageOption);
        var explicitAssembly = parseResult.GetValue(assemblyOption);
        var explicitPlatform = parseResult.GetValue(platformOption);
        bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
        bool hasExplicitSource = SourceResolver.HasExplicitSource(
            explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

        return new SourceSelectionInputs(
            argsValue,
            explicitPackage,
            explicitAssembly,
            explicitPlatform,
            isLibrarySelector,
            hasExplicitSource);
    }

    public static OptionError? GetStructuralPositionalVersionError(
        SourceSelectionInputs inputs,
        bool hasProjectSource)
    {
        if (!inputs.HasExplicitSource
            && !hasProjectSource
            && inputs.Args.Length >= 2
            && CommandLineHelpers.LooksLikeVersionNumber(
                inputs.Args[1]))
        {
            return new OptionError(
                $"'{inputs.Args[1]}' looks like a version number. "
                + $"Use '{inputs.Args[0]}@{inputs.Args[1]}' to specify a version.");
        }

        return null;
    }

    public static int GetStructuralTypeArgumentIndex(
        SourceSelectionInputs inputs,
        bool hasProjectSource)
    {
        if (inputs.HasExplicitSource || hasProjectSource)
            return 0;

        if (inputs.Args.Length == 0)
            return -1;

        if (CommandLineHelpers.TryClassifyAsFilePath(
                inputs.Args[0],
                out string? dllPath,
                out string? nupkgPath)
            && (dllPath is not null || nupkgPath is not null))
        {
            return 1;
        }

        return inputs.Args.Length >= 2 ? 1 : 0;
    }

    public static async Task<SourceSelection> ResolveSourceSelectionAsync(
        SourceSelectionInputs inputs,
        NuGetSourceOptions? sourceOptions,
        bool verbose,
        bool tryQualifiedTypeName)
    {
        var source = await SourceResolver.ResolveAsync(
            inputs.Args, inputs.ExplicitPackage, inputs.ExplicitAssembly, inputs.ExplicitPlatform,
            sourceOptions, verbose, tryQualifiedTypeName).ConfigureAwait(false);

        return new SourceSelection(
            inputs.Args,
            inputs.ExplicitPackage,
            inputs.ExplicitAssembly,
            inputs.ExplicitPlatform,
            inputs.IsLibrarySelector,
            inputs.HasExplicitSource,
            source);
    }

    public static (string TypeName, string? MemberName) SplitTrailingMember(string typeName)
    {
        var (digestHead, _) = ParseDigestShorthand(typeName);
        var (overloadHead, _) = ParseOverloadShorthand(digestHead);
        var suffixLength = typeName.Length - overloadHead.Length;
        foreach (var marker in
                 (ReadOnlySpan<string>)
                 [
                     "..cctor",
                     "..ctor",
                     ".operator",
                     ".op_",
                     ".explicit:",
                     ".extension:",
                 ])
        {
            int markerIndex = FindLastTopLevelMarker(
                overloadHead,
                marker);
            if (markerIndex > 0)
            {
                string memberName =
                    overloadHead[(markerIndex + 1)..]
                    + typeName[^suffixLength..];
                if ((marker is ".operator" or ".op_")
                    && !MemberTargetSelector.Parse(memberName)
                        .Name.StartsWith(
                            "op_",
                            StringComparison.Ordinal))
                {
                    continue;
                }

                return (
                    overloadHead[..markerIndex],
                    memberName);
            }
        }

        var lastDot = FqnParser.LastTopLevelDot(typeName);
        if (lastDot <= 0)
            return (typeName, null);

        var rightPart = typeName[(lastDot + 1)..];
        return rightPart.Contains('<') && !HasGenericMemberSelectorSuffix(rightPart)
            ? (typeName, null)
            : (typeName[..lastDot], rightPart);
    }

    private static int FindLastTopLevelMarker(
        string value,
        string marker)
    {
        var genericDepth = 0;
        var match = -1;
        for (var i = 0; i <= value.Length - marker.Length; i++)
        {
            if (genericDepth == 0
                && value.AsSpan(i).StartsWith(
                    marker,
                    StringComparison.OrdinalIgnoreCase))
            {
                match = i;
            }

            if (value[i] == '<')
                genericDepth++;
            else if (value[i] == '>' && genericDepth > 0)
                genericDepth--;
        }

        return match;
    }

    internal static (SourceResolver.LocalProbeResult Probe, string MemberName)? TrySplitQualifiedTypeMember(
        string value,
        IReadOnlyList<string> sourceKeys,
        bool allowPlatformPrefixFallback,
        Action<string>? reportPlatformLookupFailure = null)
        => TrySplitQualifiedTypeMember(
            value,
            (candidate, fallback, report) => SourceResolver.TryResolveQualifiedTypeName(
                candidate,
                sourceKeys,
                fallback,
                report),
            allowPlatformPrefixFallback,
            reportPlatformLookupFailure);

    internal static (SourceResolver.LocalProbeResult Probe, string MemberName)? TrySplitQualifiedTypeMember(
        string value,
        NuGetSourceOptions? sourceOptions,
        bool allowPlatformPrefixFallback,
        Action<string>? reportPlatformLookupFailure = null)
        => TrySplitQualifiedTypeMember(
            value,
            (candidate, fallback, report) => SourceResolver.TryResolveQualifiedTypeName(
                candidate,
                sourceOptions,
                fallback,
                report),
            allowPlatformPrefixFallback,
            reportPlatformLookupFailure);

    private static (SourceResolver.LocalProbeResult Probe, string MemberName)? TrySplitQualifiedTypeMember(
        string value,
        Func<string, bool, Action<string>?, SourceResolver.LocalProbeResult?> resolve,
        bool allowPlatformPrefixFallback,
        Action<string>? reportPlatformLookupFailure)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            if (value[i] != '.')
                continue;

            var typeCandidate = value[..i];
            var memberName = value[(i + 1)..];
            if (string.IsNullOrWhiteSpace(memberName))
                continue;

            string? platformLookupFailure = null;
            var probe = resolve(
                typeCandidate,
                allowPlatformPrefixFallback,
                message => platformLookupFailure = message);
            if (platformLookupFailure is not null)
            {
                reportPlatformLookupFailure?.Invoke(platformLookupFailure);
                return null;
            }
            if (probe != null)
                return (probe, memberName);

            // If qualified name resolution failed, try resolving the candidate as a bare
            // CoreLib type. This covers generic types ("List<T>", "Span`1") as well as
            // single-token simple/primitive type names ("Type", "Math", "string") that
            // qualified resolution does not recognize on their own.
            if (typeCandidate.Contains('<') || typeCandidate.Contains('`') || !typeCandidate.Contains('.'))
            {
                var bareProbe = SourceResolver.TryResolveBareCoreLibTypeName(typeCandidate);
                if (bareProbe != null)
                    return (bareProbe, memberName);
            }
        }

        return null;
    }

    private static bool HasGenericMemberSelectorSuffix(string value)
    {
        var selector = MemberTargetSelector.Parse(value);
        return selector.GenericArity.HasValue
            && (selector.OverloadIndex.HasValue || selector.DigestPrefix is { Length: > 0 });
    }

    /// <summary>
    /// Parses member filter values where a single numeric value means limit,
    /// otherwise values are treated as filter names.
    /// </summary>
    /// <param name="values">The member filter values from -m option.</param>
    /// <returns>A tuple of (filter set, limit). One will be empty/null.</returns>
    public static (HashSet<string> Filter, int? Limit) ParseMemberFilter(string[] values)
    {
        if (values.Length == 0)
            return ([], null);

        if (values.Length == 1 && int.TryParse(values[0], out var limit))
            return ([], limit);

        return (new HashSet<string>(values.Select(FqnParser.NormalizeMemberName), StringComparer.OrdinalIgnoreCase), null);
    }

    /// <summary>
    /// Parses Name:N shorthand for overload selection.
    /// </summary>
    /// <param name="value">The member name, possibly with :N suffix.</param>
    /// <returns>A tuple of (name without suffix, index if present).</returns>
    public static (string Name, int? Index) ParseOverloadShorthand(string value)
        => FqnParser.ParseMemberFilter(value);

    /// <summary>
    /// Parses Type.Member dotted syntax.
    /// Only splits if there's a dot, no glob patterns, and first segment isn't empty.
    /// </summary>
    /// <param name="value">The member argument, possibly with Type. prefix.</param>
    /// <returns>A tuple of (type filter if present, member name).</returns>
    public static (string? TypeFilter, string MemberName) ParseDottedMember(string value)
    {
        var dotIdx = FqnParser.LastTopLevelDot(value);
        if (dotIdx > 0 && !TypeMatcher.IsTypeGlobPattern(value))
            return (value[..dotIdx], value[(dotIdx + 1)..]);
        return (null, value);
    }

    /// <summary>
    /// Parses type filter value where a numeric value means limit,
    /// otherwise the value is a glob pattern.
    /// </summary>
    /// <param name="value">The -t option value.</param>
    /// <returns>A tuple of (filter pattern if not numeric, limit if numeric).</returns>
    public static (string? Filter, int? Limit) ParseTypeFilter(string? value)
    {
        if (value == null)
            return (null, null);

        if (int.TryParse(value, out var limit))
            return (null, limit);

        return (value, null);
    }

    public static OptionError? ParseAnalysisQueryOptions(
        ParseResult parseResult,
        SharedOptions options,
        bool typeScoped,
        string? typeName,
        out BodyKindQueryOptions bodyKindQuery,
        out PerformanceTriageOptions performanceTriage)
    {
        string[] whereExpressions =
            parseResult.GetValue(options.RowWhere) ?? [];
        if (!BodyKindQueryOptions.TryExtract(
                whereExpressions,
                out bodyKindQuery,
                out string[] performanceWhere,
                out OptionError bodyKindError))
        {
            performanceTriage = new PerformanceTriageOptions();
            return bodyKindError;
        }

        if (typeScoped
            && bodyKindQuery.HasFilter
            && (string.IsNullOrWhiteSpace(typeName)
                || typeName.Contains('*')
                || typeName.Contains('?')))
        {
            performanceTriage = new PerformanceTriageOptions();
            return new OptionError(
                "A type-scoped Body Shapes query requires one exact type name.");
        }

        performanceTriage =
            options.ParsePerformanceTriageOptions(
                parseResult,
                performanceWhere);
        if (!PerformanceTriageOptions.TryValidate(
                performanceTriage,
                out OptionError triageShapeError))
        {
            return triageShapeError;
        }

        if (bodyKindQuery.HasFilter
            && performanceTriage.HasFilters)
        {
            return new OptionError(
                typeScoped
                    ? "A Body Shapes predicate cannot yet be combined with Performance Triage filters or --order-by in one type query."
                    : "A Body Shapes predicate cannot yet be combined with Performance Triage filters or --order-by in one query.");
        }

        return null;
    }

    /// <summary>
    /// Parses package@version syntax into separate components.
    /// Handles special @latest marker.
    /// </summary>
    /// <param name="name">The package name, possibly with @version suffix.</param>
    /// <returns>Parsed package info with bare name, version, and flags.</returns>
    public static PackageVersionInfo ParsePackageVersion(string name)
    {
        var (bareName, explicitVersion) = PackageExtractor.ParsePackageReference(name);
        if (explicitVersion == null)
            return new(bareName, null, HasExplicitVersion: false, ForceLatest: false);

        bool forceLatest = string.Equals(explicitVersion, "latest", StringComparison.OrdinalIgnoreCase);
        if (forceLatest)
            return new(bareName, null, HasExplicitVersion: false, ForceLatest: true);

        return new(bareName, explicitVersion, HasExplicitVersion: true, ForceLatest: false);
    }

    /// <summary>
    /// Processes member arguments to extract dotted syntax and overload shorthand.
    /// Modifies the array in place and returns extracted metadata.
    /// </summary>
    /// <param name="members">The member arguments to process.</param>
    /// <param name="inferDottedTypeFilter">
    /// Whether a qualified member may supply a missing type target.
    /// </param>
    /// <param name="suppliedTypeName">
    /// The explicit type target, used to distinguish its qualifier from an
    /// explicit-interface member name.
    /// </param>
    /// <returns>Extracted type filter and overload index if found.</returns>
    public static (string? DottedTypeFilter, int? OverloadIndex, string? MemberDigest, int? GenericArity, bool GenericArityConflict, HashSet<string> KindFilter) ProcessMemberArguments(
        string[] members,
        bool inferDottedTypeFilter = true,
        string? suppliedTypeName = null)
    {
        string? dottedTypeFilter = null;
        int? overloadIndex = null;
        string? memberDigest = null;
        int? genericArity = null;
        bool genericArityConflict = false;
        HashSet<string> kindFilter = [];

        for (int i = 0; i < members.Length; i++)
        {
            var kindQualified = TryParseKindQualifiedMember(members[i], out _, out _);

            // Check for dotted syntax (Type.Member)
            if (!kindQualified)
            {
                var (typeFilter, dottedMemberName) = ParseDottedMember(members[i]);
                if (typeFilter != null
                    && (inferDottedTypeFilter
                        || FqnParser.LastTopLevelDot(typeFilter) < 0
                        || MatchesSuppliedTypeQualifier(
                            typeFilter,
                            suppliedTypeName)))
                {
                    if (inferDottedTypeFilter)
                        dottedTypeFilter = typeFilter;
                    members[i] = dottedMemberName;
                }
            }

            var selector = MemberTargetSelector.Parse(members[i]);
            if (selector.Kind is { Length: > 0 })
                kindFilter.Add(selector.Kind);
            if (selector.DigestPrefix is { Length: > 0 })
                memberDigest = selector.DigestPrefix;
            if (selector.GenericArity is { } arity)
            {
                if (genericArity is { } existingArity && existingArity != arity)
                    genericArityConflict = true;
                else
                    genericArity = arity;
            }
            if (selector.OverloadIndex is { } index)
                overloadIndex = index;
            members[i] = selector.Name;
        }

        return (dottedTypeFilter, overloadIndex, memberDigest, genericArity, genericArityConflict, kindFilter);
    }

    private static bool MatchesSuppliedTypeQualifier(
        string qualifier,
        string? suppliedTypeName)
    {
        if (string.IsNullOrWhiteSpace(suppliedTypeName))
            return false;

        return TypeMatcher.MatchesTypeFilter(
            suppliedTypeName,
            qualifier);
    }

    public static string StripResolvedTypeQualifier(
        string member,
        string resolvedTypeName)
    {
        var (typeFilter, dottedMemberName) =
            ParseDottedMember(member);
        return typeFilter is not null
            && MatchesSuppliedTypeQualifier(
                typeFilter,
                resolvedTypeName)
            ? dottedMemberName
            : member;
    }

    public static (string Name, string? Digest) ParseDigestShorthand(string value)
    {
        var tilde = value.LastIndexOf('~');
        if (tilde <= 0 || tilde == value.Length - 1)
            return (value, null);

        var digest = value[(tilde + 1)..];
        if (!digest.All(Uri.IsHexDigit))
            return (value, null);

        return (value[..tilde], digest.ToLowerInvariant());
    }

    private static bool TryParseKindQualifiedMember(string value, out string kind, out string memberName)
    {
        foreach (var prefix in (ReadOnlySpan<string>)["operator:", "explicit:", "extension:"])
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                kind = NormalizeKind(prefix[..^1]);
                memberName = value[prefix.Length..];
                return true;
            }
        }

        kind = "";
        memberName = value;
        return false;
    }

    /// <summary>
    /// Normalizes a kind value, accepting abbreviations.
    /// </summary>
    public static string NormalizeKind(string value) => value.ToLowerInvariant() switch
    {
        "prop" or "props" or "property" or "properties" => "property",
        "ctor" or "ctors" or "constructor" or "constructors" => "constructor",
        "finalizer" or "finalizers" or "destructor" or "destructors" => "finalizer",
        "method" or "methods" => "method",
        "op" or "ops" or "operator" or "operators" => "operator",
        "explicit" or "explicit-interface" or "explicit-interface-implementation" or "explicit-interface-implementations" => "explicit-interface-implementation",
        "extension" or "extensions" or "extension-method" or "extension-methods" => "extension-method",
        "field" or "fields" => "field",
        "event" or "events" => "event",
        "class" or "classes" => "class",
        "struct" or "structs" => "struct",
        "interface" or "interfaces" => "interface",
        "enum" or "enums" => "enum",
        "delegate" or "delegates" => "delegate",
        _ => value.ToLowerInvariant()
    };

    /// <summary>
    /// Parses kind filter values into a normalized set.
    /// </summary>
    public static HashSet<string> ParseKindFilter(string[] values)
    {
        if (values.Length == 0)
            return [];

        return new HashSet<string>(values.Select(NormalizeKind), StringComparer.OrdinalIgnoreCase);
    }

}

/// <summary>
/// Parsed package version information.
/// </summary>
/// <param name="BareName">The package name without version.</param>
/// <param name="ExplicitVersion">The explicit version if specified (not @latest).</param>
/// <param name="HasExplicitVersion">True if a specific version was provided.</param>
/// <param name="ForceLatest">True if @latest was specified.</param>
public readonly record struct PackageVersionInfo(
    string BareName,
    string? ExplicitVersion,
    bool HasExplicitVersion,
    bool ForceLatest);
