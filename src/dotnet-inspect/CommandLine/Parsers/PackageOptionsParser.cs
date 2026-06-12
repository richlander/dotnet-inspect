using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the package command options.
/// Extracts options and builds InspectionOptions for package inspection.
/// </summary>
public static class PackageOptionsParser
{
    /// <summary>
    /// Arguments container for package command options.
    /// </summary>
    public record PackageCommandArgs(
        Argument<string[]> PackageNameArg,
        Option<bool> DependenciesOption,
        Option<bool> LayoutOption,
        Option<bool> FilesOption,
        Option<bool> TfmsOption,
        Option<bool> LibOption,
        Option<bool> ToolsOption,
        Option<string?> LibraryOption,
        Option<bool> AllLibrariesOption,
        Option<int?> VersionsOption,
        Option<bool> PrereleaseOption,
        Option<bool> ReadmeOption,
        Option<string?> TfmOption,
        Option<string?> VersionOption,
        Option<bool> LatestVersionOption,
        Option<string?> OutOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption);

    /// <summary>
    /// Result of parsing package command options.
    /// </summary>
    public abstract record PackageParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found in positional args.
    /// </summary>
    public record UnrecognizedOption(string Option) : PackageParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(InspectionOptions Options, Verbosity Verbosity) : PackageParseResult;

    /// <summary>
    /// Parses package command options.
    /// </summary>
    public static PackageParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        var packageArgs = parseResult.GetValue(args.PackageNameArg) ?? [];

        // Check for unrecognized options in positional args
        var badOption = packageArgs.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        var explicitVersion = parseResult.GetValue(args.VersionOption);
        bool showLatestVersion = parseResult.GetValue(args.LatestVersionOption);
        var libraryValue = parseResult.GetValue(args.LibraryOption);
        var packageLibrary = parseResult.GetResult(args.LibraryOption) is { Implicit: false }
            ? libraryValue ?? ""
            : null;

        // Bare --version (no value): treat as version query (cache-first)
        bool bareVersion = explicitVersion == null && parseResult.GetResult(args.VersionOption) is { Implicit: false };

        var versionsValue = parseResult.GetValue(args.VersionsOption);
        bool showVersions = bareVersion || showLatestVersion || parseResult.GetResult(args.VersionsOption) is { Implicit: false };

        var verbosity = opts.ParseVerbosity(parseResult);

        var options = new InspectionOptions
        {
            PackageArgs = packageArgs,
            ExplicitVersion = explicitVersion,
            ShowDependencies = parseResult.GetValue(args.DependenciesOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            PackageLibrary = packageLibrary,
            AllLibraries = parseResult.GetValue(args.AllLibrariesOption),
            ListLayout = parseResult.GetValue(args.LayoutOption),
            ListFiles = parseResult.GetValue(args.FilesOption),
            ListTfms = parseResult.GetValue(args.TfmsOption),
            ScopeLib = parseResult.GetValue(args.LibOption),
            ScopeTools = parseResult.GetValue(args.ToolsOption),
            ListVersions = showVersions,
            IncludePrerelease = parseResult.GetValue(args.PrereleaseOption),
            ShowReadme = parseResult.GetValue(args.ReadmeOption),
            OutputPath = parseResult.GetValue(args.OutOption),
            Limit = (bareVersion || showLatestVersion) ? 1 : versionsValue,
            ForceLatest = showLatestVersion,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || verbosity != Verbosity.Minimal || options.Select != null || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.Limit != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        options = options with { TipLevel = tipLevel };

        return new Success(options, verbosity);
    }
}
