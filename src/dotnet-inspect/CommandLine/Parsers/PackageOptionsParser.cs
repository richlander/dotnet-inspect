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
        Option<int?> VersionsOption,
        Option<bool> PrereleaseOption,
        Option<bool> ReadmeOption,
        Option<string?> TfmOption,
        Option<string?> VersionOption,
        Option<string?> OutOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption);

    /// <summary>
    /// Result of parsing package command options.
    /// </summary>
    public record PackageParseResult(InspectionOptions Options, Verbosity Verbosity);

    /// <summary>
    /// Parses package command options.
    /// </summary>
    public static PackageParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        var packageArgs = parseResult.GetValue(args.PackageNameArg) ?? [];
        var explicitVersion = parseResult.GetValue(args.VersionOption);

        // Bare --version (no value): treat as --versions 1
        bool bareVersion = explicitVersion == null && parseResult.GetResult(args.VersionOption) is { Implicit: false };

        var versionsValue = parseResult.GetValue(args.VersionsOption);
        bool showVersions = bareVersion || parseResult.GetResult(args.VersionsOption) is { Implicit: false };

        var verbosity = opts.ParseVerbosity(parseResult);
        var (select, preferFields) = opts.ResolveSelectAndField(parseResult);

        var options = new InspectionOptions
        {
            PackageArgs = packageArgs,
            ExplicitVersion = explicitVersion,
            ShowDependencies = parseResult.GetValue(args.DependenciesOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            ListLayout = parseResult.GetValue(args.LayoutOption),
            ListFiles = parseResult.GetValue(args.FilesOption),
            ListTfms = parseResult.GetValue(args.TfmsOption),
            ScopeLib = parseResult.GetValue(args.LibOption),
            ScopeTools = parseResult.GetValue(args.ToolsOption),
            ListVersions = showVersions,
            IncludePrerelease = parseResult.GetValue(args.PrereleaseOption),
            ShowReadme = parseResult.GetValue(args.ReadmeOption),
            OutputPath = parseResult.GetValue(args.OutOption),
            Limit = bareVersion ? 1 : versionsValue,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = parseResult.GetValue(args.OneLineOption),
            Markdown = parseResult.GetValue(opts.Markdown),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            IncludeSections = null,
            ExcludeSections = null,
            Select = select,
            PreferFields = preferFields,
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        var tipLevel = options.IsRawOutput || verbosity != Verbosity.Minimal || ArgumentPreprocessor.HeadLines != null || options.Limit != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        options = options with { TipLevel = tipLevel };

        return new PackageParseResult(options, verbosity);
    }
}
