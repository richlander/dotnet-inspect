using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
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
        opts.AddCountOptionTo(routerCommand);

        // Version query options for the router
        var routerVersionOption = new Option<bool>("--version") { Description = "Show resolved version" };
        routerCommand.Options.Add(routerVersionOption);
        var routerLatestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest stable version from nuget.org (add --preview for prerelease)" };
        routerCommand.Options.Add(routerLatestVersionOption);
        var routerVersionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        routerVersionsOption.DefaultValueFactory = _ => null;
        routerCommand.Options.Add(routerVersionsOption);
        var routerPrereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions for --versions and latest resolution" };
        routerPrereleaseOption.Aliases.Add("--prerelease");
        routerCommand.Options.Add(routerPrereleaseOption);

        var routerCompactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        routerCommand.Options.Add(routerCompactOption);

        var commandArgs = new RouterOptionsParser.RouterCommandArgs(
            packageNameArg, routerVersionOption, routerLatestVersionOption, routerVersionsOption,
            routerPrereleaseOption, opts.OneLine, opts.NoHeaders, routerCompactOption);

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var result = RouterOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case RouterOptionsParser.ShowHelp:
                    HelpWriter.WriteHelp(routerCommand);
                    return 0;

                case RouterOptionsParser.Discovery d:
                    // Router-level discovery: show package sections (no input required)
                    var routerSchemaMap = InspectionContext.Default.GetSchemaInfo<InspectionResultView>()!.ToDocumentSchema();
                    var routerFormat = opts.ResolveFormat(parseResult, OutputFormat.Table);
                    return DiscoverOutput.Execute(d.Discover, routerSchemaMap, tree: d.Tree,
                        json: routerFormat == OutputFormat.Json,
                        tsv: routerFormat == OutputFormat.Tsv,
                        markdown: routerFormat == OutputFormat.Markdown,
                        verbosity: (int)opts.ParseVerbosity(parseResult));

                case RouterOptionsParser.ParseError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case RouterOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case RouterOptionsParser.RouteToAssemblyFile route:
                    return await AssemblyCommand.ExecuteAsync(route.Options);

                case RouterOptionsParser.RouteToPlatformAssembly route:
                    return await ExecutePlatformAssemblyAsync(route, opts, parseResult, commandArgs);

                case RouterOptionsParser.HandleVersionQuery query:
                    return await ExecuteVersionQueryAsync(query, opts, parseResult, routerVersionsOption);

                case RouterOptionsParser.RouteToType route:
                    return await ExecuteTypeCommandAsync(route, opts, parseResult, commandArgs);

                case RouterOptionsParser.RouteToMember route:
                    return await ExecuteMemberCommandAsync(route, opts, parseResult, commandArgs);

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
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        bool verbose = route.Options.Verbose;
        Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
        var client = HttpClientFactory.Shared;

        var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
            route.BareName, client, log, route.Options.PlatformFramework);

        if (resolvedPath != null && resolvedError == null)
        {
            var assemblyExitCode = await AssemblyCommand.ExecuteAsync(route.Options);

            if (assemblyExitCode == 0 && !route.Options.IsRawOutput)
            {
                var platformTipLevel = route.Verbosity != Verbosity.Minimal || route.Options.Select != null || route.Options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
                TipWriter.WritePlatformTips(route.BareName, platformTipLevel, route.Verbosity);
            }

            return assemblyExitCode;
        }

        var memberSplit = SharedParsers.TrySplitQualifiedTypeMember(route.BareName, allowPlatformPrefixFallback: true);
        if (memberSplit != null)
        {
            var memberOptions = BuildMemberOptions(
                memberSplit.Value.Probe,
                memberSplit.Value.MemberName,
                opts,
                parseResult,
                commandArgs);
            return await MemberCommand.ExecuteAsync(memberOptions);
        }

        // Platform resolution failed - check if this is a qualified type name
        // e.g., System.Text.Json.JsonSerializer -> type JsonSerializer --platform System.Text.Json
        // Probes exact local types first, then keeps a platform prefix for typo-friendly type suggestions.
        var probe = SourceResolver.TryResolveQualifiedTypeName(route.BareName, allowPlatformPrefixFallback: true);
        if (probe != null)
        {
            var typeOptions = new TypeOptions
            {
                TypeName = probe.Remainder,
                PlatformAssembly = probe.Kind == SourceResolver.LocalSourceKind.Platform ? probe.SourceName : null,
                PackagePath = probe.Kind == SourceResolver.LocalSourceKind.CachedPackage ? probe.SourceName : null,
                JsonOutput = route.Options.JsonOutput,
                PlainText = route.Options.Format == OutputFormat.PlainText,
                OneLine = route.OneLine,
                Tsv = route.Options.Tsv,
                OneLineExplicitlySet = route.Options.OneLineExplicitlySet,
                FormatExplicitlySet = route.Options.FormatExplicitlySet,
                NoHeader = route.NoHeader,
                CompactJson = parseResult.GetValue(commandArgs.CompactOption),
                Verbose = route.Options.Verbose,
                Verbosity = route.Verbosity,
                IncludeSections = null,
                Discover = route.Options.Discover,
                Tree = route.Options.Tree,
                Select = route.Options.Select,
                Columns = route.Options.Columns,
                Fields = route.Options.Fields,
                Count = route.Options.Count,
                Schema = opts.ParseSchema(parseResult),
                SourceOptions = route.Options.SourceOptions,
                TipLevel = ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
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
            Tsv = route.Options.Tsv,
            OneLineExplicitlySet = route.Options.OneLineExplicitlySet,
            NoHeader = route.NoHeader,
            Verbose = route.Options.Verbose,
            Verbosity = route.Verbosity,
            Discover = route.Options.Discover,
            Tree = route.Options.Tree,
            Select = route.Options.Select,
            Columns = route.Options.Columns,
            Fields = route.Options.Fields,
            Schema = route.Options.Schema,
            Count = route.Options.Count,
            SourceOptions = route.Options.SourceOptions
        };

        var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.Select != null || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
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
            else if (!query.IncludePrerelease)
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
            IncludePrerelease = query.IncludePrerelease,
            Limit = 1,
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = query.SourceOptions,
            ForceLatest = true
        };

        return await PackageCommand.ExecuteAsync(options);
    }

    private static async Task<int> ExecuteTypeCommandAsync(
        RouterOptionsParser.RouteToType route,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        var source = await SourceResolver.ResolveAsync(
            route.Args, null, null, null,
            parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

        if (source.VersionError)
        {
            Console.Error.WriteLine(source.VersionErrorMessage);
            return 1;
        }

        var verbosity = opts.ParseVerbosity(parseResult);

        var typeOptions = new TypeOptions
        {
            TypeName = source.TypeName,
            PackagePath = source.PackagePath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            CompactJson = parseResult.GetValue(commandArgs.CompactOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Schema = opts.ParseSchema(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            TipLevel = ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return await ApiCommand.ExecuteAsync(typeOptions);
    }

    private static Task<int> ExecuteMemberCommandAsync(
        RouterOptionsParser.RouteToMember route,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        var split = SharedParsers.TrySplitQualifiedTypeMember(route.Args[0], allowPlatformPrefixFallback: false);
        if (split == null)
            return Task.FromResult(1);

        var memberOptions = BuildMemberOptions(
            split.Value.Probe,
            split.Value.MemberName,
            opts,
            parseResult,
            commandArgs);
        return MemberCommand.ExecuteAsync(memberOptions);
    }

    private static MemberOptions BuildMemberOptions(
        SourceResolver.LocalProbeResult probe,
        string memberSelector,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        var (memberName, overloadIndex) = SharedParsers.ParseOverloadShorthand(memberSelector);
        var verbosity = opts.ParseVerbosity(parseResult);

        return new MemberOptions
        {
            TypeName = probe.Remainder,
            PlatformAssembly = probe.Kind == SourceResolver.LocalSourceKind.Platform ? probe.SourceName : null,
            PackagePath = probe.Kind == SourceResolver.LocalSourceKind.CachedPackage ? probe.SourceName : null,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { memberName },
            OverloadIndex = overloadIndex,
            ShowDocs = true,
            DocsExplicitlySet = false,
            JsonOutput = parseResult.GetValue(opts.Json),
            PlainText = opts.ResolveFormat(parseResult) == OutputFormat.PlainText,
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            CompactJson = parseResult.GetValue(commandArgs.CompactOption),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Schema = opts.ParseSchema(parseResult),
            Rows = opts.ParseRows(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            TipLevel = ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                ? TipLevel.Quiet
                : opts.ParseTipLevel(parseResult)
        };
    }

    private static async Task<int> ExecutePackageCommandAsync(RouterOptionsParser.RouteToPackage route)
    {
        var exitCode = await PackageCommand.ExecuteAsync(route.Options);

        if (exitCode == 0 && !route.Options.IsRawOutput)
            TipWriter.WritePackageTips(route.BareName, route.Options.TipLevel, route.Verbosity);

        return exitCode;
    }
}
