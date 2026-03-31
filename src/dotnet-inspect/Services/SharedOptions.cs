using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
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
    public Option<bool> Mermaid { get; } = new("--mermaid") { Description = "Output as mermaid diagram (standalone or with --markdown for embedded)" };

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

    // Output control options
    public Option<int?> Limit { get; }
    public Option<int?> Tail { get; }
    public Option<bool> Info { get; } = new("--info") { Description = "Show operational metrics (output, time, HTTP, cache) on stderr" };
    public Option<string?> Tips { get; }

    // Discovery option
    public Option<string?> Discover { get; }

    // Projection options
    public Option<string?> Select { get; }
    public Option<string?> Columns { get; }
    public Option<string?> Fields { get; }
    public Option<bool> Effective { get; } = new("--effective") { Description = "Show sections with data (runs full pipeline)" };
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
            Description = "Select sections by name (comma-separated, supports wildcards)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Select.Aliases.Add("--select");
        Select.Aliases.Add("-s");
        Select.Aliases.Add("--section");

        Columns = new Option<string?>("--columns")
        {
            Description = "Filter columns by name (comma-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };

        Fields = new Option<string?>("--fields")
        {
            Description = "Filter fields by name (comma-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };
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
        command.Options.Add(Tail);
    }

    /// <summary>
    /// Adds JSON output option to a command.
    /// </summary>
    public void AddJsonOptionTo(Command command)
    {
        command.Options.Add(Json);
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
        command.Options.Add(Effective);
        command.Options.Add(Tree);
    }

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
        bool hasVerbosity = parseResult.GetResult(Verbosity) is { Implicit: false };
        Verbosity? verbosity = hasVerbosity ? ParseVerbosity(parseResult) : null;
        return OutputFormatResolver.Resolve(jsonFlag, markdownFlag, verbosity, plainTextFlag, mermaidFlag, defaultFormat);
    }

    /// <summary>
    /// Returns true when --mermaid is combined with --markdown (embedded mermaid in markdown).
    /// </summary>
    public bool IsEmbeddedMermaid(ParseResult parseResult)
        => OutputFormatResolver.IsEmbeddedMermaid(parseResult.GetValue(Markdown), parseResult.GetValue(Mermaid));

    /// <summary>
    /// Resolves whether oneline output should be used, considering the --oneline flag and format resolution.
    /// Explicit --oneline always wins; otherwise derived from ResolveFormat.
    /// Throws if --oneline is combined with -v (contradictory: -v implies markdown).
    /// </summary>
    public bool ResolveOneLine(ParseResult parseResult, Option<bool> oneLineOption, OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        bool explicitOneLine = parseResult.GetResult(oneLineOption) is { Implicit: false };
        bool explicitVerbosity = parseResult.GetResult(Verbosity) is { Implicit: false };

        if (explicitOneLine && explicitVerbosity)
        {
            Console.Error.WriteLine("--oneline and -v cannot be combined. Use another formatter instead, or omit -v for oneline.");
            throw new OperationCanceledException();
        }

        // Explicit --oneline flag always wins
        if (explicitOneLine)
            return parseResult.GetValue(oneLineOption);

        return ResolveFormat(parseResult, defaultFormat) == OutputFormat.OneLine;
    }

    /// <summary>
    /// Returns true when the user explicitly chose an output format via CLI flags
    /// (--json, --markdown, --plain-text, --oneline, or -v).
    /// When false, commands are free to apply their own default format.
    /// </summary>
    public bool IsFormatExplicitlySet(ParseResult parseResult, Option<bool>? oneLineOption = null)
    {
        if (oneLineOption != null && parseResult.GetResult(oneLineOption) is { Implicit: false }) return true;
        if (parseResult.GetResult(Json) is { Implicit: false }) return true;
        if (parseResult.GetResult(Markdown) is { Implicit: false }) return true;
        if (parseResult.GetResult(PlainText) is { Implicit: false }) return true;
        if (parseResult.GetResult(Mermaid) is { Implicit: false }) return true;
        if (parseResult.GetResult(Verbosity) is { Implicit: false }) return true;
        return false;
    }

    /// <summary>
    /// Parses select list from parse result.
    /// Returns null if not specified, or populated array with section names.
    /// </summary>
    public string[]? ParseSelect(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Select));

    /// <summary>
    /// Parses discover flag from parse result.
    /// Returns null if not specified, empty array for bare -D or bare -S, or populated array with section name.
    /// </summary>
    public string[]? ParseDiscover(ParseResult parseResult)
    {
        var discover = ParseProjectionList(parseResult, Discover);
        // Bare -S (no value) also triggers section discovery
        if (discover == null && IsBareFlag(parseResult, Select))
            return [];
        return discover;
    }

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
    /// Returns true if -D/--discover flag is present, or bare -S (no value) is used.
    /// </summary>
    public bool IsDiscoveryMode(ParseResult parseResult)
    {
        if (parseResult.GetResult(Discover) is { Implicit: false })
            return true;

        // Bare -S (no value) also triggers discovery (lists sections)
        return IsBareFlag(parseResult, Select);
    }

    public bool ParseTree(ParseResult parseResult) => parseResult.GetValue(Tree);

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

    private static string[]? ParseCommaSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
