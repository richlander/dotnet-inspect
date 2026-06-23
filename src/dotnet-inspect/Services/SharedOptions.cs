using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Container for shared CLI options that are reused across multiple commands.
/// Created once in CreateRootCommand and passed to command builders.
/// </summary>
public class SharedOptions
{
    // Output format options
    public Option<bool> Json { get; } = new("--json") { Description = "Output as JSON" };
    public Option<bool> Markdown { get; } = new("--markdown") { Description = "Output as markdown" };
    public Option<bool> PlainText { get; } = new("--plaintext") { Description = "Output as plain text" };
    public Option<bool> Raw { get; } = new("--raw") { Description = "Print only the selected section's content, undecorated (single -S code section; e.g. redirect Decompiled Source to a .cs file)" };
    public Option<bool> BrowsableUrls { get; } = new("--blob") { Description = "Use GitHub /blob/ URLs for browser viewing (default: raw URLs for agents)" };
    public Option<bool> Mermaid { get; } = new("--mermaid") { Description = "Output as mermaid diagram (standalone or with --markdown for embedded)" };
    public Option<bool> Table { get; } = new("--table") { Description = "Output as a pretty table (space-padded columns)" };
    public Option<bool> Tsv { get; } = new("--tsv") { Description = "Output as normalized tab-separated values" };
    public Option<bool> Jsonl { get; } = new("--jsonl") { Description = "Output as JSON Lines (one object per row)" };
    public Option<bool> OneLine { get; } = new("--oneline") { Description = "Compatibility alias for --table", Hidden = true };
    public Option<bool> NoHeaders { get; } = new("--no-headers") { Description = "Suppress table/TSV column headers" };

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

    // Output control options
    public Option<int?> Limit { get; }
    public Option<bool> Rows { get; } = new("--rows") { Description = "Interpret -n/-N as data rows per rendered table instead of output lines" };
    public Option<int?> Tail { get; }
    public Option<bool> Count { get; } = new("--count") { Description = "With a single selected section, output the number of table rows" };
    public Option<bool> Info { get; } = new("--info") { Description = "Show operational metrics (output, time, HTTP, cache) on stderr" };
    public Option<string?> Tips { get; }

    // Discovery option
    public Option<string?> Discover { get; }

    // Projection options
    public Option<string?> Select { get; }
    public Option<string?> Columns { get; }
    public Option<string?> Fields { get; }
    public Option<bool> Schema { get; } = new("--schema") { Description = "With -D: show the full static schema without resolving/loading source (offline)" };
    public Option<bool> Tree { get; } = new("--tree") { Description = "Show discovery as a tree (sections → items)" };

    // NuGet source options
    public Option<string[]> Source { get; } = new("--source")
    {
        Description = "NuGet source URL (replaces defaults, can repeat)",
        AllowMultipleArgumentsPerToken = true
    };
    public Option<string[]> AddSource { get; } = new("--add-source")
    {
        Description = "NuGet source URL to add (can repeat)",
        AllowMultipleArgumentsPerToken = true
    };
    public Option<string?> NuGetConfig { get; } = new("--nugetconfig")
    {
        Description = "Path to nuget.config file"
    };

