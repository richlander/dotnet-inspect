using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;

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
        Option<bool> MembersOption,
        Option<int?> CandidatesOption,
        Option<int?> MatchesOption,
        Option<bool> PackageContentOption);

    /// <summary>
    /// Result of parsing find command options.
    /// </summary>
    public abstract record FindParseResult;

    /// <summary>
    /// Indicates help with tips should be shown (no pattern provided).
    /// </summary>
    public record ShowHelpWithTips : FindParseResult;

    public record Invalid : FindParseResult;

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
        string[] where = parseResult.GetValue(opts.RowWhere) ?? [];
        int? candidates = parseResult.GetValue(args.CandidatesOption);
        int? matches = parseResult.GetValue(args.MatchesOption);
        bool packageContent = parseResult.GetValue(args.PackageContentOption);
        bool queryRequested = where.Length > 0
            || candidates is not null || matches is not null
            || parseResult.GetResult(args.PackageContentOption) is { Implicit: false };
        string[]? select = opts.ParseSelect(parseResult);
        bool selectSpecified = parseResult.GetResult(opts.Select) is { Implicit: false };
        if ((queryRequested || selectSpecified)
            && (!string.IsNullOrEmpty(pattern) || !packagePrefixSpecified))
        {
            CommandError.Write(
                "Package Query options and -S data selection require patternless find --package-prefix; use -Q <section> for query discovery.");
            return new Invalid();
        }
        if (select is not null)
        {
            SelectResult selection = SelectResolver.ResolveSelectAsSections(
                select, [PackageProfileSections.Packages],
                categories: new Dictionary<string, string[]>());
            if (SelectOutput.WriteUnresolved(selection))
                return new Invalid();
            if (selection.Sections?.Contains(PackageProfileSections.Packages) != true)
            {
                CommandError.Write("A package-prefix data selection must include Packages.");
                return new Invalid();
            }
        }
        PackageQueryOptions? packageQuery = null;
        if (queryRequested && !PackageQueryOptions.TryCreate(
            packagePrefix ?? "", where, packageContent, candidates, matches,
            parseResult.GetValue(opts.Count),
            parseResult.GetValue(args.TypeFilterOption),
            out packageQuery, out var queryError))
        {
            CommandError.Write(queryError);
            return new Invalid();
        }

        if (string.IsNullOrEmpty(pattern)
            && !packagePrefixSpecified)
            return new ShowHelpWithTips();

        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        var packages = string.IsNullOrEmpty(pattern)
            ? parseResult.GetValue(args.PackageOption) ?? []
            : await CommandLineHelpers.MergeWithPrefixPackagesAsync(
                parseResult.GetValue(args.PackageOption) ?? [],
                packagePrefix,
                parseResult.GetValue(opts.Verbose),
                sourceOptions);
        var assemblies = parseResult.GetValue(args.AssemblyOption) ?? [];
        var projects = parseResult.GetValue(args.ProjectOption) ?? [];
        var binPaths = parseResult.GetValue(args.BinOption) ?? [];
        var typeFilter = parseResult.GetValue(args.TypeFilterOption);

        var (allPlatformFrameworks, platformAssemblies) = CommandLineHelpers.ParsePlatformSearchOption(
            parseResult,
            args.PlatformOption,
            args.PlatformLibraryOption);

        var scopeFlags = new ScopeResolver.ScopeFlags(
            Platform: allPlatformFrameworks,
            Extensions: parseResult.GetValue(args.ExtensionsOption),
            AspNetCore: parseResult.GetValue(args.AspNetCoreOption));
        var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix,
            hasOtherScopeIndicators: projects.Length > 0 || binPaths.Length > 0 || platformAssemblies.Length > 0);

        var options = new FindOptions
        {
            Pattern = pattern ?? "",
            Packages = scope.Packages,
            Assemblies = assemblies,
            PlatformAssemblies = platformAssemblies,
            PlatformFrameworks = scope.Frameworks,
            Projects = projects,
            BinPaths = binPaths,
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
            Select = select,
            PackageQuery = packageQuery,
            Tree = opts.ParseTree(parseResult),
            PackagePrefix = packagePrefix,
            PackagePrefixSpecified = packagePrefixSpecified,
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
