using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector;

/// <summary>
/// Parses CLI option values into typed domain objects.
/// </summary>
public static class OptionParsers
{
    public static Verbosity ParseVerbosity(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Verbosity.Minimal;

        var v = value.TrimStart(':').ToLowerInvariant();
        return v switch
        {
            "q" or "quiet" => Verbosity.Quiet,
            "m" or "minimal" => Verbosity.Minimal,
            "n" or "normal" => Verbosity.Normal,
            "d" or "detailed" => Verbosity.Detailed,
            _ => Verbosity.Minimal
        };
    }

    public static TipLevel ParseTipLevel(string? value, bool optionPresent)
    {
        // Bare -T (no value) means quiet
        if (optionPresent && string.IsNullOrEmpty(value))
            return TipLevel.Quiet;

        // --tips flag takes precedence, then DOTNET_INSPECT_TIPS env var, then default
        if (string.IsNullOrEmpty(value))
            value = Environment.GetEnvironmentVariable("DOTNET_INSPECT_TIPS");
        if (string.IsNullOrEmpty(value)) return TipLevel.Minimal;

        var v = value.TrimStart(':').ToLowerInvariant();
        return v switch
        {
            "q" or "quiet" => TipLevel.Quiet,
            "m" or "minimal" => TipLevel.Minimal,
            "d" or "detailed" => TipLevel.Detailed,
            _ => TipLevel.Minimal
        };
    }

    public static HashSet<string>? ParseSectionList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var v = value.TrimStart(':');
        HashSet<string> sections = [];
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                sections.Add(trimmed);
        }
        return sections.Count > 0 ? sections : null;
    }

    /// <summary>
    /// Parses -s option, distinguishing "not specified" (null) from bare "-s" (empty set).
    /// </summary>
    public static HashSet<string>? ParseIncludeSections(ParseResult parseResult, Option<string?> option)
    {
        var sections = ParseSectionList(parseResult.GetValue(option));
        // Bare -s with no value: return empty set (signals "list sections")
        if (sections == null && parseResult.GetResult(option) != null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return sections;
    }

    /// <summary>
    /// Creates NuGetSourceOptions from parsed command line values.
    /// </summary>
    public static NuGetSourceOptions ParseNuGetSourceOptions(
        ParseResult parseResult,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var sources = parseResult.GetValue(sourceOption) ?? [];
        var addSources = parseResult.GetValue(addSourceOption) ?? [];
        var configFile = parseResult.GetValue(nugetConfigOption);

        if (sources.Length == 0 && addSources.Length == 0 && configFile == null)
        {
            return NuGetSourceOptions.Default;
        }

        return new NuGetSourceOptions
        {
            Sources = sources,
            AdditionalSources = addSources,
            ConfigFile = configFile
        };
    }
}
