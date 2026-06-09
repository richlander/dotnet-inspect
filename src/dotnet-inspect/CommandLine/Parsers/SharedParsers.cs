using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Services;

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

    public static async Task<SourceSelection> ResolveSourceSelectionAsync(
        SourceSelectionInputs inputs,
        bool verbose,
        bool tryQualifiedTypeName)
    {
        var source = await SourceResolver.ResolveAsync(
            inputs.Args, inputs.ExplicitPackage, inputs.ExplicitAssembly, inputs.ExplicitPlatform,
            verbose, tryQualifiedTypeName).ConfigureAwait(false);

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
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot <= 0)
            return (typeName, null);

        var rightPart = typeName[(lastDot + 1)..];
        return rightPart.Contains('<')
            ? (typeName, null)
            : (typeName[..lastDot], rightPart);
    }

    internal static (SourceResolver.LocalProbeResult Probe, string MemberName)? TrySplitQualifiedTypeMember(
        string value,
        bool allowPlatformPrefixFallback)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            if (value[i] != '.')
                continue;

            var typeCandidate = value[..i];
            var memberName = value[(i + 1)..];
            if (string.IsNullOrWhiteSpace(memberName) || memberName.Contains('<'))
                continue;

            var probe = SourceResolver.TryResolveQualifiedTypeName(typeCandidate, allowPlatformPrefixFallback);
            if (probe != null)
                return (probe, memberName);
        }

        return null;
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

        return (new HashSet<string>(values, StringComparer.OrdinalIgnoreCase), null);
    }

    /// <summary>
    /// Parses Name:N shorthand for overload selection.
    /// </summary>
    /// <param name="value">The member name, possibly with :N suffix.</param>
    /// <returns>A tuple of (name without suffix, index if present).</returns>
    public static (string Name, int? Index) ParseOverloadShorthand(string value)
    {
        var colonIdx = value.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(value[(colonIdx + 1)..], out var idx))
            return (value[..colonIdx], idx);
        return (value, null);
    }

    /// <summary>
    /// Parses Type.Member dotted syntax.
    /// Only splits if there's a dot, no glob patterns, and first segment isn't empty.
    /// </summary>
    /// <param name="value">The member argument, possibly with Type. prefix.</param>
    /// <returns>A tuple of (type filter if present, member name).</returns>
    public static (string? TypeFilter, string MemberName) ParseDottedMember(string value)
    {
        var dotIdx = value.LastIndexOf('.');
        if (dotIdx > 0 && !value.Contains('*') && !value.Contains('?'))
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

    /// <summary>
    /// Parses package@version syntax into separate components.
    /// Handles special @latest marker.
    /// </summary>
    /// <param name="name">The package name, possibly with @version suffix.</param>
    /// <returns>Parsed package info with bare name, version, and flags.</returns>
    public static PackageVersionInfo ParsePackageVersion(string name)
    {
        bool hasExplicitVersion = name.Contains('@');
        if (!hasExplicitVersion)
            return new(name, null, HasExplicitVersion: false, ForceLatest: false);

        var bareName = name[..name.IndexOf('@')];
        var explicitVersion = name[(name.IndexOf('@') + 1)..];

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
    /// <returns>Extracted type filter and overload index if found.</returns>
    public static (string? DottedTypeFilter, int? OverloadIndex, HashSet<string> KindFilter) ProcessMemberArguments(string[] members)
    {
        string? dottedTypeFilter = null;
        int? overloadIndex = null;
        HashSet<string> kindFilter = [];

        for (int i = 0; i < members.Length; i++)
        {
            var kindQualified = TryParseKindQualifiedMember(members[i], out var kind, out var memberName);
            if (kindQualified)
            {
                kindFilter.Add(kind);
                members[i] = memberName;
            }

            // Check for dotted syntax (Type.Member)
            if (!kindQualified)
            {
                var (typeFilter, dottedMemberName) = ParseDottedMember(members[i]);
                if (typeFilter != null)
                {
                    dottedTypeFilter = typeFilter;
                    members[i] = dottedMemberName;
                }
            }

            // Check for overload shorthand (Name:N)
            var (name, index) = ParseOverloadShorthand(members[i]);
            if (index != null)
            {
                members[i] = name;
                overloadIndex = index;
            }
        }

        return (dottedTypeFilter, overloadIndex, kindFilter);
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

    /// <summary>
    /// Parses comma-separated parameter types.
    /// </summary>
    /// <param name="value">The --params option value.</param>
    /// <returns>Array of parameter types, or null if empty.</returns>
    public static string[]? ParseParamTypes(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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
