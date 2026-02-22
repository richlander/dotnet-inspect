using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the router command that auto-resolves package or platform library.
/// Extracts options and determines the routing action.
/// </summary>
public static class RouterOptionsParser
{
    /// <summary>
    /// Arguments container for router command options.
    /// </summary>
    public record RouterCommandArgs(
        Argument<string[]> PackageNameArg,
        Option<bool> VersionOption,
        Option<bool> LatestVersionOption,
        Option<int?> VersionsOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption);

    /// <summary>
    /// Result of parsing router command options.
    /// </summary>
    public abstract record RouterParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : RouterParseResult;

    /// <summary>
    /// Indicates selectable names should be listed.
    /// </summary>
    public record ListSelect : RouterParseResult;

    /// <summary>
    /// Indicates an error occurred during parsing.
    /// </summary>
    public record ParseError(string Message) : RouterParseResult;

    /// <summary>
    /// Route to assembly command for a .dll file.
    /// </summary>
    public record RouteToAssemblyFile(AssemblyOptions Options) : RouterParseResult;

    /// <summary>
    /// Route to assembly command for a platform library.
    /// </summary>
    public record RouteToPlatformAssembly(AssemblyOptions Options, string BareName, Verbosity Verbosity) : RouterParseResult;

    /// <summary>
    /// Handle --version query with cache check first.
    /// </summary>
    public record HandleVersionQuery(
        string BareName,
        string? ExplicitVersion,
        bool ForceLatest,
        NuGetSourceOptions SourceOptions) : RouterParseResult;

    /// <summary>
    /// Route to package command.
    /// </summary>
    public record RouteToPackage(InspectionOptions Options, string BareName, Verbosity Verbosity) : RouterParseResult;

    /// <summary>
    /// Parses router command options and determines the routing action.
    /// </summary>
    public static RouterParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        RouterCommandArgs args)
    {
        var packageArgs = parseResult.GetValue(args.PackageNameArg) ?? [];

        if (packageArgs.Length < 1)
        {
            if (parseResult.GetResult(opts.Select) != null && parseResult.GetValue(opts.Select) == null)
                return new ListSelect();
            return new ShowHelp();
        }

        var name = packageArgs[0];

        // Detect version number passed as a separate positional argument
        if (packageArgs.Length >= 2 && CommandLineHelpers.LooksLikeVersionNumber(packageArgs[1]))
            return new ParseError($"Error: '{packageArgs[1]}' looks like a version number. Use '{name}@{packageArgs[1]}' to specify a version.");

        // Route file paths to the appropriate command
        if (CommandLineHelpers.TryClassifyAsFilePath(name, out var dllPath, out var nupkgPath))
        {
            if (dllPath != null)
            {
                var assemblyOptions = new AssemblyOptions
                {
                    AssemblyName = dllPath,
                    IncludeMetadata = true,
                    JsonOutput = parseResult.GetValue(opts.Json),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    Verbosity = opts.ParseVerbosity(parseResult),
                    IncludeSections = null,
                    ExcludeSections = null,
                    Select = opts.ParseSelect(parseResult),
                    PreferFields = parseResult.GetResult(opts.Field) != null,
                };
                return new RouteToAssemblyFile(assemblyOptions);
            }
            // .nupkg falls through to package command below
        }

        var pkgInfo = SharedParsers.ParsePackageVersion(name);
        var bareName = pkgInfo.BareName;
        var explicitVersion = pkgInfo.ExplicitVersion;
        var hasExplicitVersion = pkgInfo.HasExplicitVersion;
        var forceLatest = pkgInfo.ForceLatest;

        // Version query flags
        bool showVersion = parseResult.GetValue(args.VersionOption);
        bool showLatestVersion = parseResult.GetValue(args.LatestVersionOption);
        var routerVersionsValue = parseResult.GetValue(args.VersionsOption);
        bool showVersions = parseResult.GetResult(args.VersionsOption) is { Implicit: false };
        bool isVersionQuery = showVersion || showLatestVersion || showVersions;

        var verbosity = opts.ParseVerbosity(parseResult);

        // Platform candidate check (skip for version queries)
        // Note: Qualified type names (e.g., System.Text.Json.JsonSerializer) are also platform candidates
        // and will be routed here. The ExecutePlatformAssemblyAsync handler will check for qualified
        // type names if platform resolution fails.
        if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName))
        {
            var assemblyOptions = BuildPlatformAssemblyOptions(parseResult, opts, bareName, hasExplicitVersion, explicitVersion);
            return new RouteToPlatformAssembly(assemblyOptions, bareName, verbosity);
        }

        // --version query
        if (showVersion)
            return new HandleVersionQuery(bareName, explicitVersion, forceLatest, opts.ParseNuGetSourceOptions(parseResult));

        // Fall through to package command
        bool useBareName = forceLatest || showLatestVersion;
        var options = new InspectionOptions
        {
            PackageArgs = useBareName ? [bareName] : packageArgs,
            ListVersions = showLatestVersion || showVersions,
            Limit = showLatestVersion ? 1 : routerVersionsValue,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = parseResult.GetValue(args.OneLineOption),
            Markdown = opts.ParseMarkdown(parseResult),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            IncludeSections = null,
            ExcludeSections = null,
            Select = opts.ParseSelect(parseResult),
            PreferFields = parseResult.GetResult(opts.Field) != null,
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            ForceLatest = forceLatest || showLatestVersion
        };

        var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || ArgumentPreprocessor.HeadLines != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        options = options with { TipLevel = tipLevel };

        return new RouteToPackage(options, bareName, verbosity);
    }

    private static AssemblyOptions BuildPlatformAssemblyOptions(
        ParseResult parseResult,
        SharedOptions opts,
        string bareName,
        bool hasExplicitVersion,
        string? explicitVersion)
    {
        // Build framework spec if explicit version given
        string? platformFrameworkSpec = null;
        if (hasExplicitVersion)
        {
            var (_, discoveredFramework, _, _) = PlatformResolver.ResolveAssembly(bareName);
            if (discoveredFramework != null)
                platformFrameworkSpec = $"{discoveredFramework}@{explicitVersion}";
        }

        return new AssemblyOptions
        {
            PlatformAssembly = bareName,
            PlatformFramework = platformFrameworkSpec,
            JsonOutput = parseResult.GetValue(opts.Json),
            Markdown = opts.ParseMarkdown(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            IncludeSections = null,
            ExcludeSections = null,
            Select = opts.ParseSelect(parseResult),
            PreferFields = parseResult.GetResult(opts.Field) != null,
        };
    }
}
