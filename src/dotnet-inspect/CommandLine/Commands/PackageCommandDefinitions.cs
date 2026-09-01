using DotnetInspector.Output;
using System.CommandLine;
using System.Globalization;
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

        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Legacy alias for -S Dependencies --tree (tip: use 'depends --package' instead)" };
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
        outOption.Aliases.Add("--output");
        outOption.Aliases.Add("-o");
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter SourceLink: Files rows by type glob/name (e.g., *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show resolved version)", Arity = ArgumentArity.ZeroOrOne };
        var latestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest stable version from eligible configured sources (add --preview for prerelease)" };
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
        var searchCommand = CreatePackageSearchCommand(
            opts,
            packageCommand,
            packageNameArg,
            prereleaseOption,
            outOption);
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
                    // else the parser did not recognize gets the plain complaint. Both go
                    // through CommandError, which owns the "Error: " prefix and containment.
                    CommandError.Write(
                        ArgumentPreprocessor.GetRemovedPackageOptionError(error.Option) is { } removed
                            ? removed
                            : $"Unrecognized option '{error.Option}'.");
                    return 1;

                case PackageOptionsParser.ValidationError validation:
                    CommandError.Write(validation.Message);
                    return 1;

                case PackageOptionsParser.Success success:
                {
                    bool packageLensHandlesRows =
                        success.Options.ListVersions
                        || success.Options.ListLayout
                        || success.Options.ListTfms
                        || success.Options.ShowContent;
                    if (!packageLensHandlesRows
                        && success.Options.PackageArgs.Length > 0
                        && opts.RejectUnsupportedDocumentJsonRowWindowBeforeAcquisition(
                            parseResult,
                            PackageCommand.Name))
                    {
                        return 1;
                    }

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
    public static Command CreatePackageSearchCommand(
        SharedOptions opts,
        Command packageCommand,
        Argument<string[]> inheritedPackageArgument,
        Option<bool> inheritedPrereleaseOption,
        Option<string?> inheritedOutOption)
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
        searchCommand.Options.Add(opts.Rows);
        searchCommand.Options.Add(opts.Lines);
        searchCommand.Options.Add(opts.TailLines);
        searchCommand.Options.Add(opts.Head);
        searchCommand.Options.Add(opts.Tail);
        searchCommand.Options.Add(opts.Count);
        searchCommand.Options.Add(opts.Fields);
        searchCommand.Options.Add(opts.Columns);
        opts.AddNuGetOptionsTo(searchCommand);
        opts.AddRowWindowValidators(searchCommand);
        searchCommand.Validators.Add(result =>
        {
            bool lineMode =
                result.GetValue(opts.Lines)
                || result.GetValue(opts.TailLines);
            int? resultLimit = null;
            var limitResult = result.GetResult(opts.Limit);
            if (limitResult is { Implicit: false }
                && limitResult.Tokens.Count > 0)
            {
                if (!int.TryParse(
                    limitResult.Tokens[^1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedLimit))
                {
                    return;
                }

                if (!lineMode)
                    resultLimit = parsedLimit;

                if (parsedLimit <= 0)
                {
                    result.AddError(
                        "-n requires a positive package search result limit greater than zero.");
                }
            }

            bool hasDirection =
                result.GetValue(opts.Head) || result.GetValue(opts.Tail);
            bool hasRows =
                result.GetResult(opts.Rows) is { Implicit: false };
            if (!lineMode && hasDirection && !hasRows && resultLimit is null)
            {
                result.AddError(
                    "--head/--tail requires a carrier: use -n for result rows "
                    + "or --rows for data rows.");
            }

            if (resultLimit is null)
                return;

            if (result.GetValue(opts.Tail))
            {
                result.AddError(
                    "--tail cannot be combined with -n for package search "
                    + "because bounded remote pages do not establish a suffix.");
            }

            if (result.GetResult(takeOption) is { Implicit: false }
                && resultLimit > 0)
            {
                result.AddError(
                    "--take and -n both limit package search results; choose one.");
            }
        });

        searchCommand.SetAction(async (parseResult, ct) =>
        {
            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Json,
                opts.Markdown,
                opts.Verbose,
                opts.Info,
                opts.Limit,
                opts.Count,
                opts.Source,
                opts.AddSource,
                opts.NuGetConfig,
                opts.Print,
                opts.Value,
                opts.Urls,
                opts.Paths,
                opts.Rows,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines,
                opts.Fields,
                opts.Columns,
                inheritedPrereleaseOption,
                inheritedOutOption,
            };
            var unsupportedParentOption = packageCommand.Options.FirstOrDefault(
                option => !acceptedParentOptions.Contains(option)
                    && parseResult.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                CommandError.Write(
                    $"{unsupportedParentOption.Name} is not available with package search.");
                return 1;
            }

            if (parseResult.GetValue(inheritedPackageArgument) is { Length: > 0 })
            {
                CommandError.Write(
                    "A package target is not available with package search; "
                    + "place 'search' immediately after 'package'.");
                return 1;
            }

            var query = parseResult.GetValue(queryArg);

            if (string.IsNullOrEmpty(query))
            {
                CommandError.WriteLine("Usage: package search <query>");
                CommandError.WriteBlankLine();
                CommandError.WriteLine("Examples:");
                CommandError.WriteLine("  package search Azure.AI");
                CommandError.WriteLine("  package search AWSSDK --take 50");
                CommandError.WriteLine("  package search \"json serializer\" --json");
                CommandError.WriteLine("  package search Contoso --source https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json");
                return 0;
            }

            var projection = ProjectionAudit.Requested(parseResult, opts);
            bool lineMode = opts.IsLinesRequested(parseResult);
            var options = new PackageSearchOptions
            {
                Query = query,
                Take = lineMode
                    ? parseResult.GetValue(takeOption)
                    : parseResult.GetValue(opts.Limit)
                        ?? parseResult.GetValue(takeOption),
                Prerelease =
                    parseResult.GetValue(inheritedPrereleaseOption)
                    || parseResult.GetValue(prereleaseOption),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Count = projection.Count,
                Print = projection.Print,
                Value = projection.Value,
                Urls = projection.Urls,
                Paths = projection.Paths,
                OutputPath = parseResult.GetValue(inheritedOutOption),
                Rows = projection.Rows,
                Fields = projection.Fields,
                Columns = projection.Columns,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await PackageSearchCommand.ExecuteAsync(options);
        });

        return searchCommand;
    }
}
