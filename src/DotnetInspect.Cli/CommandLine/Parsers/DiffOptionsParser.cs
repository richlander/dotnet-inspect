using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

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
        Option<string?> LibraryOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<bool> HistoryOption,
        Option<string[]> AtOption,
        Option<int?> MaxProbesOption,
        Option<int?> SamplePercentOption,
        Option<bool> MajorVersionsOption,
        Option<bool> PrereleaseOption,
        Option<bool> CountOption,
        Option<string[]> TypeFilterOption,
        Option<string[]> MemberFilterOption,
        Option<bool> NoHeaderOption,
        Option<bool> NameOnlyOption,
        Option<bool> BreakingOption,
        Option<bool> AdditiveOption,
        Option<bool> ChangedOption,
        Option<bool> AllocRegressionsOption,
        Option<bool> PdbSourceOption,
        Option<bool> LegacyAuthoredSourceOption,
        Option<string?> FindingOption,
        Option<bool> LegendOption,
        Option<string[]> RepoOption,
        Option<bool> CompactOption);

    /// <summary>
    /// Result of parsing diff command options.
    /// </summary>
    public abstract record DiffParseResult;

    /// <summary>
    /// Indicates a version number error (positional looks like version).
    /// </summary>
    public record VersionNumberError(string Value, string VersionRange) : DiffParseResult;

    /// <summary>
    /// Indicates invalid command input that depends on the selected Diff mode.
    /// </summary>
    public record Invalid(string Message) : DiffParseResult;

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
        DiffCommandArgs args,
        RowSelectionIntent<string>? semanticRowSelection = null)
    {
        var argsValue = parseResult.GetValue(args.ArgsArg) ?? [];
        var explicitPackage = parseResult.GetValue(args.PackageOption);
        var explicitPlatform = parseResult.GetValue(args.PlatformOption);
        var explicitLibrary = parseResult.GetValue(args.LibraryOption);
        bool hasExplicitSource = explicitPackage != null || explicitPlatform != null || explicitLibrary != null;
        bool history = parseResult.GetValue(args.HistoryOption);
        int maximumPositionals = hasExplicitSource ? 1 : 2;
        if (history && argsValue.Length > maximumPositionals)
        {
            return new Invalid(
                "Diff History accepts one package range and exactly one Type focus.");
        }

        string? packageVersionRange = explicitPackage;
        string? platformVersionRange = explicitPlatform;
        string? libraryVersionRange = explicitLibrary;
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
        var memberFilterValues = parseResult.GetValue(args.MemberFilterOption);
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

        HashSet<string> memberFilter = [];
        if (memberFilterValues?.Length > 0)
            memberFilter = new HashSet<string>(memberFilterValues, StringComparer.OrdinalIgnoreCase);

        bool envelopeOutput = parseResult.GetValue(opts.Envelope);
        var options = new DiffOptions
        {
            PackageVersionRange = packageVersionRange,
            PlatformVersionRange = platformVersionRange,
            LibraryVersionRange = libraryVersionRange,
            Framework = parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            History = history,
            At = parseResult.GetValue(args.AtOption) ?? [],
            MaxProbes = parseResult.GetValue(args.MaxProbesOption),
            SamplePercent =
                parseResult.GetValue(args.SamplePercentOption),
            MajorVersions =
                parseResult.GetValue(args.MajorVersionsOption),
            IncludePrerelease = parseResult.GetValue(args.PrereleaseOption),
            Count = parseResult.GetValue(args.CountOption),
            SemanticRowSelection = semanticRowSelection,
            Verbose = parseResult.GetValue(opts.Verbose),
            TypeFilter = typeFilter,
            MemberFilter = memberFilter,
            Tabular = !envelopeOutput && opts.ResolveTabular(parseResult),
            Tsv = !envelopeOutput && opts.ResolveTsv(parseResult),
            Jsonl = !envelopeOutput && opts.ResolveJsonl(parseResult),
            JsonOutput = !envelopeOutput && opts.ResolveFormat(parseResult) == OutputFormat.Json,
            EnvelopeOutput = envelopeOutput,
            CompactJson = parseResult.GetValue(args.CompactOption),
            VerbosityExplicitlySet =
                parseResult.GetResult(opts.Verbosity) is { Implicit: false },
            HasRenderedLineWindow =
                ArgumentPreprocessor.HeadLines is not null
                || ArgumentPreprocessor.TailLines is not null,
            TabularExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            NameOnly = parseResult.GetValue(args.NameOnlyOption),
            Breaking = parseResult.GetValue(args.BreakingOption),
            Additive = parseResult.GetValue(args.AdditiveOption),
            ChangedOnly = parseResult.GetValue(args.ChangedOption),
            AllocRegressionsOnly = parseResult.GetValue(args.AllocRegressionsOption),
            IncludePdbSource = parseResult.GetValue(args.PdbSourceOption)
                || parseResult.GetValue(args.LegacyAuthoredSourceOption),
            Finding = parseResult.GetValue(args.FindingOption),
            Legend = parseResult.GetValue(args.LegendOption),
            SourceRepositories = parseResult.GetValue(args.RepoOption) ?? [],
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Schema = opts.ParseSchema(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            SelectDefault = opts.ParseSelectDefault(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Rows = opts.ParseRows(parseResult),
        };

        var verbosity = opts.ParseVerbosity(parseResult);
        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || verbosity == Verbosity.Quiet || options.Discover != null || options.Select != null || options.SelectDefault || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
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
                if (!options.Tabular && !options.NameOnly)
                    tips.Add(new(TypeCommand.Name, $"<TypeName> {sourceFlag} {pkgName}@{toVersion} --tree", "view current type tree"));
                if (!options.Tabular)
                    tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange} --table", "summary statistics"));
            }
        }

        return tips;
    }
}
