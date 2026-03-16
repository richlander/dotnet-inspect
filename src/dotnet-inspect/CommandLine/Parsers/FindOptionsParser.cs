using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the find command options.
/// Extracts options and builds FindOptions for type search.
/// </summary>
public static class FindOptionsParser
{
    /// <summary>
    /// Arguments container for find command options.
    /// </summary>
    public record FindCommandArgs(
        Argument<string?> PatternArg,
        Option<string[]> PackageOption,
        Option<string[]> AssemblyOption,
        Option<bool> PlatformOption,
        Option<bool> ExtensionsOption,
        Option<bool> AspNetCoreOption,
        Option<bool> CuratedOption,
        Option<string[]> ProjectOption,
        Option<string[]> BinOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> TypeFilterOption,
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<string?> PackagePrefixOption);

    /// <summary>
    /// Result of parsing find command options.
    /// </summary>
    public abstract record FindParseResult;

    /// <summary>
    /// Indicates help with tips should be shown (no pattern provided).
    /// </summary>
    public record ShowHelpWithTips : FindParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(FindOptions Options, Verbosity Verbosity, TipLevel TipLevel) : FindParseResult;

    /// <summary>
    /// Parses find command options asynchronously (due to package prefix resolution).
    /// </summary>
    public static async Task<FindParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        FindCommandArgs args)
    {
        var pattern = parseResult.GetValue(args.PatternArg);

        if (string.IsNullOrEmpty(pattern))
            return new ShowHelpWithTips();

        var packagePrefix = parseResult.GetValue(args.PackagePrefixOption);
        var packages = await CommandLineHelpers.MergeWithPrefixPackagesAsync(
            parseResult.GetValue(args.PackageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
        var assemblies = parseResult.GetValue(args.AssemblyOption) ?? [];
        var projects = parseResult.GetValue(args.ProjectOption) ?? [];
        var binPaths = parseResult.GetValue(args.BinOption) ?? [];

        var scopeFlags = new ScopeResolver.ScopeFlags(
            Platform: parseResult.GetValue(args.PlatformOption),
            Extensions: parseResult.GetValue(args.ExtensionsOption),
            AspNetCore: parseResult.GetValue(args.AspNetCoreOption),
            Curated: parseResult.GetValue(args.CuratedOption));
        var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix,
            hasOtherScopeIndicators: projects.Length > 0 || binPaths.Length > 0);

        var options = new FindOptions
        {
            Pattern = pattern!,
            Packages = scope.Packages,
            Assemblies = assemblies,
            PlatformAssemblies = [],
            PlatformFrameworks = scope.Frameworks,
            Projects = projects,
            BinPaths = binPaths,
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            Limit = CommandLineHelpers.ParseTypeLimit(parseResult.GetValue(args.TypeFilterOption)),
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult, args.OneLineOption),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Tree = opts.ParseTree(parseResult),
            PackagePrefix = packagePrefix,
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        var verbosity = opts.ParseVerbosity(parseResult);
        var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.Limit != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

        return new Success(options, verbosity, tipLevel);
    }

    /// <summary>
    /// Builds tips for successful find execution.
    /// </summary>
    public static List<Tip> BuildTips(FindOptions options, string? pattern)
    {
        var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
        var sourceFlag = pkg != null ? $"--package {pkg}" : "--platform";

        return
        [
            new(MemberCommand.Name, $"<TypeName> {sourceFlag}", "inspect type members"),
            new(FindCommand.Name, $"{pattern} {sourceFlag} --oneline", "compact output"),
            new(FindCommand.Name, $"{pattern} {sourceFlag} -v:d", "detailed results")
        ];
    }
}
