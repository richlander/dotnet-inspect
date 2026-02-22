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
    public Option<bool> Markdown { get; } = new("--markdown") { Description = "Full markdown output (default is oneline)" };

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };

    // Output control options
    public Option<int?> Limit { get; } = new("-n") { Description = "Limit output lines (like head -n)" };
    public Option<string?> Tips { get; }

    // Projection options
    public Option<string?> Select { get; }
    public Option<string?> Field { get; }

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

        Select = new Option<string?>("-S")
        {
            Description = "Select sections/fields/columns by name (comma-separated). Use -S alone to list.",
            Arity = ArgumentArity.ZeroOrOne
        };
        Select.Aliases.Add("--select");

        Field = new Option<string?>("-F")
        {
            Description = "Select fields by name (comma-separated, bypasses section matching). Use -F alone to list.",
            Arity = ArgumentArity.ZeroOrOne
        };
        Field.Aliases.Add("--field");
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
        command.Options.Add(Select);
        command.Options.Add(Field);
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
    /// Parses select list from parse result.
    /// Returns null if not specified, empty array for bare -S (discovery), or populated array.
    /// </summary>
    public string[]? ParseSelect(ParseResult parseResult)
        => ParseProjectionList(parseResult, Select);

    /// <summary>
    /// Parses field list from parse result.
    /// Returns null if not specified, empty array for bare -F (discovery), or populated array.
    /// </summary>
    public string[]? ParseField(ParseResult parseResult)
        => ParseProjectionList(parseResult, Field);

    /// <summary>
    /// Resolves -S and -F into a unified (Select, PreferFields) pair.
    /// -F wins over -S when both specified (values go to Select, PreferFields=true).
    /// </summary>
    public (string[]? Select, bool PreferFields) ResolveSelectAndField(ParseResult parseResult)
    {
        var field = ParseField(parseResult);
        if (field != null)
            return (field, true);

        return (ParseSelect(parseResult), false);
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
