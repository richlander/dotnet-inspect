using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the find, implements, extensions, and depends commands.
/// </summary>
public static class SearchCommandDefinitions
{
    public static Command CreateFindCommand(SharedOptions opts)
    {
        var findCommand = new Command(FindCommand.Name, "Search for types across packages and libraries");

        var patternArg = new Argument<string?>("pattern")
        {
            Description = "Type name or glob pattern. Comma-separated for multiple (e.g., \"Option*,Argument*,Command*\")",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var binOption = new Option<string[]>("--bin")
        {
            Description = "Search all DLLs in output directory(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library or target framework by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        findCommand.Arguments.Add(patternArg);
        findCommand.Options.Add(packageOption);
        findCommand.Options.Add(assemblyOption);
        findCommand.Options.Add(platformOption);
        findCommand.Options.Add(extensionsOption);
        findCommand.Options.Add(aspnetcoreOption);
        findCommand.Options.Add(curatedOption);
        findCommand.Options.Add(projectOption);
        findCommand.Options.Add(binOption);
        findCommand.Options.Add(tfmOption);
        findCommand.Options.Add(allOption);
        findCommand.Options.Add(typeFilterOption);
        findCommand.Options.Add(opts.Json);
        findCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(findCommand);
        findCommand.Options.Add(packagePrefixOption);
        findCommand.Options.Add(opts.Columns);
        findCommand.Options.Add(opts.Fields);
        opts.AddOutputOptionsTo(findCommand);
        opts.AddNuGetOptionsTo(findCommand);

        var commandArgs = new FindOptionsParser.FindCommandArgs(
            patternArg, packageOption, assemblyOption, platformOption, extensionsOption,
            aspnetcoreOption, curatedOption, projectOption, binOption, tfmOption, allOption,
            typeFilterOption, compactOption, opts.OneLine, opts.NoHeaders, packagePrefixOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var result = await FindOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case FindOptionsParser.ShowHelpWithTips:
                    return TipWriter.ShowHelpWithTips(findCommand,
                        "find Chat*                                # search default scope",
                        "find Chat* --platform                     # platform libraries only",
                        "find Chat* --extensions                   # Microsoft.Extensions packages",
                        "find Chat* --aspnetcore                   # ASP.NET Core packages",
                        "find Chat* --package Newtonsoft.Json       # specific package",
                        "find Chat* --platform --extensions         # combine scopes");

                case FindOptionsParser.Success success:
                    var exitCode = await FindCommand.ExecuteAsync(success.Options);

                    if (exitCode == 0 && !success.Options.IsRawOutput)
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
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        implCommand.Arguments.Add(targetTypeArg);
        implCommand.Options.Add(packageOption);
        implCommand.Options.Add(assemblyOption);
        implCommand.Options.Add(platformOption);
        implCommand.Options.Add(extensionsOption);
        implCommand.Options.Add(aspnetcoreOption);
        implCommand.Options.Add(curatedOption);
        implCommand.Options.Add(tfmOption);
        implCommand.Options.Add(allOption);
        implCommand.Options.Add(typeFilterOption);
        implCommand.Options.Add(opts.Json);
        implCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(implCommand);
        implCommand.Options.Add(packagePrefixOption);
        implCommand.Options.Add(opts.Columns);
        implCommand.Options.Add(opts.Fields);
        opts.AddOutputOptionsTo(implCommand);
        opts.AddNuGetOptionsTo(implCommand);

        implCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return TipWriter.ShowHelpWithTips(implCommand,
                    "implements Stream                         # search default scope",
                    "implements Stream --platform              # platform libraries only",
                    "implements Stream --extensions             # Microsoft.Extensions packages",
                    "implements Stream --aspnetcore             # ASP.NET Core packages",
                    "implements Stream --package Foo            # specific package",
                    "implements Stream --platform --extensions  # combine scopes");
            }

            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var packages = await CommandLineHelpers.MergeWithPrefixPackagesAsync(
                parseResult.GetValue(packageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix);

            var options = new ImplementsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = CommandLineHelpers.ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                Rows = opts.ParseRows(parseResult),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = opts.ResolveOneLine(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Verbose = parseResult.GetValue(opts.Verbose),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Tree = opts.ParseTree(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await ImplementsCommand.ExecuteAsync(options);
        });

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
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
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
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        extCommand.Arguments.Add(targetTypeArg);
        extCommand.Options.Add(packageOption);
        extCommand.Options.Add(assemblyOption);
        extCommand.Options.Add(platformOption);
        extCommand.Options.Add(extensionsOption);
        extCommand.Options.Add(aspnetcoreOption);
        extCommand.Options.Add(curatedOption);
        extCommand.Options.Add(reachableOption);
        extCommand.Options.Add(depthOption);
        extCommand.Options.Add(tfmOption);
        extCommand.Options.Add(allOption);
        extCommand.Options.Add(typeFilterOption);
        extCommand.Options.Add(opts.Json);
        extCommand.Options.Add(compactOption);
        extCommand.Options.Add(packagePrefixOption);
        opts.AddOutputOptionsTo(extCommand);
        opts.AddNuGetOptionsTo(extCommand);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return TipWriter.ShowHelpWithTips(extCommand,
                    "extensions HttpClient                     # search default scope",
                    "extensions HttpClient --platform          # platform libraries only",
                    "extensions HttpClient --extensions         # Microsoft.Extensions packages",
                    "extensions HttpClient --aspnetcore         # ASP.NET Core packages",
                    "extensions HttpClient --package Foo        # specific package",
                    "extensions HttpClient --platform --extensions  # combine scopes");
            }

            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var packages = await CommandLineHelpers.MergeWithPrefixPackagesAsync(
                parseResult.GetValue(packageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix);

            var options = new ExtensionsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = CommandLineHelpers.ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                Rows = opts.ParseRows(parseResult),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await ExtensionsCommand.ExecuteAsync(options);
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
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        dependsCommand.Arguments.Add(targetTypeArg);
        dependsCommand.Options.Add(packageOption);
        dependsCommand.Options.Add(assemblyOption);
        dependsCommand.Options.Add(platformOption);
        dependsCommand.Options.Add(extensionsOption);
        dependsCommand.Options.Add(aspnetcoreOption);
        dependsCommand.Options.Add(curatedOption);
        dependsCommand.Options.Add(tfmOption);
        dependsCommand.Options.Add(opts.Json);
        dependsCommand.Options.Add(compactOption);
        dependsCommand.Options.Add(opts.Mermaid);
        dependsCommand.Options.Add(opts.Markdown);
        opts.AddOutputOptionsTo(dependsCommand);
        opts.AddNuGetOptionsTo(dependsCommand);

        dependsCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            // Mode detection: no type arg → library or package dependency mode
            if (string.IsNullOrEmpty(targetType))
            {
                var commonOptions = new DependsOptions
                {
                    Tfm = parseResult.GetValue(tfmOption),
                    JsonOutput = parseResult.GetValue(opts.Json),
                    CompactJson = parseResult.GetValue(compactOption),
                    MermaidOutput = opts.ResolveFormat(parseResult) == OutputFormat.Mermaid,
                    EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                    Rows = opts.ParseRows(parseResult),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
                };

                if (assemblies.Length == 1 && packages.Length == 0)
                    return await DependsCommand.ExecuteLibraryDependsAsync(commonOptions with { LibraryName = assemblies[0] });

                if (packages.Length == 1 && assemblies.Length == 0)
                    return await DependsCommand.ExecutePackageDependsAsync(commonOptions with { PackageName = packages[0] });

                return TipWriter.ShowHelpWithTips(dependsCommand,
                    "depends IFloatingPointIeee754 --platform   # type hierarchy",
                    "depends --library Microsoft.Extensions.AI   # assembly references",
                    "depends --package System.Text.Json          # NuGet dependencies");
            }

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies);

            var options = new DependsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                MermaidOutput = opts.ResolveFormat(parseResult) == OutputFormat.Mermaid,
                EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                Rows = opts.ParseRows(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            var exitCode = await DependsCommand.ExecuteTypeDependsAsync(options);

            // Type not found — fall back to library mode if the name could be a library
            if (exitCode == DependsCommand.TypeNotFoundExitCode && !targetType!.Contains('<'))
            {
                var libOptions = new DependsOptions
                {
                    LibraryName = targetType,
                    Tfm = parseResult.GetValue(tfmOption),
                    JsonOutput = parseResult.GetValue(opts.Json),
                    CompactJson = parseResult.GetValue(compactOption),
                    MermaidOutput = opts.ResolveFormat(parseResult) == OutputFormat.Mermaid,
                    EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                    Rows = opts.ParseRows(parseResult),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
                };
                return await DependsCommand.ExecuteLibraryDependsAsync(libOptions);
            }

            if (exitCode == DependsCommand.TypeNotFoundExitCode)
            {
                Console.Error.WriteLine($"Type '{targetType}' not found in the specified scope.");
                return 1;
            }

            return exitCode;
        });

        return dependsCommand;
    }
}
