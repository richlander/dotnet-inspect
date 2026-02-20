using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the package and package search commands.
/// </summary>
public static class PackageCommandDefinitions
{
    /// <summary>
    /// Creates the package command for inspecting NuGet packages.
    /// </summary>
    public static Command CreatePackageCommand(SharedOptions opts)
    {
        var packageCommand = new Command(PackageCommand.Name, "Inspect a NuGet package");

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "NuGet package name or path to .nupkg file, optionally with version (e.g., System.Text.Json@9.0.0)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show transitive package dependency tree" };
        var layoutOption = new Option<bool>("--layout") { Description = "Show package file tree" };
        var filesOption = new Option<bool>("--files") { Description = "List files in the package (flat list, filterable with --tfm)" };
        var tfmsOption = new Option<bool>("--tfms") { Description = "List target frameworks in the package" };
        var libOption = new Option<bool>("--lib") { Description = "Scope to lib/ folder (use with --files or --layout)" };
        var toolsOption = new Option<bool>("--tools") { Description = "Scope to tools/ folder (use with --files or --layout)" };
        var versionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        versionsOption.DefaultValueFactory = _ => null;
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var readmeOption = new Option<bool>("--readme") { Description = "Show the README.md content from the package" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show latest)", Arity = ArgumentArity.ZeroOrOne };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };

        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(dependenciesOption);
        packageCommand.Options.Add(layoutOption);
        packageCommand.Options.Add(filesOption);
        packageCommand.Options.Add(tfmsOption);
        packageCommand.Options.Add(libOption);
        packageCommand.Options.Add(toolsOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(readmeOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(versionOption);
        packageCommand.Options.Add(outOption);
        packageCommand.Options.Add(oneLineOption);
        packageCommand.Options.Add(noHeaderOption);
        packageCommand.Options.Add(opts.Json);
        packageCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(packageCommand);
        opts.AddSectionOptionsTo(packageCommand);
        opts.AddNuGetOptionsTo(packageCommand);

        // Search subcommand
        var searchCommand = CreatePackageSearchCommand(opts);
        packageCommand.Subcommands.Add(searchCommand);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var explicitVersion = parseResult.GetValue(versionOption);

            // Bare --version (no value): treat as --versions 1
            bool bareVersion = explicitVersion == null && parseResult.GetResult(versionOption) is { Implicit: false };

            var versionsValue = parseResult.GetValue(versionsOption);
            bool showVersions = bareVersion || parseResult.GetResult(versionsOption) is { Implicit: false };

            var verbosity = opts.ParseVerbosity(parseResult);

            var options = new InspectionOptions
            {
                PackageArgs = packageArgs,
                ExplicitVersion = explicitVersion,
                ShowDependencies = parseResult.GetValue(dependenciesOption),
                Tfm = parseResult.GetValue(tfmOption),
                ListLayout = parseResult.GetValue(layoutOption),
                ListFiles = parseResult.GetValue(filesOption),
                ListTfms = parseResult.GetValue(tfmsOption),
                ScopeLib = parseResult.GetValue(libOption),
                ScopeTools = parseResult.GetValue(toolsOption),
                ListVersions = showVersions,
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                ShowReadme = parseResult.GetValue(readmeOption),
                OutputPath = parseResult.GetValue(outOption),
                Limit = bareVersion ? 1 : versionsValue,
                JsonOutput = parseResult.GetValue(opts.Json),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = verbosity,
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            var tipLevel = options.IsRawOutput || verbosity != Verbosity.Minimal || options.IncludeSections != null || ArgumentPreprocessor.HeadLines != null || options.Limit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && packageArgs.Length > 0 && !options.IsRawOutput)
            {
                var pkg = packageArgs[0];
                if (pkg.Contains('@')) pkg = pkg[..pkg.IndexOf('@')];
                TipWriter.WritePackageTips(pkg, tipLevel, options.Verbosity);
            }

            return exitCode;
        });

        return packageCommand;
    }

    /// <summary>
    /// Creates the package search subcommand for searching NuGet packages.
    /// </summary>
    public static Command CreatePackageSearchCommand(SharedOptions opts)
    {
        var searchCommand = new Command(PackageSearchCommand.Name, "Search NuGet for packages by keyword");

        var queryArg = new Argument<string?>("query")
        {
            Description = "Search query (keyword or package name prefix)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var takeOption = new Option<int>("--take")
        {
            Description = "Maximum number of results (default: 20)",
            DefaultValueFactory = _ => 20
        };
        var prereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        searchCommand.Arguments.Add(queryArg);
        searchCommand.Options.Add(takeOption);
        searchCommand.Options.Add(prereleaseOption);
        searchCommand.Options.Add(opts.Json);
        searchCommand.Options.Add(compactOption);
        searchCommand.Options.Add(opts.Verbose);
        searchCommand.Options.Add(opts.Limit);

        searchCommand.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArg);

            if (string.IsNullOrEmpty(query))
            {
                Console.Error.WriteLine("Usage: package search <query>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Examples:");
                Console.Error.WriteLine("  package search Azure.AI");
                Console.Error.WriteLine("  package search AWSSDK --take 50");
                Console.Error.WriteLine("  package search \"json serializer\" --json");
                return 0;
            }

            var options = new PackageSearchOptions
            {
                Query = query,
                Take = parseResult.GetValue(takeOption),
                Prerelease = parseResult.GetValue(prereleaseOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose)
            };

            return await PackageSearchCommand.ExecuteAsync(options);
        });

        return searchCommand;
    }
}
