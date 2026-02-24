using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the diff and library (assembly) commands.
/// </summary>
public static class InspectionCommandDefinitions
{
    public static Command CreateDiffCommand(SharedOptions opts)
    {
        var diffCommand = new Command(DiffCommand.Name, "Compare API surfaces between package or platform versions");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Version range and type filter. When no --package/--platform is given, first arg is the package version range.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package")
        {
            Description = "Package with version range (e.g., System.Text.Json@9.0.0..10.0.2)"
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform library with version range (e.g., System.Text.Json@8.0.23..10.0.2)"
        };
        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework for platform diff (runtime, aspnetcore). Default: runtime"
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden/obsolete members" };
        var typeFilterOption = new Option<string[]>("-t")
        {
            Description = "Filter to specific type(s)",
            AllowMultipleArgumentsPerToken = true
        };
        typeFilterOption.Aliases.Add("--type");
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names that changed" };
        var breakingOption = new Option<bool>("--breaking") { Description = "Show only breaking changes" };
        var additiveOption = new Option<bool>("--additive") { Description = "Show only additive changes" };

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(oneLineOption);
        diffCommand.Options.Add(noHeaderOption);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Select);

        var commandArgs = new DiffOptionsParser.DiffCommandArgs(
            argsArg, packageOption, platformOption, frameworkOption, tfmOption, allOption,
            typeFilterOption, oneLineOption, noHeaderOption, nameOnlyOption, breakingOption, additiveOption);

        diffCommand.SetAction(async (parseResult, ct) =>
        {
            var result = DiffOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case DiffOptionsParser.VersionNumberError error:
                    Console.Error.WriteLine($"Error: '{error.Value}' looks like a version number. Use '{error.VersionRange}@{error.Value}' to specify a version.");
                    return 1;

                case DiffOptionsParser.Success success:
                    var exitCode = await DiffCommand.ExecuteAsync(success.Options);

                    if (exitCode == 0)
                    {
                        var tips = DiffOptionsParser.BuildTips(success.Options, success.Options.TypeFilter);
                        Hints.WriteTips(success.TipLevel, [.. tips]);
                    }

                    return exitCode;

                default:
                    return 1;
            }
        });

        return diffCommand;
    }

    public static Command CreateAssemblyCommand(SharedOptions opts)
    {
        var assemblyCommand = new Command("library", "Inspect a .NET library file");

        var assemblyPathArg = new Argument<string?>("source")
        {
            Description = "Library file path, NuGet package name (e.g., System.Text.Json), or package@version",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var sourcelinkAuditOption = new Option<bool>("--source-link-audit") { Description = "Full provenance verification (parallel HTTP HEAD on all source files)" };
        var referencesOption = new Option<bool>("--references") { Description = "Show library references" };
        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show library dependencies as a tree (tip: use 'depends --library' instead)" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform library (e.g., System.Text.Json)" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Inspect library from NuGet package (e.g., System.Text.Json or System.Text.Json@9.0.4)" };
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore). Use @version for specific version" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0, or 'all' for every TFM)" };
        var extractResourcesOption = new Option<string?>("--extract-resources") { Description = "Extract embedded resources to a directory" };

        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(sourcelinkAuditOption);
        assemblyCommand.Options.Add(referencesOption);
        assemblyCommand.Options.Add(dependenciesOption);
        assemblyCommand.Options.Add(asmPlatformOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(asmFrameworkOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(extractResourcesOption);
        opts.AddAllOptionsTo(assemblyCommand);

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            var source = parseResult.GetValue(assemblyPathArg);
            var explicitPackage = parseResult.GetValue(asmPackageOption);
            var explicitPlatform = parseResult.GetValue(asmPlatformOption);

            // Disambiguate positional arg: local file vs package name
            string? assemblyPath = null;
            string? packagePath = explicitPackage;
            string? platformAssembly = explicitPlatform;

            if (!string.IsNullOrEmpty(source) && string.IsNullOrEmpty(explicitPlatform) && string.IsNullOrEmpty(explicitPackage))
            {
                if (File.Exists(source))
                    assemblyPath = source;
                else if (!source.Contains('@') && PlatformResolver.IsPlatformCandidate(source))
                {
                    // Platform-preferred routing for System.*/Microsoft.* bare names
                    bool verbose = parseResult.GetValue(opts.Verbose);
                    Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                    var (asmPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(source, HttpClientFactory.Shared, log);
                    if (error == null && asmPath != null)
                        platformAssembly = source;
                    else
                        packagePath = source;
                }
                else
                    packagePath = source;
            }

            bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

            bool showReferences = parseResult.GetValue(referencesOption);
            bool showDependencies = parseResult.GetValue(dependenciesOption);

            var options = new AssemblyOptions
            {
                AssemblyName = assemblyPath,
                IncludeMetadata = true,
                IncludeSourcelinkAudit = runSourcelinkAudit,
                IncludeReferences = showReferences,
                IncludeDependencies = showDependencies,
                PackagePath = packagePath,
                PlatformAssembly = platformAssembly,
                PlatformFramework = parseResult.GetValue(asmFrameworkOption),
                Tfm = parseResult.GetValue(asmTfmOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                ExtractResources = parseResult.GetValue(extractResourcesOption)
            };

            return await AssemblyCommand.ExecuteAsync(options);
        });

        return assemblyCommand;
    }
}
