using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
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
        Option<bool> ExtensionsOption,
        Option<bool> AspNetCoreOption,
        Option<string[]> ProjectOption,
        Option<string[]> BinOption,
        Option<string[]> EcosystemOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> TypeFilterOption,
        Option<bool> CompactOption,
        Option<bool> NoHeaderOption,
        Option<bool> MembersOption,
        Option<string?> LiteralOption);

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
    /// Parses find command options asynchronously.
    /// </summary>
    public static async Task<FindParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        FindCommandArgs args)
    {
        var pattern = parseResult.GetValue(args.PatternArg);
        var literal = parseResult.GetValue(args.LiteralOption);
        var typeFilter = parseResult.GetValue(args.TypeFilterOption);
        if (string.IsNullOrEmpty(pattern) && literal is null)
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

        if (!TryCreateWorkspacePlan(
                parseResult.GetValue(args.EcosystemOption) ?? [],
                out WorkspacePlan? workspacePlan))
        {
            return new Invalid();
        }

        if (literal is not null
            && !ValidateLiteralQuery(
                parseResult,
                opts,
                args,
                pattern,
                literal))
        {
            return new Invalid();
        }
        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        AssemblySetRequest sources;
        SearchSourceSelection? selection = null;
        if (literal is not null)
        {
            // Literal assembly queries read the declared sources directly: the shared
            // planner needs the caller's exact ordered selection, including duplicates it
            // rejects itself, so source normalization must not silently remove them.
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
                args.AspNetCoreOption, args.BinOption);
            SearchSourceBinding binding = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);
            selection = binding.Selection;
            sources = binding.Request;
        }

        var verbosity = opts.ParseVerbosity(parseResult);
        var options = new FindOptions
        {
            Pattern = pattern ?? "",
            Literal = literal,
            SourceSelection = selection,
            WorkspacePlan = workspacePlan,
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
            Tree = opts.ParseTree(parseResult),
            SourceOptions = sourceOptions
        };

        var tipLevel = options.Literal is not null || options.FormatExplicitlySet || options.IsRawOutput || options.Count || verbosity == Verbosity.Quiet || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.RowSelection is not null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

        return new Success(options, verbosity, tipLevel);
    }

    private static bool ValidateLiteralQuery(
        ParseResult parseResult,
        SharedOptions opts,
        FindCommandArgs args,
        string? pattern,
        string literal)
    {
        if (!string.IsNullOrEmpty(pattern)
            || parseResult.GetResult(args.AssemblyOption)
                is { Implicit: false }
            || parseResult.GetResult(args.EcosystemOption)
                is { Implicit: false }
            || parseResult.GetResult(args.PlatformOption)
                is { Implicit: false }
            || parseResult.GetResult(args.PlatformLibraryOption)
                is { Implicit: false }
            || parseResult.GetValue(args.ExtensionsOption)
            || parseResult.GetValue(args.AspNetCoreOption)
            || parseResult.GetResult(args.ProjectOption)
                is { Implicit: false }
            || parseResult.GetResult(args.BinOption)
                is { Implicit: false }
            || parseResult.GetValue(args.MembersOption)
            || parseResult.GetValue(args.AllOption)
            || parseResult.GetResult(args.TypeFilterOption)
                is { Implicit: false })
        {
            CommandError.Write(
                "--literal searches only explicit ID@VERSION packages; "
                + "it cannot be combined with a type pattern, API search scopes, "
                + "--ecosystem, --members, --all, or --type.");
            return false;
        }

        if (parseResult.GetValue(opts.Discover) is not null)
            return true;

        string tfm =
            parseResult.GetValue(args.TfmOption) ?? "";
        if (string.IsNullOrWhiteSpace(tfm))
        {
            CommandError.Write(
                PackageAssemblyQueryDiagnostics.MissingTargetFramework);
            return false;
        }

        try
        {
            _ = PackageAssemblyQuery.Plan(
                PackageAssemblyPatterns.StringLiteralContains,
                literal,
                parseResult.GetValue(args.PackageOption) ?? [],
                tfm);
            return true;
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(
                PackageAssemblyQueryDiagnostics.Describe(ex));
            return false;
        }
    }

    internal static bool TryCreateWorkspacePlan(
        IReadOnlyList<string> values,
        out WorkspacePlan? plan)
    {
        if (values.Count == 0)
        {
            plan = EcosystemPackCatalog.CreateWorkspacePlan();
            return true;
        }

        var selected = new List<EcosystemPackId>(values.Count);
        var seen = new HashSet<EcosystemPackId>();
        var packs = EcosystemPackCatalog.Discover();
        foreach (string value in values)
        {
            if (!EcosystemCommand.TryResolveFocus(
                    value,
                    packs,
                    out EcosystemPackDescriptor? pack)
                || pack is null)
            {
                if (pack is null
                    && value.Equals(
                        "list",
                        StringComparison.OrdinalIgnoreCase))
                {
                    CommandError.Write(
                        "'list' is not an ecosystem selection. "
                        + "Run 'ecosystem' to list available ecosystems.");
                }
                plan = null;
                return false;
            }

            if (!seen.Add(pack.Id))
            {
                CommandError.Write(
                    $"Find cannot register ecosystem '{value}' more than once.");
                plan = null;
                return false;
            }

            selected.Add(pack.Id);
        }

        plan = EcosystemPackCatalog.CreateWorkspacePlan(selected);
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
