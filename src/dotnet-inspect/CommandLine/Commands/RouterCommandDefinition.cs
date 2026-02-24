using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the hidden router command that auto-resolves package or platform library.
/// </summary>
public static class RouterCommandDefinition
{
    /// <summary>
    /// Creates the router command with all options configured.
    /// </summary>
    public static Command Create(SharedOptions opts)
    {
        var routerCommand = new Command("router", "Auto-resolve package or platform library") { Hidden = true };

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "Package or platform library name",
            Arity = ArgumentArity.ZeroOrMore
        };

        routerCommand.Arguments.Add(packageNameArg);
        opts.AddAllOptionsTo(routerCommand);

        var routerOneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var routerNoHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        routerCommand.Options.Add(routerOneLineOption);
        routerCommand.Options.Add(routerNoHeaderOption);

        // Version query options for the router
        var routerVersionOption = new Option<bool>("--version") { Description = "Show resolved version" };
        routerCommand.Options.Add(routerVersionOption);
        var routerLatestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest version from nuget.org" };
        routerCommand.Options.Add(routerLatestVersionOption);
        var routerVersionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        routerVersionsOption.DefaultValueFactory = _ => null;
        routerCommand.Options.Add(routerVersionsOption);

        var commandArgs = new RouterOptionsParser.RouterCommandArgs(
            packageNameArg, routerVersionOption, routerLatestVersionOption, routerVersionsOption,
            routerOneLineOption, routerNoHeaderOption);

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var result = RouterOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case RouterOptionsParser.ShowHelp:
                    new HelpAction().Invoke(parseResult);
                    return 0;

                case RouterOptionsParser.Discovery d:
                    var routerSchema = new MarkoutContext().GetSchemaInfo<InspectionResultView>();
                    SelectResolver.Discover(d.Select, d.Columns, null, routerSchema);
                    return 0;

                case RouterOptionsParser.ParseError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case RouterOptionsParser.RouteToAssemblyFile route:
                    return await AssemblyCommand.ExecuteAsync(route.Options);

                case RouterOptionsParser.RouteToPlatformAssembly route:
                    return await ExecutePlatformAssemblyAsync(route, opts, parseResult);

                case RouterOptionsParser.HandleVersionQuery query:
                    return await ExecuteVersionQueryAsync(query, opts, parseResult, routerVersionsOption);

                case RouterOptionsParser.RouteToPackage route:
                    return await ExecutePackageCommandAsync(route);

