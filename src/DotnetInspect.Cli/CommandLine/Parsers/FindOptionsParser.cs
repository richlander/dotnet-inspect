using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
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
        Option<string[]> EcosystemOption,
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
        var typeFilter = parseResult.GetValue(args.TypeFilterOption);
        if (!TryParseEcosystems(
                parseResult,
                args.EcosystemOption,
                out EcosystemPackId[]? ecosystems))
        {
            return new Invalid();
        }
        bool packagePrefixSpecified =
            parseResult.GetResult(args.PackagePrefixOption)
                is { Implicit: false };
        if (string.IsNullOrEmpty(pattern)
            && !packagePrefixSpecified)
            return new ShowHelpWithTips();
        if (string.IsNullOrEmpty(pattern)
            && packagePrefixSpecified)
        {
            CommandError.Write(
                "find --package-prefix requires a type or member pattern; "
                + "use 'package query <ID-or-prefix*>' for package rows.");
            return new Invalid();
        }

        if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Find",
                out RowSelectionIntent<string>? rowSelection,
                out string? rowSelectionError))
        {
            CommandError.Write(rowSelectionError!);
            return new Invalid();
        }
        bool members = parseResult.GetValue(args.MembersOption)
            || (pattern?.StartsWith('.') ?? false);
        FindQueryRouteKind routeKind =
            members
                ? FindQueryRouteKind.MemberResults
                : FindQueryRouteKind.TypeResults;
        if (!FindQueryOptions.TryResolve(
                routeKind,
                rowSelection,
                out FindQueryPlan queryPlan,
                out string? queryPlanError))
        {
            CommandError.Write(queryPlanError!);
            return new Invalid();
        }

        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        var intent = SearchSourceAdapter.Declare(
            parseResult, args.PackageOption, args.AssemblyOption, args.ProjectOption,
            args.PlatformOption, args.PlatformLibraryOption, args.ExtensionsOption,
            args.AspNetCoreOption, args.BinOption, args.PackagePrefixOption);
        SearchSourceBinding binding = await SearchSourceAdapter.BindAsync(
            intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);
        SearchSourceSelection selection = binding.Selection;
        AssemblySetRequest sources = binding.Request;
        bool packagePrefixLimitReached =
            binding.PackagePrefixLimitReached;

        var verbosity = opts.ParseVerbosity(parseResult);
        var options = new FindOptions
        {
            Pattern = pattern ?? "",
            SourceSelection = selection,
            Ecosystems = ecosystems,
            PackagePrefixLimitReached = packagePrefixLimitReached,
            Packages = [.. sources.Packages],
            Assemblies = [.. sources.Assemblies],
            PlatformAssemblies = [.. sources.PlatformAssemblies],
            PlatformFrameworks = [.. sources.PlatformFrameworks],
            Projects = [.. sources.Projects],
            BinPaths = [.. sources.Directories],
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            // Member lens: explicit --members, or auto-enabled by a leading '.'
            // sentinel (e.g. .Serialize). No valid type or namespace starts
            // with '.', so the shortcut is unambiguous.
            Members = members,
            TypeFilter = typeFilter,
            QueryPlan = queryPlan,
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
            Tree = opts.ParseTree(parseResult),
            PackagePrefix = packagePrefix,
            PackagePrefixSpecified = packagePrefixSpecified,
            SourceOptions = sourceOptions
        };

        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Count || verbosity == Verbosity.Quiet || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.EffectiveRowSelection is not null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

        return new Success(options, verbosity, tipLevel);
    }

    private static bool TryParseEcosystems(
        ParseResult parseResult,
        Option<string[]> option,
        out EcosystemPackId[]? ecosystems)
    {
        if (parseResult.GetResult(option) is not { Implicit: false })
        {
            ecosystems = null;
            return true;
        }

        var selected = new List<EcosystemPackId>();
        var seen = new HashSet<EcosystemPackId>();
        foreach (string value in parseResult.GetValue(option) ?? [])
        {
            if (!EcosystemPackId.TryCreate(value, out EcosystemPackId? id))
            {
                CommandError.Write(
                    $"Invalid ecosystem '{value}'. Use a canonical ID such as ecosystem.aspire.");
                ecosystems = null;
                return false;
            }
            if (!seen.Add(id))
            {
                CommandError.Write(
                    $"Ecosystem '{id}' cannot be selected more than once.");
                ecosystems = null;
                return false;
            }

            switch (EcosystemPackCatalog.SelectWorkspaceRegistration(id))
            {
                case EcosystemWorkspaceRegistrationSelectionResult.Known:
                    selected.Add(id);
                    break;
                case EcosystemWorkspaceRegistrationSelectionResult.Unavailable:
                    CommandError.Write(
                        $"Ecosystem '{id}' has no Workspace registration.");
                    ecosystems = null;
                    return false;
                case EcosystemWorkspaceRegistrationSelectionResult.Unknown:
                    CommandError.Write($"Unknown ecosystem '{id}'.");
                    ecosystems = null;
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unexpected ecosystem Workspace registration outcome.");
            }
        }

        ecosystems = [.. selected];
        return true;
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
