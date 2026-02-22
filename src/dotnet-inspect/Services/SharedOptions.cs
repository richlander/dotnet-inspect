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
    public Option<bool> Markout { get; } = new("--markout") { Description = "Output as Markout (default)" };

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };

    // Output control options
    public Option<int?> Limit { get; } = new("-n") { Description = "Limit output lines (like head -n)" };
    public Option<string?> Tips { get; }

    // Section filtering options
    public Option<string?> IncludeSections { get; } = new("-s")
    {
        Description = "Include sections by name (comma-separated, supports wildcards). Use -s alone to list.",
        Arity = ArgumentArity.ZeroOrOne
    };
    public Option<string?> ExcludeSections { get; } = new("-x")
    {
        Description = "Exclude sections by name (comma-separated, e.g., -x:Methods)"
    };

    // Projection options
    public Option<string?> Columns { get; } = new("--columns")
    {
        Description = "Include only these table columns (comma-separated). Use --columns alone to list.",
        Arity = ArgumentArity.ZeroOrOne
    };
    public Option<string?> Fields { get; } = new("--fields")
    {
        Description = "Include only these scalar fields (comma-separated). Use --fields alone to list.",
        Arity = ArgumentArity.ZeroOrOne
    };

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
        command.Options.Add(IncludeSections);
        command.Options.Add(ExcludeSections);
        command.Options.Add(Columns);
        command.Options.Add(Fields);
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
    /// Adds all common options for a full inspection command (JSON, Markout, verbose, sections, NuGet).
    /// </summary>
    public void AddAllOptionsTo(Command command)
    {
        command.Options.Add(Json);
        command.Options.Add(Markout);
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
    /// Parses include sections from parse result.
    /// </summary>
    public HashSet<string>? ParseIncludeSections(ParseResult parseResult)
        => OptionParsers.ParseIncludeSections(parseResult, IncludeSections);

    /// <summary>
    /// Parses exclude sections from parse result.
    /// </summary>
    public HashSet<string>? ParseExcludeSections(ParseResult parseResult)
        => OptionParsers.ParseSectionList(parseResult.GetValue(ExcludeSections));

    /// <summary>
    /// Parses column list from parse result.
    /// Returns null if not specified, empty array for bare --columns (discovery), or populated array.
    /// </summary>
    public string[]? ParseColumns(ParseResult parseResult)
        => ParseProjectionList(parseResult, Columns);

    /// <summary>
    /// Parses field list from parse result.
    /// Returns null if not specified, empty array for bare --fields (discovery), or populated array.
    /// </summary>
    public string[]? ParseFields(ParseResult parseResult)
        => ParseProjectionList(parseResult, Fields);

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