    public SharedOptions()
    {
        Limit = new Option<int?>("-n") { Description = "Limit to first N lines (like head -n)" };
        Limit.Aliases.Add("--head");

        Tail = new Option<int?>("--tail") { Description = "Limit to last N lines (like tail -n)" };

        Tips = new Option<string?>("--tips")
        {
            Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Tips.Aliases.Add("-T");

        Discover = new Option<string?>("-D")
        {
            Description = "Discover schema: sections, or items within a section",
            Arity = ArgumentArity.ZeroOrOne
        };
        Discover.Aliases.Add("--discover");

        Select = new Option<string?>("-S")
        {
            Description = "Select sections/categories by name, wildcard, or @All (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Select.Aliases.Add("--select");
        Select.Aliases.Add("-s");
        Select.Aliases.Add("--section");

        Columns = new Option<string?>("--columns")
        {
            Description = "Filter columns by name (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };

        Fields = new Option<string?>("--fields")
        {
            Description = "Filter fields by name (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };

        NoHeaders.Aliases.Add("--no-header");
        NoHeaders.Aliases.Add("--nh");
        BrowsableUrls.Aliases.Add("--browsable-urls");
    }

    /// <summary>
    /// Adds core output options to a command (verbose, verbosity, tips, limit).
    /// </summary>
    public void AddOutputOptionsTo(Command command)
    {
        command.Options.Add(Verbose);
        command.Options.Add(Verbosity);
        command.Options.Add(Tips);
        command.Options.Add(Info);
        command.Options.Add(Limit);
        command.Options.Add(Rows);
        command.Options.Add(Tail);
    }

    /// <summary>
    /// Adds JSON output option to a command.
    /// </summary>
    public void AddJsonOptionTo(Command command)
    {
        command.Options.Add(Json);
    }

    public void AddTableOptionsTo(Command command)
    {
        command.Options.Add(Table);
        command.Options.Add(Tsv);
        command.Options.Add(Jsonl);
        command.Options.Add(OneLine);
        command.Options.Add(NoHeaders);
    }

    /// <summary>
    /// Adds section filtering and projection options to a command.
    /// </summary>
    public void AddSectionOptionsTo(Command command)
    {
        command.Options.Add(Discover);
        command.Options.Add(Select);
        command.Options.Add(Columns);
        command.Options.Add(Fields);
        command.Options.Add(Schema);
        command.Options.Add(Tree);
    }

    /// <summary>
    /// Adds the count output option to commands that support sectioned tables.
    /// </summary>
    public void AddCountOptionTo(Command command)
    {
        command.Options.Add(Count);
    }

    public int? ParseRows(ParseResult parseResult)
        => parseResult.GetValue(Rows) ? parseResult.GetValue(Limit) : null;

    /// <summary>
    /// Adds NuGet source options to a command.
    /// </summary>
    public void AddNuGetOptionsTo(Command command)
    {
        command.Options.Add(Source);
        command.Options.Add(AddSource);
        command.Options.Add(NuGetConfig);
    }

    /// <summary>
    /// Adds all common options for a full inspection command (JSON, markdown, verbose, sections, NuGet).
    /// </summary>
    public void AddAllOptionsTo(Command command)
    {
        command.Options.Add(Json);
        command.Options.Add(Markdown);
        command.Options.Add(PlainText);
        command.Options.Add(Mermaid);
        AddTableOptionsTo(command);
        AddOutputOptionsTo(command);
        AddSectionOptionsTo(command);
        AddNuGetOptionsTo(command);
    }

    // Parsing helpers that use this instance's options

    /// <summary>
    /// Parses NuGet source options from parse result.
    /// </summary>
    public NuGetSourceOptions ParseNuGetSourceOptions(ParseResult parseResult)
    {
        var sources = parseResult.GetValue(Source) ?? [];
        var addSources = parseResult.GetValue(AddSource) ?? [];
        var configFile = parseResult.GetValue(NuGetConfig);

        if (sources.Length == 0 && addSources.Length == 0 && configFile == null)
            return NuGetSourceOptions.Default;

        return new NuGetSourceOptions
        {
            Sources = sources,
            AdditionalSources = addSources,
            ConfigFile = configFile
        };
    }

    /// <summary>
    /// Parses verbosity from parse result.
    /// </summary>
    public Verbosity ParseVerbosity(ParseResult parseResult)
        => OptionParsers.ParseVerbosity(parseResult.GetValue(Verbosity));

    /// <summary>
    /// Parses tip level from parse result.
    /// </summary>
    public TipLevel ParseTipLevel(ParseResult parseResult)
        => OptionParsers.ParseTipLevel(parseResult.GetValue(Tips), parseResult.GetResult(Tips) != null);

    /// <summary>
    /// Resolves the output format from parse result.
    /// Precedence: explicit CLI flags (--json, --markdown, -v:*) → DOTNET_INSPECT_FORMAT env → <paramref name="defaultFormat"/>.
    /// </summary>
    public OutputFormat ResolveFormat(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        bool jsonFlag = parseResult.GetValue(Json);
        bool markdownFlag = parseResult.GetValue(Markdown);
        bool plainTextFlag = parseResult.GetValue(PlainText);
        bool mermaidFlag = parseResult.GetValue(Mermaid);
        bool tableFlag = IsExplicitTrue(parseResult, Table) || IsExplicitTrue(parseResult, OneLine);
        bool tsvFlag = IsExplicitTrue(parseResult, Tsv);
        bool jsonlFlag = IsExplicitTrue(parseResult, Jsonl);
        bool hasVerbosity = parseResult.GetResult(Verbosity) is { Implicit: false };
        Verbosity? verbosity = hasVerbosity ? ParseVerbosity(parseResult) : null;
        ValidateRendererFlags(jsonFlag, markdownFlag, plainTextFlag, mermaidFlag, tableFlag || tsvFlag || jsonlFlag, hasVerbosity);
        return OutputFormatResolver.Resolve(jsonFlag, markdownFlag, verbosity, plainTextFlag, mermaidFlag, tableFlag, tsvFlag, jsonlFlag, defaultFormat);
    }

    /// <summary>
    /// Returns true when --mermaid is combined with --markdown (embedded mermaid in markdown).
    /// </summary>
    public bool IsEmbeddedMermaid(ParseResult parseResult)
        => OutputFormatResolver.IsEmbeddedMermaid(parseResult.GetValue(Markdown), parseResult.GetValue(Mermaid));

    /// <summary>
    /// Resolves whether tabular output should be used, considering --table, --tsv, --jsonl, and the --oneline compatibility alias.
    /// Throws if a tabular flag is combined with -v (contradictory: -v implies markdown).
    /// </summary>
    public bool ResolveOneLine(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        var format = ResolveFormat(parseResult, defaultFormat);
        return format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;
    }

    public bool ResolveTsv(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown) =>
        ResolveFormat(parseResult, defaultFormat) == OutputFormat.Tsv;

    public bool ResolveJsonl(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown) =>
        ResolveFormat(parseResult, defaultFormat) == OutputFormat.Jsonl;

    /// <summary>
    /// Returns true when the user explicitly chose an output format via CLI flags
    /// (--json, --markdown, --plain-text, --table, --tsv, --jsonl, --oneline, or -v).
    /// When false, commands are free to apply their own default format.
    /// </summary>
    public bool IsFormatExplicitlySet(ParseResult parseResult)
    {
        if (IsTableExplicitlySet(parseResult)) return true;
        if (parseResult.GetResult(Json) is { Implicit: false }) return true;
        if (parseResult.GetResult(Markdown) is { Implicit: false }) return true;
        if (parseResult.GetResult(PlainText) is { Implicit: false }) return true;
        if (parseResult.GetResult(Mermaid) is { Implicit: false }) return true;
        if (parseResult.GetResult(Verbosity) is { Implicit: false }) return true;
        return false;
    }

    public bool IsTableExplicitlySet(ParseResult parseResult) =>
        IsExplicit(parseResult, Table) || IsExplicit(parseResult, Tsv) || IsExplicit(parseResult, Jsonl) || IsExplicit(parseResult, OneLine);

    /// <summary>
    /// Parses select list from parse result.
    /// Returns null if not specified, @Default for bare -S, or populated array with section/category names.
    /// </summary>
    public string[]? ParseSelect(ParseResult parseResult)
        => IsBareFlag(parseResult, Select)
            ? [SelectResolver.InfoSelector]
            : ParseCommaSeparatedList(parseResult.GetValue(Select));

    /// <summary>
    /// Parses discover flag from parse result.
    /// Returns null if not specified, empty array for bare -D, or populated array with section name.
    /// </summary>
    public string[]? ParseDiscover(ParseResult parseResult)
        => ParseProjectionList(parseResult, Discover);

    /// <summary>
    /// Parses columns list from parse result.
    /// Returns null if not specified, or populated array with column names.
    /// </summary>
    public string[]? ParseColumns(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Columns));

    /// <summary>
    /// Parses fields list from parse result.
    /// Returns null if not specified, or populated array with field names.
    /// </summary>
    public string[]? ParseFields(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Fields));

    /// <summary>
    /// Returns true if -D/--discover flag is present.
    /// </summary>
    public bool IsDiscoveryMode(ParseResult parseResult)
        => parseResult.GetResult(Discover) is { Implicit: false };

    public bool ParseTree(ParseResult parseResult) => parseResult.GetValue(Tree);

    /// <summary>
    /// Resolves static discovery. <c>--schema</c> opts out of effective discovery.
    /// </summary>
    public bool ParseSchema(ParseResult parseResult)
        => parseResult.GetValue(Schema);

    private static string[]? ParseProjectionList(ParseResult parseResult, Option<string?> option)
    {
        var values = ParseCommaSeparatedList(parseResult.GetValue(option));
        // Bare flag with no value: return empty array (signals "discover")
        if (values == null && parseResult.GetResult(option) != null)
            return [];
        return values;
    }

    private static bool IsBareFlag(ParseResult parseResult, Option<string?> option)
    {
        return parseResult.GetResult(option) is { Implicit: false } &&
               string.IsNullOrWhiteSpace(parseResult.GetValue(option));
    }

    private static void ValidateRendererFlags(
        bool jsonFlag,
        bool markdownFlag,
        bool plainTextFlag,
        bool mermaidFlag,
        bool tabularFlag,
        bool hasVerbosity)
    {
        if (!tabularFlag)
            return;

        if (jsonFlag)
        {
            Console.Error.WriteLine("--json cannot be combined with --table, --tsv, or --jsonl.");
            throw new OperationCanceledException();
        }

        if (markdownFlag || plainTextFlag || mermaidFlag || hasVerbosity)
        {
            Console.Error.WriteLine("--table/--tsv/--jsonl cannot be combined with --markdown, --plaintext, --mermaid, or -v.");
            throw new OperationCanceledException();
        }
    }

    private static bool IsExplicit(ParseResult parseResult, Option<bool> option) =>
        parseResult.GetResult(option) is { Implicit: false };

    private static bool IsExplicitTrue(ParseResult parseResult, Option<bool> option) =>
        IsExplicit(parseResult, option) && parseResult.GetValue(option);

    private static readonly char[] ListSeparators = [',', ';'];

    private static string[]? ParseCommaSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UnescapeAtCategory)
            .ToArray();
    }

    private static string UnescapeAtCategory(string value)
        => ArgumentPreprocessor.UnescapeAtCategoryValue(value);
}