                default:
                    return 1;
            }
        });

        return routerCommand;
    }

    private static async Task<int> ExecutePlatformAssemblyAsync(
        RouterOptionsParser.RouteToPlatformAssembly route,
        SharedOptions opts,
        ParseResult parseResult)
    {
        bool verbose = route.Options.Verbose;
        Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
        var client = HttpClientFactory.Shared;

        var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
            route.BareName, client, log, route.Options.PlatformFramework);

        if (resolvedPath != null && resolvedError == null)
        {
            var assemblyExitCode = await AssemblyCommand.ExecuteAsync(route.Options);

            if (assemblyExitCode == 0 && !route.Options.JsonOutput)
            {
                var platformTipLevel = route.Verbosity != Verbosity.Minimal || route.IncludeSections != null || ArgumentPreprocessor.HeadLines != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
                TipWriter.WritePlatformTips(route.BareName, platformTipLevel, route.Verbosity);
            }

            return assemblyExitCode;
        }

        // Platform resolution failed - check if this is a qualified type name
        // e.g., System.Text.Json.JsonSerializer -> type JsonSerializer --platform System.Text.Json
        if (PlatformResolver.TryParseQualifiedTypeName(route.BareName, out var qtAssembly, out var qtType))
        {
            var typeOptions = new ApiOptions
            {
                TypeName = qtType,
                PlatformAssembly = qtAssembly,
                JsonOutput = route.Options.JsonOutput,
                Verbose = route.Options.Verbose,
                Verbosity = route.Verbosity,
                IncludeSections = route.IncludeSections,
                ExcludeSections = route.Options.ExcludeSections,
                Select = route.Options.Select,
                Columns = route.Options.Columns,
                TipLevel = ArgumentPreprocessor.HeadLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
            };

            return await ApiCommand.ExecuteAsync(typeOptions);
        }

        // Fall through to package command.
        // Names like "System.CommandLine" are platform candidates (because they start with "System.")
        // but aren't actually platform libraries. When platform resolution fails, we fall through here.
        // Use OriginalArg (e.g., "System.CommandLine@2.0.2") to preserve any explicit version.
        var options = new InspectionOptions
        {
            PackageArgs = [route.OriginalArg],
            JsonOutput = route.Options.JsonOutput,
            OneLine = route.OneLine,
            NoHeader = route.NoHeader,
            Verbose = route.Options.Verbose,
            Verbosity = route.Verbosity,
            IncludeSections = route.IncludeSections,
            ExcludeSections = route.Options.ExcludeSections,
            Select = route.Options.Select,
            Columns = route.Options.Columns,
            SourceOptions = route.Options.SourceOptions
        };

        var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.IncludeSections != null || ArgumentPreprocessor.HeadLines != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        options = options with { TipLevel = tipLevel };

        var exitCode = await PackageCommand.ExecuteAsync(options);

        if (exitCode == 0 && !options.IsRawOutput)
            TipWriter.WritePackageTips(route.BareName, tipLevel, options.Verbosity);

        return exitCode;
    }

    private static async Task<int> ExecuteVersionQueryAsync(
        RouterOptionsParser.HandleVersionQuery query,
        SharedOptions opts,
        ParseResult parseResult,
        Option<int?> routerVersionsOption)
    {
        if (!query.ForceLatest)
        {
            if (query.ExplicitVersion != null)
            {
                // Check app cache and NuGet cache
                if (NuGetCache.TryGetCachedPackage(query.BareName, query.ExplicitVersion) != null)
                {
                    Console.WriteLine(query.ExplicitVersion);
                    return 0;
                }

                // Check NuGet version API
                var allVersions = await PackageExtractor.GetVersionsAsync(
                    HttpClientFactory.Shared, query.BareName, includePrerelease: true, limit: null,
                    log: null, sourceOptions: query.SourceOptions);

                if (allVersions != null && allVersions.Any(v => string.Equals(v, query.ExplicitVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine(query.ExplicitVersion);
                    return 0;
                }

                // Differentiate bad package from bad version
                if (allVersions == null || allVersions.Count == 0)
                    Console.Error.WriteLine($"Error: Package '{query.BareName}' not found.");
                else
                    Console.Error.WriteLine($"Error: Version '{query.ExplicitVersion}' of package '{query.BareName}' not found. Use --versions to see available versions.");
                return 1;
            }
            else
            {
                // Bare name: use newest cached version
                var cachedVersion = NuGetCache.TryGetLatestCachedVersion(query.BareName);
                if (cachedVersion != null)
                {
                    Console.WriteLine(cachedVersion);
                    return 0;
                }
            }
        }

        // No cache hit, or @latest: fall through to --latest-version (version API query)
        var options = new Options.InspectionOptions
        {
            PackageArgs = [query.BareName],
            ListVersions = true,
            Limit = 1,
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = query.SourceOptions,
            ForceLatest = true
        };

        return await PackageCommand.ExecuteAsync(options);
    }

    private static async Task<int> ExecutePackageCommandAsync(RouterOptionsParser.RouteToPackage route)
    {
        var exitCode = await PackageCommand.ExecuteAsync(route.Options);

        if (exitCode == 0 && !route.Options.IsRawOutput)
            TipWriter.WritePackageTips(route.BareName, route.Options.TipLevel, route.Verbosity);

        return exitCode;
    }
}
