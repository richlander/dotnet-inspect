using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the diff command options.
/// Extracts options and builds DiffOptions for API comparison.
/// </summary>
public static class DiffOptionsParser
{
    /// <summary>
    /// Arguments container for diff command options.
    /// </summary>
    public record DiffCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> PlatformOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string[]> TypeFilterOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<bool> NameOnlyOption,
        Option<bool> BreakingOption,
        Option<bool> AdditiveOption,
        Option<bool> LegendOption);

    /// <summary>
    /// Result of parsing diff command options.
    /// </summary>
    public abstract record DiffParseResult;

    /// <summary>
    /// Indicates a version number error (positional looks like version).
    /// </summary>
    public record VersionNumberError(string Value, string VersionRange) : DiffParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(DiffOptions Options, Verbosity Verbosity, TipLevel TipLevel) : DiffParseResult;

    /// <summary>
    /// Parses diff command options.
    /// </summary>
    public static DiffParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        DiffCommandArgs args)
    {
        var argsValue = parseResult.GetValue(args.ArgsArg) ?? [];
        var explicitPackage = parseResult.GetValue(args.PackageOption);
        var explicitPlatform = parseResult.GetValue(args.PlatformOption);
        bool hasExplicitSource = explicitPackage != null || explicitPlatform != null;

        string? packageVersionRange = explicitPackage;
        string? platformVersionRange = explicitPlatform;
        string? typeName = null;

        if (hasExplicitSource)
        {
            // All positionals are type filters
            if (argsValue.Length >= 1) typeName = argsValue[0];
        }
        else
        {
            // First positional is the package version range
            if (argsValue.Length >= 1) packageVersionRange = argsValue[0];
            if (argsValue.Length >= 2) typeName = argsValue[1];

            if (CommandLineHelpers.LooksLikeVersionNumber(typeName))
                return new VersionNumberError(typeName!, packageVersionRange!);
        }

        // Build type filter set
        var typeFilterValues = parseResult.GetValue(args.TypeFilterOption);
        HashSet<string> typeFilter = [];
        if (typeFilterValues?.Length > 0 || !string.IsNullOrEmpty(typeName))
        {
            typeFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(typeName))
                typeFilter.Add(typeName);
            if (typeFilterValues != null)
            {
                foreach (var t in typeFilterValues)
                    typeFilter.Add(t);
            }
        }

        var options = new DiffOptions
        {
            PackageVersionRange = packageVersionRange,
            PlatformVersionRange = platformVersionRange,
            Framework = parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            TypeFilter = typeFilter,
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            NameOnly = parseResult.GetValue(args.NameOnlyOption),
            Breaking = parseResult.GetValue(args.BreakingOption),
            Additive = parseResult.GetValue(args.AdditiveOption),
            Legend = parseResult.GetValue(args.LegendOption),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Rows = opts.ParseRows(parseResult),
        };

        var verbosity = opts.ParseVerbosity(parseResult);
        var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet || options.Discover != null || options.Select != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

        return new Success(options, verbosity, tipLevel);
    }

    /// <summary>
    /// Builds tips for successful diff execution.
    /// </summary>
    public static List<Tip> BuildTips(DiffOptions options, HashSet<string>? typeFilter)
    {
        List<Tip> tips = [];
        var versionRange = options.PackageVersionRange ?? options.PlatformVersionRange;
        var sourceFlag = options.PackageVersionRange != null ? "--package" : "--platform";

        if (typeFilter != null && typeFilter.Count > 0)
            tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange}", "diff all types"));

        if (versionRange != null)
        {
            var atIdx = versionRange.IndexOf('@');
            var dotDotIdx = versionRange.IndexOf("..", StringComparison.Ordinal);
            if (atIdx > 0 && dotDotIdx > atIdx)
            {
                var pkgName = versionRange[..atIdx];
                var toVersion = versionRange[(dotDotIdx + 2)..];
                if (!options.OneLine && !options.NameOnly)
                    tips.Add(new(TypeCommand.Name, $"<TypeName> {sourceFlag} {pkgName}@{toVersion} --shape", "view current type shape"));
                if (!options.OneLine)
                    tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange} --table", "summary statistics"));
            }
        }

        return tips;
    }
}
