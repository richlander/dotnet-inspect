using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the find command options.
/// Extracts options and builds FindOptions for type search.
/// </summary>
public static class FindOptionsParser
{
    /// <summary>
    /// Arguments container for find command options.
    /// </summary>
    public record FindCommandArgs(
        Argument<string?> PatternArg,
        Option<string[]> PackageOption,
        Option<string[]> AssemblyOption,
        Option<bool> PlatformOption,
        Option<string[]> PlatformLibraryOption,
        Option<bool> ExtensionsOption,
        Option<bool> AspNetCoreOption,
        Option<string[]> ProjectOption,
        Option<string[]> BinOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> TypeFilterOption,
        Option<bool> CompactOption,
        Option<bool> NoHeaderOption,
        Option<string?> PackagePrefixOption,
        Option<bool> MembersOption);

    /// <summary>
    /// Result of parsing find command options.
    /// </summary>
    public abstract record FindParseResult;

    /// <summary>
    /// Indicates help with tips should be shown (no pattern provided).
    /// </summary>
    public record ShowHelpWithTips : FindParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(FindOptions Options, Verbosity Verbosity, TipLevel TipLevel) : FindParseResult;

    /// <summary>
    /// Parses find command options asynchronously (due to package prefix resolution).
    /// </summary>
    public static async Task<FindParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        FindCommandArgs args)
    {
        var pattern = parseResult.GetValue(args.PatternArg);
        var packagePrefix = parseResult.GetValue(args.PackagePrefixOption);
        bool packagePrefixSpecified =
            parseResult.GetResult(args.PackagePrefixOption)
                is { Implicit: false };

        if (string.IsNullOrEmpty(pattern)
            && !packagePrefixSpecified)
            return new ShowHelpWithTips();

        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        var typeFilter = parseResult.GetValue(args.TypeFilterOption);
        AssemblySetRequest sources;
        SearchSourceSelection? selection = null;
        bool profileHasGroupScope = false;
        if (string.IsNullOrEmpty(pattern))
        {
            // Profiles have their own grammar and reject API scopes before acquisition.
            profileHasGroupScope = parseResult.GetValue(args.PlatformOption)
                || parseResult.GetValue(args.ExtensionsOption)
                || parseResult.GetValue(args.AspNetCoreOption);
            sources = new()
            {
                Packages = parseResult.GetValue(args.PackageOption) ?? [],
                Assemblies = parseResult.GetValue(args.AssemblyOption) ?? [],
                PlatformAssemblies = parseResult.GetValue(args.PlatformLibraryOption) ?? [],
                Projects = parseResult.GetValue(args.ProjectOption) ?? [],
                Directories = parseResult.GetValue(args.BinOption) ?? [],
            };
        }
        else
        {
            var intent = SearchSourceAdapter.Declare(
                parseResult, args.PackageOption, args.AssemblyOption, args.ProjectOption,
                args.PlatformOption, args.PlatformLibraryOption, args.ExtensionsOption,
                args.AspNetCoreOption, args.BinOption, args.PackagePrefixOption);
            (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);
        }

        var options = new FindOptions
        {
            Pattern = pattern ?? "",
            SourceSelection = selection,
            Packages = [.. sources.Packages],
            Assemblies = [.. sources.Assemblies],
            PlatformAssemblies = [.. sources.PlatformAssemblies],
            PlatformFrameworks = [.. sources.PlatformFrameworks],
            Projects = [.. sources.Projects],
            BinPaths = [.. sources.Directories],
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            // Member lens: explicit --members, or auto-enabled by a leading '.' sentinel (e.g. .Serialize).
            // No valid type/namespace starts with '.', so the shortcut is unambiguous.
            Members = parseResult.GetValue(args.MembersOption)
                || (pattern?.StartsWith('.') ?? false),
            Limit = CommandLineHelpers.ParseTypeLimit(typeFilter),
            TypeFilter = typeFilter,
            Rows = opts.ParseRows(parseResult),
            Count = parseResult.GetValue(opts.Count),
            JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular = opts.ResolveTabular(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Verbose = parseResult.GetValue(opts.Verbose),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Tree = opts.ParseTree(parseResult),
            PackagePrefix = packagePrefix,
            PackagePrefixSpecified = packagePrefixSpecified,
            HasPackageProfileGroupScope = profileHasGroupScope,
            SourceOptions = sourceOptions
        };

        var verbosity = opts.ParseVerbosity(parseResult);
        var tipLevel = options.IsPackageProfile || options.FormatExplicitlySet || options.IsRawOutput || options.Count || verbosity == Verbosity.Quiet || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.Limit != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

        return new Success(options, verbosity, tipLevel);
    }

    /// <summary>
    /// Builds tips for successful find execution.
    /// </summary>
    public static List<Tip> BuildTips(FindOptions options, string? pattern)
    {
        // In member mode, canonicalize the displayed pattern (strip the leading '.' sentinel per
        // segment, preserving .ctor/.cctor) and append --members so following a tip stays in the
        // member lens and the explicit-flag and leading-dot forms yield identical tips.
        var tipPattern = options.Members ? MemberTipPattern(pattern) : pattern;
        var memberFlag = options.Members ? " --members" : "";

        var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
        if (pkg != null)
        {
            var sourceFlag = $"--package {pkg}";
            var pinnedSourceFlag = pkg.Contains("@", StringComparison.Ordinal)
                ? sourceFlag
                : $"--package {pkg}@<version>";

            return
            [
                new(MemberCommand.Name, $"<TypeName> {pinnedSourceFlag} --library <LibraryName>", "inspect the type you found"),
                new(FindCommand.Name, $"{tipPattern} {sourceFlag}{memberFlag} --table", "compact output"),
                new(FindCommand.Name, $"{tipPattern} {sourceFlag}{memberFlag} -v:d", "detailed results")
            ];
        }

        return
        [
            new(MemberCommand.Name, "<TypeName> --platform <LibraryName>", "inspect the type you found"),
            new(FindCommand.Name, $"{tipPattern} --platform{memberFlag} --table", "compact output"),
            new(FindCommand.Name, $"{tipPattern} --platform{memberFlag} -v:d", "detailed results")
        ];
    }

    /// <summary>
    /// Canonicalizes a member-lens pattern for tip display: strips the leading '.' sentinel from each
    /// comma segment (preserving .ctor/.cctor) so tips match the search actually performed.
    /// </summary>
    private static string? MemberTipPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return pattern;

        var segments = pattern
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MemberPatternSentinel.Strip)
            .Where(p => p.Length > 0)
            .ToArray();

        return segments.Length == 0 ? pattern : string.Join(",", segments);
    }
}
