using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the find, implements, extensions, and depends commands.
/// </summary>
public static class SearchCommandDefinitions
{
    public static Command CreateFindCommand(SharedOptions opts)
    {
        var findCommand = new Command(FindCommand.Name, "Search package facets or types across packages and libraries");

        var patternArg = new Argument<string?>("pattern")
        {
            Description = "Type name or glob pattern. Comma-separated for multiple (e.g., \"Option*,Argument*,Command*\")",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var binOption = new Option<string[]>("--bin")
        {
            Description = "Search all DLLs in output directory(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library or target framework by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete types" };
        var membersOption = new Option<bool>("--members") { Description = "Search member names instead of type names (auto-enabled when the pattern starts with '.', e.g. .Serialize)" };
        var literalOption = new Option<string?>("--literal")
        {
            Description = "Find decoded IL string literals containing this exact ordinal substring in the primary implementation assembly of up to 5 explicit name@version packages; requires --tfm; -v:n includes literal-use rows",
            Arity = ArgumentArity.ExactlyOne
        };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description = $"With a type pattern, search up to {ScopeConstants.PackagePrefixExpansionLimit} matching package IDs; without one, inspect {FindCommand.PackageProfileDefaultLimit} latest manifests by default (-t up to {FindCommand.PackageProfileMaximumLimit}). Use -Q Packages for facet queries."
        };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit result count (-t 5) or filter API types by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var candidatesOption = new Option<int?>("--candidates")
        {
            Description = "Package Query candidate budget (default 200, or 20 with --package-content; maximum 1000)"
        };
        var matchesOption = new Option<int?>("--matches")
        {
            Description = "Package Query match budget after facet evaluation (default 100; maximum 1000)"
        };
        var packageContentOption = new Option<bool>("--package-content")
        {
            Description = "Permit Package Query archive-content facets (at most 20 candidates)"
        };

        findCommand.Arguments.Add(patternArg);
        findCommand.Options.Add(packageOption);
        findCommand.Options.Add(assemblyOption);
        findCommand.Options.Add(platformOption);
        findCommand.Options.Add(platformLibraryOption);
        findCommand.Options.Add(extensionsOption);
        findCommand.Options.Add(aspnetcoreOption);
        findCommand.Options.Add(projectOption);
        findCommand.Options.Add(binOption);
        findCommand.Options.Add(tfmOption);
        findCommand.Options.Add(allOption);
        findCommand.Options.Add(membersOption);
        findCommand.Options.Add(literalOption);
        findCommand.Options.Add(typeFilterOption);
        findCommand.Options.Add(opts.RowWhere);
        findCommand.Options.Add(candidatesOption);
        findCommand.Options.Add(matchesOption);
        findCommand.Options.Add(packageContentOption);
        findCommand.Options.Add(opts.Json);
        findCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(findCommand);
        findCommand.Options.Add(packagePrefixOption);
        findCommand.Options.Add(opts.Discover);
        findCommand.Options.Add(opts.Tree);
        findCommand.Options.Add(opts.Columns);
        findCommand.Options.Add(opts.Fields);
        opts.AddCountOptionTo(findCommand);
        opts.AddOutputOptionsTo(findCommand);
        opts.AddNuGetOptionsTo(findCommand);

        findCommand.Validators.Add(result =>
        {
            string? literal = result.GetValue(literalOption);
            if (literal is null)
                return;

            if (!string.IsNullOrEmpty(result.GetValue(patternArg))
                || result.GetResult(packagePrefixOption) is { Implicit: false }
                || result.GetResult(assemblyOption) is { Implicit: false }
                || result.GetResult(platformOption) is { Implicit: false }
                || result.GetResult(platformLibraryOption) is { Implicit: false }
                || result.GetValue(extensionsOption)
                || result.GetValue(aspnetcoreOption)
                || result.GetResult(projectOption) is { Implicit: false }
                || result.GetResult(binOption) is { Implicit: false }
                || result.GetValue(membersOption)
                || result.GetValue(allOption)
                || result.GetResult(typeFilterOption) is { Implicit: false })
            {
                result.AddError(
                    "--literal searches only explicit ID@VERSION packages; "
                    + "it cannot be combined with a type pattern, API search scopes, "
                    + "--package-prefix, --members, --all, or -t.");
                return;
            }

            if (result.GetValue(opts.Discover) is not null)
                return;

            // The planner rejects a missing target framework through the ordinary argument
            // contract, which carries no product-authored sentence. The CLI owns that diagnostic.
            string tfm = result.GetValue(tfmOption) ?? "";
            if (string.IsNullOrWhiteSpace(tfm))
            {
                result.AddError(PackageAssemblyQueryDiagnostics.MissingTargetFramework);
                return;
            }

            try
            {
                _ = PackageAssemblyQuery.Plan(
                    PackageAssemblyPatterns.StringLiteralContains,
                    literal,
                    result.GetValue(packageOption) ?? [],
                    tfm);
            }
            catch (ArgumentException ex)
            {
                result.AddError(PackageAssemblyQueryDiagnostics.Describe(ex));
            }
        });

        var commandArgs = new FindOptionsParser.FindCommandArgs(
            patternArg, packageOption, assemblyOption, platformOption, platformLibraryOption,
            extensionsOption, aspnetcoreOption, projectOption, binOption, tfmOption, allOption,
            typeFilterOption, compactOption, opts.NoHeaders, packagePrefixOption, membersOption,
            literalOption,
            candidatesOption, matchesOption, packageContentOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var result = await FindOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case FindOptionsParser.ShowHelpWithTips:
                    return TipWriter.MissingArgumentWithTips(findCommand,
                        "Search pattern required.",
                        "find Chat*                                # implicit platform scope",
                        "find Chat* --platform                     # explicit platform scope",
                        "find Chat* --extensions                   # Microsoft.Extensions packages",
                        "find Chat* --aspnetcore                   # ASP.NET Core packages",
                        "find Chat* --package Newtonsoft.Json       # specific package",
                        "find --package-prefix Azure.AI            # stream package manifests",
                        "find -Q Packages                          # discover package query facets",
                        "find --literal Json --package System.Text.Json@10.0.0 --tfm net10.0",
                        "find Chat* --platform --extensions         # combine scopes");

                case FindOptionsParser.Success success:
                    var exitCode = await FindCommand.ExecuteAsync(
                        success.Options,
                        ct);

                    if (exitCode == 0
                        && success.Options.Literal is null
                        && !success.Options.IsPackageProfile
                        && !success.Options.FormatExplicitlySet
                        && !success.Options.IsRawOutput)
                    {
                        var tips = FindOptionsParser.BuildTips(success.Options, success.Options.Pattern);
                        Hints.WriteTips(success.TipLevel, [.. tips]);
                    }

                    return exitCode;

                default:
                    return 1;
            }
        });

        return findCommand;
    }

    public static Command CreateImplementsCommand(SharedOptions opts)
    {
        var implCommand = new Command("implements", "Find types implementing an interface or extending a base class");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Target interface or base type (e.g., IDisposable, Stream, IList<T>)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description = $"Search up to {ScopeConstants.PackagePrefixExpansionLimit} packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)"
        };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        implCommand.Arguments.Add(targetTypeArg);
        implCommand.Options.Add(packageOption);
        implCommand.Options.Add(assemblyOption);
        implCommand.Options.Add(platformOption);
        implCommand.Options.Add(platformLibraryOption);
        implCommand.Options.Add(extensionsOption);
        implCommand.Options.Add(aspnetcoreOption);
        implCommand.Options.Add(projectOption);
        implCommand.Options.Add(tfmOption);
        implCommand.Options.Add(allOption);
        implCommand.Options.Add(typeFilterOption);
        implCommand.Options.Add(opts.Json);
        implCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(implCommand);
        implCommand.Options.Add(packagePrefixOption);
        implCommand.Options.Add(opts.Columns);
        implCommand.Options.Add(opts.Fields);
        opts.AddCountOptionTo(implCommand);
        opts.AddOutputOptionsTo(
            implCommand,
            validateLegacyRowWindow: static _ => false);
        opts.AddNuGetOptionsTo(implCommand);

        implCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return TipWriter.MissingArgumentWithTips(implCommand,
                    "Type name required.",
                    "implements Stream                         # implicit platform scope",
                    "implements Stream --platform              # explicit platform scope",
                    "implements Stream --extensions             # Microsoft.Extensions packages",
                    "implements Stream --aspnetcore             # ASP.NET Core packages",
                    "implements Stream --package Foo            # specific package",
                    "implements Stream --platform --extensions  # combine scopes");
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var intent = SearchSourceAdapter.Declare(
                parseResult, packageOption, assemblyOption, projectOption, platformOption,
                platformLibraryOption, extensionsOption, aspnetcoreOption,
                prefixOption: packagePrefixOption);
            var (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Implements",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            var options = new ImplementsOptions
            {
                SourceSelection = selection,
                TargetType = targetType,
                Packages = [.. sources.Packages],
                Assemblies = [.. sources.Assemblies],
                PlatformAssemblies = [.. sources.PlatformAssemblies],
                PlatformFrameworks = [.. sources.PlatformFrameworks],
                Projects = [.. sources.Projects],
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = CommandLineHelpers.ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                Rows = rowSelection is null ? opts.ParseRows(parseResult) : null,
                RowSelection = rowSelection,
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Verbose = parseResult.GetValue(opts.Verbose),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = sourceOptions
            };

            return await ImplementsCommand.ExecuteAsync(options, ct);
        });

        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");
        CliRowSelectionCommandRegistry.Register(
            implCommand,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                linesOption,
                tailLinesOption),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window,
            isActive: static _ => true);

        return implCommand;
    }

    public static Command CreateExtensionsCommand(SharedOptions opts)
    {
        var extCommand = new Command("extensions", "Find extension methods for a type");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Target type to find extensions for (e.g., HttpClient, IEnumerable<T>)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var reachableOption = new Option<bool>("--reachable")
        {
            Description = "Include extensions on types reachable via properties/methods"
        };
        var depthOption = new Option<int>("--depth")
        {
            Description = "Max depth for reachable type traversal (default: 2)",
            DefaultValueFactory = _ => 2
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description = $"Search up to {ScopeConstants.PackagePrefixExpansionLimit} packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)"
        };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        extCommand.Arguments.Add(targetTypeArg);
        extCommand.Options.Add(packageOption);
        extCommand.Options.Add(assemblyOption);
        extCommand.Options.Add(platformOption);
        extCommand.Options.Add(platformLibraryOption);
        extCommand.Options.Add(extensionsOption);
        extCommand.Options.Add(aspnetcoreOption);
        extCommand.Options.Add(projectOption);
        extCommand.Options.Add(reachableOption);
        extCommand.Options.Add(depthOption);
        extCommand.Options.Add(tfmOption);
        extCommand.Options.Add(allOption);
        extCommand.Options.Add(typeFilterOption);
        extCommand.Options.Add(opts.Json);
        extCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(extCommand);
        extCommand.Options.Add(packagePrefixOption);
        extCommand.Options.Add(opts.Columns);
        extCommand.Options.Add(opts.Fields);
        opts.AddCountOptionTo(extCommand);
        opts.AddOutputOptionsTo(extCommand);
        opts.AddNuGetOptionsTo(extCommand);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return TipWriter.MissingArgumentWithTips(extCommand,
                    "Type name required.",
                    "extensions HttpClient                     # implicit platform scope",
                    "extensions HttpClient --platform          # explicit platform scope",
                    "extensions HttpClient --extensions         # Microsoft.Extensions packages",
                    "extensions HttpClient --aspnetcore         # ASP.NET Core packages",
                    "extensions HttpClient --package Foo        # specific package",
                    "extensions HttpClient --platform --extensions  # combine scopes");
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var intent = SearchSourceAdapter.Declare(
                parseResult, packageOption, assemblyOption, projectOption, platformOption,
                platformLibraryOption, extensionsOption, aspnetcoreOption,
                prefixOption: packagePrefixOption);
            var (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);

            var options = new ExtensionsOptions
            {
                SourceSelection = selection,
                TargetType = targetType,
                Packages = [.. sources.Packages],
                Assemblies = [.. sources.Assemblies],
                PlatformAssemblies = [.. sources.PlatformAssemblies],
                PlatformFrameworks = [.. sources.PlatformFrameworks],
                Projects = [.. sources.Projects],
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = CommandLineHelpers.ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                Rows = opts.ParseRows(parseResult),
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = sourceOptions
            };

            return await ExtensionsCommand.ExecuteAsync(options, ct);
        });

        return extCommand;
    }

    public static Command CreateDependsCommand(SharedOptions opts)
    {
        var dependsCommand = new Command("depends", "Walk dependency graphs upward (type hierarchy, library references, or package dependencies)");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Type name to walk dependencies for (e.g., IFloatingPointIeee754, Int128)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var shareOption = WorkspaceShareOption.Create(
            "Emit a resolved NuGet package dependency view as a canonical Workspace packet or complete URL");

        dependsCommand.Arguments.Add(targetTypeArg);
        dependsCommand.Options.Add(packageOption);
        dependsCommand.Options.Add(assemblyOption);
        dependsCommand.Options.Add(platformOption);
        dependsCommand.Options.Add(platformLibraryOption);
        dependsCommand.Options.Add(extensionsOption);
        dependsCommand.Options.Add(aspnetcoreOption);
        dependsCommand.Options.Add(projectOption);
        dependsCommand.Options.Add(tfmOption);
        dependsCommand.Options.Add(shareOption);
        dependsCommand.Options.Add(opts.Json);
        dependsCommand.Options.Add(compactOption);
        dependsCommand.Options.Add(opts.Mermaid);
        dependsCommand.Options.Add(opts.Markdown);
        dependsCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(dependsCommand);
        dependsCommand.Options.Add(opts.Tree);
        opts.AddCountOptionTo(dependsCommand);
        opts.AddOutputOptionsTo(dependsCommand);
        opts.AddNuGetOptionsTo(dependsCommand);

        dependsCommand.Validators.Add(result =>
        {
            if (result.GetValue(opts.Tree)
                && result.GetValue(opts.Mermaid))
            {
                result.AddError(
                    "--tree and --mermaid are alternate graph renderings; choose one.");
            }
            if (result.GetValue(opts.Tree)
                && (result.GetValue(opts.Json)
                    || result.GetValue(opts.Markdown)
                    || result.GetValue(opts.PlainText)
                    || result.GetValue(opts.Table)
                    || result.GetValue(opts.Tsv)
                    || result.GetValue(opts.Jsonl)
                    || result.GetResult(opts.Verbosity)
                        is { Implicit: false }))
            {
                result.AddError(
                    "--tree is a standalone graph rendering and cannot combine with another output format.");
            }
            if (!string.IsNullOrEmpty(result.GetValue(targetTypeArg))
                && result.GetResult(opts.Limit)
                    is { Implicit: false }
                && result.GetValue(opts.Limit) is int count
                && count <= 0)
            {
                result.AddError(
                    "-n requires a positive whole number for type dependency rows.");
            }
        });

        dependsCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var projects = parseResult.GetValue(projectOption) ?? [];
            OutputFormat outputFormat = opts.ResolveFormat(parseResult);
            RowWindow? rows = ParseDependsRows(parseResult, opts);
            RowSelectionIntent<TypeDependencyRowOrder>
                typeDependencyRows =
                    string.IsNullOrEmpty(targetType)
                        ? RowSelectionIntent<
                            TypeDependencyRowOrder>.Empty
                        : ParseTypeDependencyRows(
                            parseResult,
                            opts);
            WorkspaceShareFormat? shareFormat =
                WorkspaceShareOption.Parse(parseResult, shareOption);
            bool hasNonPackageShareInput =
                !string.IsNullOrEmpty(targetType)
                || packages.Length != 1
                || assemblies.Length > 0
                || projects.Length > 0
                || parseResult.GetValue(platformOption)
                || (parseResult.GetValue(platformLibraryOption)?.Length ?? 0) > 0
                || parseResult.GetValue(extensionsOption)
                || parseResult.GetValue(aspnetcoreOption);
            if (shareFormat is not null && hasNonPackageShareInput)
            {
                CommandError.Write(
                    "--share requires exactly one --package input and "
                    + "cannot be used with type, library, project, or platform dependency modes.");
                return 1;
            }

            // Mode detection: no type arg → library or package dependency mode
            if (string.IsNullOrEmpty(targetType))
            {
                var commonOptions = new DependsOptions
                {
                    Tfm = parseResult.GetValue(tfmOption),
                    ShareFormat = shareFormat,
                    Format = outputFormat,
                    JsonOutput = outputFormat == OutputFormat.Json,
                    CompactJson = parseResult.GetValue(compactOption),
                    MermaidOutput = outputFormat == OutputFormat.Mermaid,
                    EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Rows = rows,
                    Count = parseResult.GetValue(opts.Count),
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                    LineWindowExplicitlySet =
                        parseResult.GetResult(opts.Limit) is { Implicit: false }
                        || parseResult.GetResult(opts.Head) is { Implicit: false }
                        || parseResult.GetResult(opts.Tail) is { Implicit: false },
                    OutputFormatExplicitlySet =
                        opts.IsFormatFlagExplicitlySet(parseResult),
                };

                if (assemblies.Length == 1 && packages.Length == 0 && projects.Length == 0)
                    return await DependsCommand.ExecuteLibraryDependsAsync(commonOptions with { LibraryName = assemblies[0] });

                if (packages.Length == 1 && assemblies.Length == 0 && projects.Length == 0)
                    return await DependsCommand.ExecutePackageDependsAsync(
                        commonOptions with { PackageName = packages[0] },
                        ct);

                return TipWriter.MissingArgumentWithTips(dependsCommand,
                    "Type, package, or library required.",
                    "depends IFloatingPointIeee754 --platform   # type hierarchy",
                    "depends --library Microsoft.Extensions.AI   # assembly references",
                    "depends --package System.Text.Json          # NuGet dependencies");
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            var intent = SearchSourceAdapter.Declare(
                parseResult, packageOption, assemblyOption, projectOption, platformOption,
                platformLibraryOption, extensionsOption, aspnetcoreOption);
            var (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);

            var options = new DependsOptions
            {
                SourceSelection = selection,
                TargetType = targetType,
                Packages = [.. sources.Packages],
                Assemblies = [.. sources.Assemblies],
                PlatformAssemblies = [.. sources.PlatformAssemblies],
                PlatformFrameworks = [.. sources.PlatformFrameworks],
                Projects = [.. sources.Projects],
                Tfm = parseResult.GetValue(tfmOption),
                Format = outputFormat,
                JsonOutput = outputFormat == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                MermaidOutput = outputFormat == OutputFormat.Mermaid,
                EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                Tree = parseResult.GetValue(opts.Tree),
                Rows = rows,
                TypeDependencyRows = typeDependencyRows,
                Count = parseResult.GetValue(opts.Count),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = sourceOptions
            };

            var outcome = await DependsCommand.ExecuteTypeDependsAsync(
                options,
                ct);

            // Type not found — fall back to library mode if the name could be a
            // library. A source option makes the positional argument
            // unambiguously a type, so no fallback applies.
            if (outcome.ExitCode == DependsCommand.TypeNotFoundExitCode &&
                selection.UsesImplicitPlatform &&
                !targetType!.Contains('<'))
            {
                var libOptions = new DependsOptions
                {
                    LibraryName = targetType,
                    Tfm = parseResult.GetValue(tfmOption),
                    Format = outputFormat,
                    JsonOutput = outputFormat == OutputFormat.Json,
                    CompactJson = parseResult.GetValue(compactOption),
                    MermaidOutput = outputFormat == OutputFormat.Mermaid,
                    EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Rows = rows,
                    Count = parseResult.GetValue(opts.Count),
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
                };

                // Library mode resolves the name itself and never consults the
                // excluded candidate, so its answer stands on its own. It is
                // also unreachable while a candidate was excluded: an explicit
                // source option is what makes exclusion possible, and that same
                // option suppresses this fallback.
                return await DependsCommand.ExecuteLibraryDependsAsync(
                    libOptions);
            }

            if (outcome.ExitCode == DependsCommand.TypeNotFoundExitCode)
            {
                CommandError.Write($"Type '{targetType}' not found in the specified scope.");
                return outcome.Uncertified
                    ? DependsCommand.UncertifiedScanExitCode
                    : 1;
            }

            return outcome.ExitCode;
        });

        return dependsCommand;
    }

    private static RowWindow? ParseDependsRows(
        ParseResult parseResult,
        SharedOptions opts)
    {
        RowWindow? rows = opts.ParseRows(parseResult);
        if (rows is not null)
            return rows;

        if (parseResult.GetResult(opts.Limit) is not { Implicit: false }
            || parseResult.GetValue(opts.Limit) is not int count)
        {
            return null;
        }

        return parseResult.GetValue(opts.Tail)
            ? RowWindow.Tail(count)
            : RowWindow.Head(count);
    }

    private static RowSelectionIntent<TypeDependencyRowOrder>
        ParseTypeDependencyRows(
        ParseResult parseResult,
        SharedOptions opts)
    {
        string? rows = parseResult.GetValue(opts.Rows);
        if (rows is not null)
        {
            if (!RowSpec.TryParse(
                    rows,
                    out RowSpec spec,
                    out string? error))
            {
                throw new RowWindowValidationException(
                    $"--rows {error}");
            }

            RowSelectionIntentOperation<TypeDependencyRowOrder>
                operation =
                    spec.Kind switch
                    {
                        RowSpecKind.Count
                            when parseResult.GetValue(opts.Tail) =>
                            RowSelectionIntentOperation<
                                TypeDependencyRowOrder>.Tail(
                                    spec.Count),
                        RowSpecKind.Count =>
                            RowSelectionIntentOperation<
                                TypeDependencyRowOrder>.Head(
                                    spec.Count),
                        RowSpecKind.Range =>
                            RowSelectionIntentOperation<
                                TypeDependencyRowOrder>.Window(
                                    spec.Start,
                                    spec.End),
                        _ => throw new InvalidOperationException(
                            "Unsupported type-dependency row selection."),
                    };
            return RowSelectionIntent<
                TypeDependencyRowOrder>.Create([operation]);
        }

        if (parseResult.GetResult(opts.Limit) is not { Implicit: false }
            || parseResult.GetValue(opts.Limit) is not int count)
        {
            return RowSelectionIntent<
                TypeDependencyRowOrder>.Empty;
        }

        return RowSelectionIntent<TypeDependencyRowOrder>.Create(
            [
                parseResult.GetValue(opts.Tail)
                    ? RowSelectionIntentOperation<
                        TypeDependencyRowOrder>.Tail(count)
                    : RowSelectionIntentOperation<
                        TypeDependencyRowOrder>.Head(count),
            ]);
    }
}
