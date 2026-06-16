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
        var routerLibraryOption = new Option<string?>("--library")
        {
            Description = "Inspect a library from the resolved package; omit value to select the primary library when unambiguous",
            Arity = ArgumentArity.ZeroOrOne
        };
        routerCommand.Options.Add(routerLibraryOption);

        var commandArgs = new RouterOptionsParser.RouterCommandArgs(
            packageNameArg, routerVersionOption, routerLatestVersionOption, routerVersionsOption,
            routerPrereleaseOption, opts.OneLine, opts.NoHeaders, routerCompactOption, routerLibraryOption);

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
                    var routerPipeline = PackageSectionDescriptors.CreatePipeline();
                    return DiscoverOutput.Execute(d.Discover, routerSchemaMap, tree: d.Tree,
                        json: routerFormat == OutputFormat.Json,
                        tsv: routerFormat == OutputFormat.Tsv,
                        jsonl: routerFormat == OutputFormat.Jsonl,
                        markdown: routerFormat == OutputFormat.Markdown,
                        verbosity: (int)opts.ParseVerbosity(parseResult),
                        sectionCategories: routerPipeline.GetCategoryMap());

                case RouterOptionsParser.ParseError error:
                    Console.Error.WriteLine(error.Message);
                    return 1;

                case RouterOptionsParser.UnrecognizedOption error:
                    Console.Error.WriteLine($"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case RouterOptionsParser.RouteRequest request:
                    return await ExecuteRouteRequestAsync(request, opts, parseResult, commandArgs, routerVersionsOption);

                default:
                    return 1;
            }
        });

        return routerCommand;
    }

    private interface IRouterRouteProbe
    {
        Task<int?> TryExecuteAsync(RouterRouteContext context);
    }

    private sealed record RouterRouteContext(
        RouterOptionsParser.RouteRequest Request,
        SharedOptions Options,
        ParseResult ParseResult,
        RouterOptionsParser.RouterCommandArgs CommandArgs,
        Option<int?> RouterVersionsOption);

    private static readonly IRouterRouteProbe[] RouteProbes =
    [
        new SecondArgumentTypeProbe(),
        new FileProbe(),
        new VersionProbe(),
        new ExactPlatformLibraryProbe(),
        new QualifiedTypeOrMemberProbe(),
        new TypeFindIfMissProbe(),
        new PlatformPrefixBrowseProbe(),
        new PackageProbe()
    ];

    private static async Task<int> ExecuteRouteRequestAsync(
        RouterOptionsParser.RouteRequest request,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs,
        Option<int?> routerVersionsOption)
    {
        var context = new RouterRouteContext(request, opts, parseResult, commandArgs, routerVersionsOption);
        foreach (var probe in RouteProbes)
        {
            var exitCode = await probe.TryExecuteAsync(context);
            if (exitCode.HasValue)
                return exitCode.Value;
        }

        return 1;
    }

    private sealed class SecondArgumentTypeProbe : IRouterRouteProbe
    {
        public async Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (request.Args.Length < 2 || CommandLineHelpers.LooksLikeVersionNumber(request.Args[1]))
                return null;

            if (request.PackageLibrary != null)
            {
                Console.Error.WriteLine("Error: When using --library, the second positional argument must be a package version. Use --library <dll> to select a DLL.");
                return 1;
            }

            return await ExecuteTypeCommandAsync(
                new RouterOptionsParser.RouteToType(request.Args),
                context.Options,
                context.ParseResult,
                context.CommandArgs);
        }
    }

    private sealed class FileProbe : IRouterRouteProbe
    {
        public async Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            if (!CommandLineHelpers.TryClassifyAsFilePath(context.Request.Name, out var dllPath, out _)
                || dllPath == null)
                return null;

            return await LibraryCommand.ExecuteAsync(BuildLibraryFileOptions(dllPath, context));
        }
    }

    private sealed class VersionProbe : IRouterRouteProbe
    {
        public async Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (!request.ShowVersion)
                return null;

            return await ExecuteVersionQueryAsync(
                new RouterOptionsParser.HandleVersionQuery(
                    request.BareName,
                    request.ExplicitVersion,
                    request.ForceLatest,
                    request.IncludePrerelease,
                    context.Options.ParseNuGetSourceOptions(context.ParseResult)),
                context.Options,
                context.ParseResult,
                context.RouterVersionsOption);
        }
    }

    private sealed class ExactPlatformLibraryProbe : IRouterRouteProbe
    {
        public Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (request.IsVersionQuery || request.PackageLibrary != null || !PlatformResolver.IsPlatformCandidate(request.BareName))
                return Task.FromResult<int?>(null);

            return TryExecuteExactPlatformLibraryAsync(BuildPlatformRoute(context), context.Options, context.ParseResult, context.CommandArgs);
        }
    }

    private sealed class QualifiedTypeOrMemberProbe : IRouterRouteProbe
    {
        public async Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (request.IsVersionQuery || request.PackageLibrary != null || request.Args.Length != 1)
                return null;

            var route = BuildPlatformRoute(context);
            var allowPlatformPrefixFallback = PlatformResolver.IsPlatformCandidate(request.BareName);
            return await TryExecuteTypeOrMemberAsync(
                route,
                context.Options,
                context.ParseResult,
                context.CommandArgs,
                allowPlatformPrefixFallback);
        }
    }

    private sealed class TypeFindIfMissProbe : IRouterRouteProbe
    {
        public Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (request.IsVersionQuery || request.PackageLibrary != null || request.Args.Length != 1)
                return Task.FromResult<int?>(null);

            return TypeCommand.TryExecuteFindIfMissAsync(BuildFindIfMissTypeOptions(context));
        }
    }

    private sealed class PlatformPrefixBrowseProbe : IRouterRouteProbe
    {
        public Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            var request = context.Request;
            if (request.IsVersionQuery || request.PackageLibrary != null || request.Args.Length != 1
                || !PlatformResolver.IsPlatformCandidate(request.BareName))
                return Task.FromResult<int?>(null);

            return TypeCommand.TryExecutePlatformPrefixBrowseAsync(
                BuildPlatformPrefixTypeOptions(BuildPlatformRoute(context), context.Options, context.ParseResult, context.CommandArgs),
                ApiTypeSectionDescriptors.CreatePipeline());
        }
    }

    private sealed class PackageProbe : IRouterRouteProbe
    {
        public async Task<int?> TryExecuteAsync(RouterRouteContext context)
        {
            return await ExecutePackageCommandAsync(
                new RouterOptionsParser.RouteToPackage(BuildPackageOptions(context), context.Request.BareName, context.Request.Verbosity),
                context.ParseResult,
                context.Options);
        }
    }

    private static RouterOptionsParser.RouteToPlatformLibrary BuildPlatformRoute(RouterRouteContext context)
    {
        var options = BuildPlatformLibraryOptions(context);
        return new RouterOptionsParser.RouteToPlatformLibrary(
            options,
            context.Request.BareName,
            context.Request.OriginalArg,
            context.Request.Verbosity,
            options.OneLine,
            context.Request.NoHeader);
    }

    private static async Task<int?> TryExecuteExactPlatformLibraryAsync(
        RouterOptionsParser.RouteToPlatformLibrary route,
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
            if (PlatformResolver.IsFacadeOnlyAssembly(resolvedPath))
            {
                var facadeRouteExitCode = await TryExecuteTypeOrMemberAsync(
                    route,
                    opts,
                    parseResult,
                    commandArgs,
                    allowPlatformPrefixFallback: false);
                if (facadeRouteExitCode.HasValue)
                    return facadeRouteExitCode.Value;
            }

            var assemblyExitCode = await LibraryCommand.ExecuteAsync(route.Options);

            if (assemblyExitCode == 0 && !route.Options.FormatExplicitlySet && !route.Options.IsRawOutput)
            {
                var platformTipLevel = route.Options.FormatExplicitlySet || route.Verbosity != Verbosity.Minimal || route.Options.Select != null || route.Options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
                TipWriter.WritePlatformTips(route.BareName, platformTipLevel, route.Verbosity);
            }

            return assemblyExitCode;
        }

        return null;
    }

    private static TypeOptions BuildPlatformPrefixTypeOptions(
        RouterOptionsParser.RouteToPlatformLibrary route,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        var routePolicy = TypeRoutePolicy.ResolveImplicit(
            route.BareName,
            new SourceResolver.ResolvedSource(route.BareName, null, null, null, null));

        return routePolicy.ApplyTo(new TypeOptions
        {
            JsonOutput = route.Options.JsonOutput,
            PlainText = route.Options.Format == OutputFormat.PlainText,
            OneLine = route.OneLine,
            Tsv = route.Options.Tsv,
            Jsonl = route.Options.Jsonl,
            OneLineExplicitlySet = route.Options.OneLineExplicitlySet,
            FormatExplicitlySet = route.Options.FormatExplicitlySet,
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
            NoHeader = route.NoHeader,
            CompactJson = parseResult.GetValue(commandArgs.CompactOption),
            Verbose = route.Options.Verbose,
            Verbosity = route.Verbosity,
            Discover = route.Options.Discover,
            Tree = route.Options.Tree,
            Select = route.Options.Select,
            Columns = route.Options.Columns,
            Fields = route.Options.Fields,
            Count = route.Options.Count,
            Schema = opts.ParseSchema(parseResult),
            Rows = route.Options.Rows,
            SourceOptions = route.Options.SourceOptions,
            TipLevel = route.Options.FormatExplicitlySet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                ? TipLevel.Quiet
                : opts.ParseTipLevel(parseResult)
        });
    }

    private static async Task<int?> TryExecuteTypeOrMemberAsync(
        RouterOptionsParser.RouteToPlatformLibrary route,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs,
        bool allowPlatformPrefixFallback)
    {
        var memberSplit = SharedParsers.TrySplitQualifiedTypeMember(route.BareName, allowPlatformPrefixFallback);
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

        // e.g., System.Text.Json.JsonSerializer -> type JsonSerializer --platform System.Text.Json.
        // Prefix fallback is only for unresolved platform candidates, where it preserves typo suggestions.
        var probe = SourceResolver.TryResolveQualifiedTypeName(route.BareName, allowPlatformPrefixFallback);
        if (probe == null)
            return null;

        var typeOptions = BuildTypeOptions(route, probe, opts, parseResult, commandArgs);
        return await ApiCommand.ExecuteAsync(typeOptions);
    }

    private static TypeOptions BuildTypeOptions(
        RouterOptionsParser.RouteToPlatformLibrary route,
        SourceResolver.LocalProbeResult probe,
        SharedOptions opts,
        ParseResult parseResult,
        RouterOptionsParser.RouterCommandArgs commandArgs)
    {
        var source = new SourceResolver.ResolvedSource(
            probe.Kind == SourceResolver.LocalSourceKind.CachedPackage ? probe.SourceName : null,
            null,
            probe.Kind == SourceResolver.LocalSourceKind.Platform ? probe.SourceName : null,
            null,
            probe.Remainder);
        var routePolicy = TypeRoutePolicy.ResolveImplicit(route.BareName, source);

        return routePolicy.ApplyTo(new TypeOptions
        {
            TypeName = probe.Remainder,
            PlatformAssembly = probe.Kind == SourceResolver.LocalSourceKind.Platform ? probe.SourceName : null,
            PackagePath = probe.Kind == SourceResolver.LocalSourceKind.CachedPackage ? probe.SourceName : null,
            JsonOutput = route.Options.JsonOutput,
            PlainText = route.Options.Format == OutputFormat.PlainText,
            OneLine = route.OneLine,
            Tsv = route.Options.Tsv,
            Jsonl = route.Options.Jsonl,
            OneLineExplicitlySet = route.Options.OneLineExplicitlySet,
            FormatExplicitlySet = route.Options.FormatExplicitlySet,
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
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
            TipLevel = route.Options.FormatExplicitlySet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        });
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
        var routePolicy = TypeRoutePolicy.Resolve(route.Args, hasExplicitSource: false, source);

        var typeOptions = routePolicy.ApplyTo(new TypeOptions
        {
            TypeName = source.TypeName,
            PackagePath = source.PackagePath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride,
            JsonOutput = parseResult.GetValue(opts.Json),
            PlainText = opts.ResolveFormat(parseResult) == OutputFormat.PlainText,
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
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
            TipLevel = opts.IsFormatExplicitlySet(parseResult) || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        });

        return await ApiCommand.ExecuteAsync(typeOptions);
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
            TipLevel = opts.IsFormatExplicitlySet(parseResult) || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                ? TipLevel.Quiet
                : opts.ParseTipLevel(parseResult)
        };
    }

    private static async Task<int> ExecutePackageCommandAsync(
        RouterOptionsParser.RouteToPackage route,
        ParseResult parseResult,
        SharedOptions opts)
    {
        var exitCode = await PackageCommand.ExecuteAsync(route.Options);

        if (exitCode == 0 && route.Options.PackageLibrary == null && !route.Options.FormatExplicitlySet && !route.Options.IsRawOutput)
            TipWriter.WritePackageTips(route.BareName, route.Options.TipLevel, route.Verbosity);

        return exitCode;
    }

    private static LibraryOptions BuildLibraryFileOptions(string dllPath, RouterRouteContext context)
    {
        var opts = context.Options;
        var parseResult = context.ParseResult;
        return new LibraryOptions
        {
            AssemblyName = dllPath,
            IncludeMetadata = true,
            JsonOutput = parseResult.GetValue(opts.Json),
            Markdown = parseResult.GetValue(opts.Markdown),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            Format = opts.ResolveFormat(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
        };
    }

    private static LibraryOptions BuildPlatformLibraryOptions(RouterRouteContext context)
    {
        var request = context.Request;
        var opts = context.Options;
        var parseResult = context.ParseResult;

        string? platformFrameworkSpec = null;
        if (request.HasExplicitVersion)
        {
            var (_, discoveredFramework, _, _) = PlatformResolver.ResolveAssembly(request.BareName);
            if (discoveredFramework != null)
                platformFrameworkSpec = $"{discoveredFramework}@{request.ExplicitVersion}";
        }

        return new LibraryOptions
        {
            PlatformAssembly = request.BareName,
            PlatformFramework = platformFrameworkSpec,
            JsonOutput = parseResult.GetValue(opts.Json),
            Markdown = parseResult.GetValue(opts.Markdown),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            Format = opts.ResolveFormat(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = request.Verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            NoHeader = request.NoHeader,
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
        };
    }

    private static TypeOptions BuildFindIfMissTypeOptions(RouterRouteContext context)
    {
        var request = context.Request;
        var opts = context.Options;
        var parseResult = context.ParseResult;
        return new TypeOptions
        {
            PackagePath = request.BareName,
            OriginalTypeQuery = request.BareName,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
            NoHeader = request.NoHeader,
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = request.Verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            Schema = opts.ParseSchema(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            TipLevel = opts.IsFormatExplicitlySet(parseResult) || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                ? TipLevel.Quiet
                : opts.ParseTipLevel(parseResult)
        };
    }

    private static InspectionOptions BuildPackageOptions(RouterRouteContext context)
    {
        var request = context.Request;
        var opts = context.Options;
        var parseResult = context.ParseResult;
        bool useBareName = request.ForceLatest || request.ShowLatestVersion;
        var options = new InspectionOptions
        {
            PackageArgs = useBareName ? [request.BareName] : request.Args,
            ListVersions = request.ShowLatestVersion || request.ShowVersions,
            IncludePrerelease = request.IncludePrerelease,
            Limit = request.ShowLatestVersion ? 1 : request.VersionsLimit,
            JsonOutput = parseResult.GetValue(opts.Json),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = request.NoHeader,
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = request.Verbosity,
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            PackageLibrary = request.PackageLibrary,
            ForceLatest = request.ForceLatest || request.ShowLatestVersion
        };

        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.Select != null || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        return options with { TipLevel = tipLevel };
    }
}
