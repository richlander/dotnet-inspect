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

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

    // Output control options
    public Option<int?> Limit { get; } = new("-n") { Description = "Limit output lines (like head -n)" };
    public Option<string?> Tips { get; }

    // Section filtering options
    public Option<string?> ExcludeSections { get; } = new("-x")
    {
        Description = "Exclude sections by name (comma-separated, e.g., -x:Methods)"
    };

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
            Arity = ArgumentArity.ExactlyOne
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
        command.Options.Add(Limit);
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
        command.Options.Add(ExcludeSections);
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
    /// Parses exclude sections from parse result.
    /// </summary>
    public HashSet<string>? ParseExcludeSections(ParseResult parseResult)
        => OptionParsers.ParseSectionList(parseResult.GetValue(ExcludeSections));

    /// <summary>
    /// Resolves the output format from parse result.
    /// Precedence: explicit CLI flags (--json, --markdown, -v:*) → DOTNET_INSPECT_FORMAT env → default (OneLine).
    /// </summary>
    public OutputFormat ResolveFormat(ParseResult parseResult)
    {
        bool jsonFlag = parseResult.GetValue(Json);
        bool markdownFlag = parseResult.GetValue(Markdown);
        bool hasVerbosity = parseResult.GetResult(Verbosity) is { Implicit: false };
        Verbosity? verbosity = hasVerbosity ? ParseVerbosity(parseResult) : null;
        return OutputFormatResolver.Resolve(jsonFlag, markdownFlag, verbosity);
    }

    /// <summary>
    /// Resolves whether oneline output should be used, considering the --oneline flag and format resolution.
    /// Explicit --oneline always wins; otherwise derived from ResolveFormat.
    /// </summary>
    public bool ResolveOneLine(ParseResult parseResult, Option<bool> oneLineOption)
    {
        // Explicit --oneline flag always wins
        if (parseResult.GetResult(oneLineOption) is { Implicit: false })
            return parseResult.GetValue(oneLineOption);

        return ResolveFormat(parseResult) == OutputFormat.OneLine;
    }

    /// <summary>
    /// Parses select list from parse result.
    /// Returns null if not specified, or populated array with section names.
    /// </summary>
    public string[]? ParseSelect(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Select));

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
    {
        return parseResult.GetResult(Discover) is { Implicit: false };
    }

    private static string[]? ParseProjectionList(ParseResult parseResult, Option<string?> option)
    {
        var values = ParseCommaSeparatedList(parseResult.GetValue(option));
        // Bare flag with no value: return empty array (signals "discover")
        if (values == null && parseResult.GetResult(option) != null)
            return [];
        return values;
    }

    private static string[]? ParseCommaSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
