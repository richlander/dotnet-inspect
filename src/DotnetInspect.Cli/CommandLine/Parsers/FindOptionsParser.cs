using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

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
        Option<string?> LiteralOption,
        Option<string[]> TakeOption,
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
        var literal = parseResult.GetValue(args.LiteralOption);
        var packagePrefix = parseResult.GetValue(args.PackagePrefixOption);
        var typeFilter = parseResult.GetValue(args.TypeFilterOption);
        bool packagePrefixSpecified =
            parseResult.GetResult(args.PackagePrefixOption)
                is { Implicit: false };
        string[] where = parseResult.GetValue(opts.RowWhere) ?? [];
        int? take =
            CliExecutionBoundCommandRegistry.GetPreparedValue(
                parseResult);
        bool packageContent = parseResult.GetValue(args.PackageContentOption);
        bool queryRequested = where.Length > 0
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
            SelectResult sectionSelection = SelectResolver.ResolveSelectAsSections(
                select, [PackageProfileSections.Packages],
                categories: new Dictionary<string, string[]>());
            if (SelectOutput.WriteUnresolved(sectionSelection))
                return new Invalid();
            if (sectionSelection.Sections?.Contains(PackageProfileSections.Packages) != true)
            {
                CommandError.Write("A package-prefix data selection must include Packages.");
                return new Invalid();
            }
        }
        PackageQueryOptions? packageQuery = null;
        if (queryRequested && !PackageQueryOptions.TryCreate(
            packagePrefix ?? "", where, packageContent, take,
            typeFilter,
            out packageQuery, out var queryError))
        {
            CommandError.Write(queryError);
            return new Invalid();
        }

        if (string.IsNullOrEmpty(pattern)
            && !packagePrefixSpecified
            && literal is null)
            return new ShowHelpWithTips();

        if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Find",
                out RowSelectionIntent<string>? rowSelection,
                out string? rowSelectionError))
        {
            CommandError.Write(rowSelectionError!);
            return new Invalid();
        }

        bool isPackageProfile =
            literal is null
            && string.IsNullOrEmpty(pattern)
            && packagePrefixSpecified;
        if (take is not null && !isPackageProfile)
        {
            CommandError.Write(
                "--take is available only with patternless find --package-prefix.");
            return new Invalid();
        }
        if (isPackageProfile
            && !queryRequested
            && typeFilter is not null)
        {
            CommandError.Write(
                "Package Profile does not support --type; "
                + "supply a pattern to run API search.");
            return new Invalid();
        }

        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        AssemblySetRequest sources;
        SearchSourceSelection? selection = null;
        bool profileHasGroupScope = false;
        if (literal is not null || string.IsNullOrEmpty(pattern))
        {
            // Profiles have their own grammar and reject API scopes before acquisition.
            // Literal assembly queries read the declared sources the same way: the shared
            // planner needs the caller's exact ordered selection, including duplicates it
            // rejects itself, so source normalization must not silently remove them.
            profileHasGroupScope = literal is null
                && (parseResult.GetValue(args.PlatformOption)
                    || parseResult.GetValue(args.ExtensionsOption)
                    || parseResult.GetValue(args.AspNetCoreOption));
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

        var verbosity = opts.ParseVerbosity(parseResult);
        var options = new FindOptions
        {
            Pattern = pattern ?? "",
            Literal = literal,
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
            Take = take,
            TypeFilter = typeFilter,
            RowSelection = rowSelection,
            Count = parseResult.GetValue(opts.Count),
            JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular = opts.ResolveTabular(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Select = select,
            PackageQuery = packageQuery,
            Tree = opts.ParseTree(parseResult),
            PackagePrefix = packagePrefix,
            PackagePrefixSpecified = packagePrefixSpecified,
            HasPackageProfileGroupScope = profileHasGroupScope,
            SourceOptions = sourceOptions
        };

        var tipLevel = options.Literal is not null || options.IsPackageProfile || options.FormatExplicitlySet || options.IsRawOutput || options.Count || verbosity == Verbosity.Quiet || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.RowSelection is not null
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
