using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;
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

        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show transitive package dependency tree (tip: use 'depends --package' instead)" };
        var layoutOption = new Option<bool>("--layout") { Description = "Show package file tree" };
        var pathOption = new Option<string[]>("--path")
        {
            Description = "List package files with sizes (the Package files section), scoped to a file, directory, glob, @readme (README.md > PACKAGE.md), or @agents. Can repeat. Pass --path with no value for the whole package.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var pathMatchOption = new Option<string?>("--match") { Description = "For repeated --path: all (default) or first matching selector per package" };
        var skipEmptyOption = new Option<bool>("--skip-empty") { Description = "With multi-package Files rows, omit packages with no matching files" };
        var tfmsOption = new Option<bool>("--tfms") { Description = "List target frameworks in the package" };
        var libOption = new Option<bool>("--lib") { Description = "Scope to lib/ folder (use with --layout)" };
        var toolsOption = new Option<bool>("--tools") { Description = "Scope to tools/ folder (use with --layout)" };
        var libraryOption = new Option<string?>("--library")
        {
            Description = "Inspect a library from this package; omit value to select the primary library when unambiguous",
            Arity = ArgumentArity.ZeroOrOne
        };
        var allLibrariesOption = new Option<bool>("--all-libraries")
        {
            Description = "Inspect all compatible libraries from this package"
        };
        var versionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        versionsOption.DefaultValueFactory = _ => null;
        var versionsWithFeedOption = new Option<int?>("--versions-with-feed") { Description = "List available versions with the feed each came from (optionally limit version count)", Arity = ArgumentArity.ZeroOrOne };
        versionsWithFeedOption.DefaultValueFactory = _ => null;
        var prereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions for --versions and latest resolution" };
        prereleaseOption.Aliases.Add("--prerelease");
        var includeUnlistedOption = new Option<bool>("--include-unlisted") { Description = "Include unlisted versions in --versions output, marked as unlisted" };
        var contentOption = new Option<bool>("--content") { Description = "Print contents of files selected by --path; use --jsonl for structured rows" };
        var frontmatterOption = new Option<bool>("--frontmatter") { Description = "When printing markdown content, output only the leading YAML frontmatter block" };
        frontmatterOption.Aliases.Add("--yaml-header");
        var bodyOption = new Option<bool>("--body") { Description = "When printing markdown content, output only content after YAML frontmatter" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter SourceLink: Files rows by type glob/name (e.g., *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show resolved version)", Arity = ArgumentArity.ZeroOrOne };
        var latestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest stable version from nuget.org (add --preview for prerelease)" };
        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(dependenciesOption);
        packageCommand.Options.Add(layoutOption);
        packageCommand.Options.Add(pathOption);
        packageCommand.Options.Add(pathMatchOption);
        packageCommand.Options.Add(skipEmptyOption);
        packageCommand.Options.Add(tfmsOption);
        packageCommand.Options.Add(libOption);
        packageCommand.Options.Add(toolsOption);
        packageCommand.Options.Add(libraryOption);
        packageCommand.Options.Add(allLibrariesOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(versionsWithFeedOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(includeUnlistedOption);
        packageCommand.Options.Add(contentOption);
        packageCommand.Options.Add(frontmatterOption);
        packageCommand.Options.Add(bodyOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(typeFilterOption);
        packageCommand.Options.Add(versionOption);
        packageCommand.Options.Add(latestVersionOption);
        packageCommand.Options.Add(opts.RawUrls);
        packageCommand.Options.Add(opts.BrowsableUrls);
        packageCommand.Options.Add(opts.Bare);
        packageCommand.Options.Add(outOption);
        opts.AddTableOptionsTo(packageCommand);
        packageCommand.Options.Add(opts.Json);
        packageCommand.Options.Add(opts.Markdown);
        packageCommand.Options.Add(opts.PlainText);
        opts.AddOutputOptionsTo(packageCommand);
        opts.AddSectionOptionsTo(packageCommand);
        opts.AddCountOptionTo(packageCommand);
        opts.AddPrintOptionTo(packageCommand);
        opts.AddShapeProjectionOptionsTo(packageCommand);
        opts.AddNuGetOptionsTo(packageCommand);

        // Search subcommand
        var searchCommand = CreatePackageSearchCommand(opts);
        packageCommand.Subcommands.Add(searchCommand);

        var commandArgs = new PackageOptionsParser.PackageCommandArgs(
            packageNameArg, dependenciesOption, layoutOption, pathOption, tfmsOption,
            libOption, toolsOption, libraryOption, allLibrariesOption, versionsOption, versionsWithFeedOption, prereleaseOption, includeUnlistedOption,
            contentOption, frontmatterOption, bodyOption,
            tfmOption, typeFilterOption, versionOption, latestVersionOption, outOption, pathMatchOption, skipEmptyOption, opts.NoHeaders);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var result = PackageOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case PackageOptionsParser.UnrecognizedOption error:
                    // A spelling this command removed is answered with its replacement; anything
                    // else the parser did not recognize gets the plain complaint.
                    Console.Error.WriteLine(
                        ArgumentPreprocessor.GetRemovedPackageOptionError(error.Option) is { } removed
                            ? $"Error: {removed}"
                            : $"Error: Unrecognized option '{error.Option}'.");
                    return 1;

                case PackageOptionsParser.Success success:
                {
                    var exitCode = await PackageCommand.ExecuteAsync(success.Options);

                    if (exitCode == 0 && success.Options.PackageArgs.Length > 0 && success.Options.PackageLibrary == null && !success.Options.AllLibraries && !success.Options.FormatExplicitlySet && !success.Options.IsRawOutput)
                    {
                        var target = PackageExtractor.ParsePackageTarget(success.Options.PackageArgs[0]);
                        var pkg = target.IsLocalFile
                            ? target.OriginalArgument
                            : PackageExtractor.ParsePackageReference(target.OriginalArgument).name;
                        TipWriter.WritePackageTips(pkg, success.Options.TipLevel, success.Verbosity);
                    }

                    return exitCode;
                }

                default:
                    return 1;
            }
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
        searchCommand.Options.Add(opts.Count);
        opts.AddNuGetOptionsTo(searchCommand);

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
                Console.Error.WriteLine("  package search Contoso --source https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json");
                return 0;
            }

            var options = new PackageSearchOptions
            {
                Query = query,
                Take = parseResult.GetValue(takeOption),
                Prerelease = parseResult.GetValue(prereleaseOption),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Count = parseResult.GetValue(opts.Count),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await PackageSearchCommand.ExecuteAsync(options);
        });

        return searchCommand;
    }
}
