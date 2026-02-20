using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

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

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];

            if (packageArgs.Length < 1)
            {
                new HelpAction().Invoke(parseResult);
                return 0;
            }

            var name = packageArgs[0];

            // Detect version number passed as a separate positional argument
            if (packageArgs.Length >= 2 && CommandLineHelpers.LooksLikeVersionNumber(packageArgs[1]))
            {
                Console.Error.WriteLine($"Error: '{packageArgs[1]}' looks like a version number. Use '{name}@{packageArgs[1]}' to specify a version.");
                return 1;
            }

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
                        IncludeSections = opts.ParseIncludeSections(parseResult),
                        ExcludeSections = opts.ParseExcludeSections(parseResult)
                    };
                    return await AssemblyCommand.ExecuteAsync(assemblyOptions);
                }
                // .nupkg falls through to package command below
            }

            bool hasExplicitVersion = name.Contains('@');
            var bareName = hasExplicitVersion ? name[..name.IndexOf('@')] : name;
            var explicitVersion = hasExplicitVersion ? name[(name.IndexOf('@') + 1)..] : null;

            // @latest forces network resolution, bypassing cache-first
            bool forceLatest = string.Equals(explicitVersion, "latest", StringComparison.OrdinalIgnoreCase);
            if (forceLatest)
            {
                hasExplicitVersion = false;
                explicitVersion = null;
            }

            // Platform candidate: download ref packs, then resolve
            // Skip platform probing for version queries (NuGet package operations)
            bool showVersion = parseResult.GetValue(routerVersionOption);
            bool showLatestVersion = parseResult.GetValue(routerLatestVersionOption);
            var routerVersionsValue = parseResult.GetValue(routerVersionsOption);
            bool showVersions = parseResult.GetResult(routerVersionsOption) is { Implicit: false };
            bool isVersionQuery = showVersion || showLatestVersion || showVersions;
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName))
            {
                bool verbose = parseResult.GetValue(opts.Verbose);
                Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                var client = HttpClientFactory.Shared;

                // Build framework spec if explicit version given (e.g., System.Text.Json@9.0.0 -> runtime@9.0.0)
                string? platformFrameworkSpec = null;
                if (hasExplicitVersion)
                {
                    var (_, discoveredFramework, _, _) = PlatformResolver.ResolveAssembly(bareName);
                    if (discoveredFramework != null)
                        platformFrameworkSpec = $"{discoveredFramework}@{explicitVersion}";
                }

                // Resolve assembly (local-first, then network if needed)
                var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                    bareName, client, log, platformFrameworkSpec);

                if (resolvedPath != null && resolvedError == null)
                {
                    var verbosity = opts.ParseVerbosity(parseResult);
                    var includeSections = opts.ParseIncludeSections(parseResult);
                    var assemblyOptions = new AssemblyOptions
                    {
                        PlatformAssembly = bareName,
                        PlatformFramework = platformFrameworkSpec,
                        JsonOutput = parseResult.GetValue(opts.Json),
                        Verbose = parseResult.GetValue(opts.Verbose),
                        Verbosity = verbosity,
                        IncludeSections = includeSections,
                        ExcludeSections = opts.ParseExcludeSections(parseResult)
                    };

                    var assemblyExitCode = await AssemblyCommand.ExecuteAsync(assemblyOptions);

                    if (assemblyExitCode == 0 && !assemblyOptions.JsonOutput)
                    {
                        var platformTipLevel = verbosity != Verbosity.Minimal || includeSections != null || ArgumentPreprocessor.HeadLines != null
                            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
                        TipWriter.WritePlatformTips(bareName, platformTipLevel, verbosity);
                    }

                    return assemblyExitCode;
                }
            }

            // Qualified type name: e.g., System.Text.Json.JsonSerializer -> type JsonSerializer --platform System.Text.Json
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName)
                && PlatformResolver.TryParseQualifiedTypeName(bareName, out var qtAssembly, out var qtType))
            {
                var verbosity = opts.ParseVerbosity(parseResult);
                var typeOptions = new ApiOptions
                {
                    TypeName = qtType,
                    PlatformAssembly = qtAssembly,
                    JsonOutput = parseResult.GetValue(opts.Json),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    Verbosity = verbosity,
                    IncludeSections = opts.ParseIncludeSections(parseResult),
                    ExcludeSections = opts.ParseExcludeSections(parseResult),
                    TipLevel = ArgumentPreprocessor.HeadLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
                };

                return await ApiCommand.ExecuteAsync(typeOptions);
            }

            // --version: print the resolved version and exit (no package inspection needed)
            if (showVersion)
            {
                if (!forceLatest)
                {
                    if (explicitVersion != null)
                    {
                        // 1. Check app cache and NuGet cache
                        if (NuGetCache.TryGetCachedPackage(bareName, explicitVersion) != null)
                        {
                            Console.WriteLine(explicitVersion);
                            return 0;
                        }

                        // 2. Check NuGet version API
                        var allVersions = await PackageExtractor.GetVersionsAsync(
                            HttpClientFactory.Shared, bareName, includePrerelease: true, limit: null,
                            log: null, sourceOptions: opts.ParseNuGetSourceOptions(parseResult));

                        if (allVersions != null && allVersions.Any(v => string.Equals(v, explicitVersion, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine(explicitVersion);
                            return 0;
                        }

                        // 3. Differentiate bad package from bad version
                        if (allVersions == null || allVersions.Count == 0)
                            Console.Error.WriteLine($"Error: Package '{bareName}' not found.");
                        else
                            Console.Error.WriteLine($"Error: Version '{explicitVersion}' of package '{bareName}' not found. Use --versions to see available versions.");
                        return 1;
                    }
                    else
                    {
                        // Bare name: use newest cached version
                        var cachedVersion = NuGetCache.TryGetLatestCachedVersion(bareName);
                        if (cachedVersion != null)
                        {
                            Console.WriteLine(cachedVersion);
                            return 0;
                        }
                    }
                }
                // No cache hit, or @latest: fall through to --latest-version (version API query)
                showLatestVersion = true;
            }

            // Fall through to package command (NuGet resolution)
            bool useBareName = forceLatest || showLatestVersion;
            var options = new InspectionOptions
            {
                PackageArgs = useBareName ? [bareName] : packageArgs,
                ListVersions = showLatestVersion || showVersions,
                Limit = showLatestVersion ? 1 : routerVersionsValue,
                JsonOutput = parseResult.GetValue(opts.Json),
                OneLine = parseResult.GetValue(routerOneLineOption),
                NoHeader = parseResult.GetValue(routerNoHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                ForceLatest = forceLatest || showLatestVersion
            };

            var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.IncludeSections != null || ArgumentPreprocessor.HeadLines != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && !options.IsRawOutput)
                TipWriter.WritePackageTips(bareName, tipLevel, options.Verbosity);

            return exitCode;
        });

        return routerCommand;
    }
}
