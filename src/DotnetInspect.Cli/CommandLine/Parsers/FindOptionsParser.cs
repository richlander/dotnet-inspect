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
        Option<string?> PlatformOption,
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
        Option<bool> MembersOption,
        Option<string?> WorkspaceOption);

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
    public record Success(FindOptions Options, Verbosity Verbosity, CompanionOutput CompanionOutput) : FindParseResult;

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
        int? take = CliExecutionBoundCommandRegistry.GetPreparedValue(parseResult);
        string? workspace = parseResult.GetValue(args.WorkspaceOption);
        if (workspace is not null
            && new Option[] { args.PackageOption, args.AssemblyOption, args.PlatformOption,
                args.PlatformLibraryOption, args.EcosystemOption, args.ExtensionsOption,
                args.AspNetCoreOption, args.ProjectOption, args.BinOption, args.PackagePrefixOption,
                args.TfmOption }.Any(option => parseResult.GetResult(option) is { Implicit: false }))
        {
            CommandError.Write("--workspace cannot be combined with another Find source selector or --tfm.");
            return new Invalid();
        }
        if (!TryParseEcosystems(
                parseResult,
                args.EcosystemOption,
                out EcosystemPackId[]? ecosystems))
        {
            return new Invalid();
        }
        if (ecosystems is not null
            && (parseResult.GetResult(args.PackageOption) is { Implicit: false }
                || parseResult.GetResult(args.AssemblyOption) is { Implicit: false }
                || parseResult.GetResult(args.PlatformOption) is { Implicit: false }
                || parseResult.GetResult(args.PlatformLibraryOption) is { Implicit: false }
                || parseResult.GetResult(args.ExtensionsOption) is { Implicit: false }
                || parseResult.GetResult(args.AspNetCoreOption) is { Implicit: false }
                || parseResult.GetResult(args.ProjectOption) is { Implicit: false }
                || parseResult.GetResult(args.BinOption) is { Implicit: false }
                || parseResult.GetResult(args.PackagePrefixOption) is { Implicit: false }))
        {
            CommandError.Write(
                "--ecosystem cannot be combined with another Find source selector.");
            return new Invalid();
        }
        OutputFormat format = opts.ResolveFormat(parseResult, OutputFormat.Tsv);
        if (take is not null
            && (ecosystems is null && workspace is null
                || parseResult.GetValue(opts.Count)
                || format is not (OutputFormat.Tsv or OutputFormat.Jsonl)))
        {
            CommandError.Write(
                "find --take bounds Ecosystem prefix package candidates "
                + "and requires --ecosystem with TSV or JSONL output, or --workspace with those formats.");
            return new Invalid();
        }
        if (take is <= 0 or > FindOptions.MaximumEcosystemPrefixPackagesLimit)
        {
            CommandError.Write(
                "Find --take must be between 1 and "
                + $"{FindOptions.MaximumEcosystemPrefixPackagesLimit}.");
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
                + "use 'find --packages <ID-or-prefix*>' for package rows.");
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
            WorkspacePacket = workspace,
            SourceSelection = workspace is null ? selection : null,
            Ecosystems = ecosystems,
            PackagePrefixLimitReached = packagePrefixLimitReached,
            MaximumEcosystemPrefixPackages =
                take ?? FindOptions.DefaultMaximumEcosystemPrefixPackages,
            Packages = workspace is null ? [.. sources.Packages] : [],
            Assemblies = workspace is null ? [.. sources.Assemblies] : [],
            PlatformAssemblies = workspace is null ? [.. sources.PlatformAssemblies] : [],
            PlatformFrameworks = workspace is null ? [.. sources.PlatformFrameworks] : [],
            Projects = workspace is null ? [.. sources.Projects] : [],
            BinPaths = workspace is null ? [.. sources.Directories] : [],
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            // Member lens: explicit --members, or auto-enabled by a leading '.'
            // sentinel (e.g. .Serialize). No valid type or namespace starts
            // with '.', so the shortcut is unambiguous.
            Members = members,
            TypeFilter = typeFilter,
            QueryPlan = queryPlan,
            Count = parseResult.GetValue(opts.Count),
            JsonOutput = format == OutputFormat.Json,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular = opts.ResolveTabular(parseResult, OutputFormat.Tsv),
            Tsv = opts.ResolveTsv(parseResult, OutputFormat.Tsv),
            Jsonl = opts.ResolveJsonl(parseResult, OutputFormat.Tsv),
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

        var companionOutput = opts.ParseCompanionOutput(parseResult);

        return new Success(options, verbosity, companionOutput);
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
        var packs = EcosystemPackCatalog.Discover();
        string[] values =
        [
            .. (parseResult.GetValue(option) ?? [])
                .SelectMany(value => value.Split(','))
                .Select(value => value.Trim()),
        ];
        if (values.Any(string.IsNullOrEmpty))
        {
            CommandError.Write(
                "--ecosystem requires one or more comma-separated ecosystem names.");
            ecosystems = null;
            return false;
        }
        if (values.Any(value =>
                value.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            if (values.Length != 1)
            {
                CommandError.Write(
                    "'all' cannot be combined with other --ecosystem values.");
                ecosystems = null;
                return false;
            }

            values = [.. packs.Select(pack => pack.Id.Value)];
        }

        foreach (string value in values)
        {
            if (!EcosystemCommand.TryResolveFocus(
                    value, packs, out var pack))
            {
                ecosystems = null;
                return false;
            }
            if (pack is null)
            {
                CommandError.Write(
                    $"Unknown ecosystem '{value}'. Use a short name such as aspire "
                    + "or a canonical ID such as ecosystem.aspire.");
                ecosystems = null;
                return false;
            }
            EcosystemPackId id = pack.Id;
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
    public static List<Tip> BuildTips(
        FindOptions options,
        string? pattern,
        int? rowCount = null)
    {
        // In member mode, canonicalize the displayed pattern (strip the leading '.' sentinel per
        // segment, preserving .ctor/.cctor) and append --members so following a tip stays in the
        // member lens and the explicit-flag and leading-dot forms yield identical tips.
        var tipPattern = options.Members ? MemberTipPattern(pattern) : pattern;
        var memberFlag = options.Members ? " --members" : "";
        List<Tip> packageDiscoveryTips =
            rowCount == 0
                && !options.Members
                && (options.SourceSelection?.UsesImplicitPlatform
                    ?? !options.HasAnyScope)
                ? [new(
                    "find --packages",
                    pattern ?? "<ID-or-prefix*>",
                    "discover package IDs; find searches API symbols")]
                : [];

        if (options.WorkspacePacket is { } workspace)
        {
            return
            [
                new(FindCommand.Name, $"{tipPattern} --workspace {workspace}{memberFlag} --table", "compact output"),
                new(FindCommand.Name, $"{tipPattern} --workspace {workspace}{memberFlag} -v:d", "detailed results")
            ];
        }

        var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
        if (pkg != null)
        {
            var sourceFlag = $"--package {pkg}";
            var pinnedSourceFlag = pkg.Contains("@", StringComparison.Ordinal)
                ? sourceFlag
                : $"--package {pkg}@<version>";

            return
            [
                .. packageDiscoveryTips,
                new(MemberCommand.Name, $"<TypeName> {pinnedSourceFlag} --library <LibraryName>", "inspect the type you found"),
                new(FindCommand.Name, $"{tipPattern} {sourceFlag}{memberFlag} --table", "compact output"),
                new(FindCommand.Name, $"{tipPattern} {sourceFlag}{memberFlag} -v:d", "detailed results")
            ];
        }

        return
        [
            .. packageDiscoveryTips,
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
