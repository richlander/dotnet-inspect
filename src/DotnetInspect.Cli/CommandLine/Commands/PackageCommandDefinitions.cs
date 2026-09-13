using DotnetInspect.Cli.Output;
using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the package command and its package-space query surface.
/// </summary>
public static class PackageCommandDefinitions
{
    /// <summary>
    /// Creates the package command for inspecting NuGet packages.
    /// </summary>
    public static Command CreatePackageCommand(
        SharedOptions opts,
        out PackageOptionsParser.PackageCommandArgs structuralArgs)
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
        var versionsOption = new Option<bool>("--versions")
        {
            Description = "List available versions; use -n N to select N version rows",
            Arity = ArgumentArity.Zero
        };
        var versionsWithFeedOption = new Option<bool>("--versions-with-feed")
        {
            Description = "List available versions with source feeds; use -n N to select N version/feed rows",
            Arity = ArgumentArity.Zero
        };
        var linesOption = new Option<bool>("--lines")
        {
            Description = "Apply -n to rendered lines instead of version rows",
            Arity = ArgumentArity.Zero
        };
        var tailLinesOption = new Option<bool>("--tail-lines")
        {
            Description = "Apply -n to rendered lines from the end",
            Arity = ArgumentArity.Zero
        };
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
        packageCommand.Options.Add(linesOption);
        packageCommand.Options.Add(tailLinesOption);
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
        opts.AddOutputOptionsTo(
            packageCommand,
            validateLegacyRowWindow: result =>
                !result.GetValue(versionsOption)
                && !result.GetValue(versionsWithFeedOption));
        opts.AddSectionOptionsTo(packageCommand);
        opts.AddCountOptionTo(packageCommand);
        opts.AddPrintOptionTo(packageCommand);
        opts.AddShapeProjectionOptionsTo(packageCommand);
        opts.AddNuGetOptionsTo(packageCommand);
        packageCommand.Validators.Add(result =>
        {
            bool hasPluralVersionSelector =
                result.GetValue(versionsOption)
                || result.GetValue(versionsWithFeedOption);
            bool hasLineSelection =
                result.GetValue(linesOption)
                || result.GetValue(tailLinesOption);
            if (!hasPluralVersionSelector
                && hasLineSelection)
            {
                result.AddError(
                    "--lines and --tail-lines are available with "
                    + "--versions or --versions-with-feed.");
            }

        });

        CliRowSelectionCommandRegistry.Register(
            packageCommand,
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
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            result =>
                result.GetValue(versionsOption)
                || result.GetValue(versionsWithFeedOption),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.ResolveFormat(result),
                    lowering));

        var queryCommand = CreatePackageQueryCommand(
            opts,
            packageCommand,
            packageNameArg,
            prereleaseOption);
        packageCommand.Subcommands.Add(queryCommand);

        var commandArgs = new PackageOptionsParser.PackageCommandArgs(
            packageNameArg, dependenciesOption, layoutOption, pathOption, tfmsOption,
            libOption, toolsOption, libraryOption, allLibrariesOption, versionsOption, versionsWithFeedOption, prereleaseOption, includeUnlistedOption,
            contentOption, frontmatterOption, bodyOption,
            tfmOption, typeFilterOption, versionOption, latestVersionOption,
            linesOption, tailLinesOption, outOption, pathMatchOption,
            skipEmptyOption, opts.NoHeaders);
        structuralArgs = commandArgs;

        CliOptionValueValidation.RegisterCapacity(
            packageNameArg,
            result => PackageOptionsParser.GetPositionalCapacity(result, opts, commandArgs));

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            if (parseResult.GetValue(packageNameArg) is [var first, ..]
                && first.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    "'package search' has been removed. Use 'package query <ID>' "
                    + "for an exact package or 'package query <PREFIX>*' for "
                    + "package-prefix discovery.");
                return 1;
            }

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

                case PackageOptionsParser.InvalidArguments error:
                    CommandError.Write(error.Message);
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
    /// Creates the host-neutral package query subcommand.
    /// </summary>
    public static Command CreatePackageQueryCommand(
        SharedOptions opts,
        Command packageCommand,
        Argument<string[]> inheritedPackageArgument,
        Option<bool> inheritedPrereleaseOption)
    {
        var queryCommand = new Command(
            "query",
            "Query exact package IDs or package-ID prefixes");

        var inputArg = new Argument<string?>("package")
        {
            Description =
                "Exact package ID or literal package-ID prefix ending in '*'",
            Arity = ArgumentArity.ZeroOrOne
        };
        var takeOption = new Option<string[]>("--take")
        {
            Description =
                "Maximum package candidates to inspect "
                + $"(otherwise default {PackageQuery.DefaultMaximumCandidates}; "
                + $"{PackageQuery.MaximumPackageContentCandidates} for "
                + "package-content queries; maximum "
                + $"{PackageQueryOptions.MaximumCandidates})",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description = "Include prerelease versions"
        };
        prereleaseOption.Aliases.Add("--prerelease");
        var nuspecOnlyOption = new Option<bool>("--nuspec-only")
        {
            Description =
                "Reject queries that require package archive content"
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Minified JSON (use with --json)"
        };
        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");

        queryCommand.Arguments.Add(inputArg);
        queryCommand.Options.Add(takeOption);
        queryCommand.Options.Add(prereleaseOption);
        queryCommand.Options.Add(nuspecOnlyOption);
        queryCommand.Options.Add(opts.RowWhere);
        queryCommand.Options.Add(opts.Json);
        queryCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(queryCommand);
        queryCommand.Options.Add(opts.Limit);
        queryCommand.Options.Add(opts.Rows);
        queryCommand.Options.Add(opts.Head);
        queryCommand.Options.Add(opts.Tail);
        queryCommand.Options.Add(opts.Count);
        queryCommand.Options.Add(opts.Fields);
        queryCommand.Options.Add(opts.Columns);
        queryCommand.Options.Add(opts.Discover);
        queryCommand.Options.Add(opts.QueryHelp);
        queryCommand.Options.Add(opts.Select);
        queryCommand.Options.Add(opts.Tree);
        opts.AddNuGetOptionsTo(queryCommand);

        queryCommand.SetAction(async (parseResult, ct) =>
        {
            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Json,
                opts.Markdown,
                opts.Table,
                opts.Tsv,
                opts.Jsonl,
                opts.NoHeaders,
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
                opts.Fields,
                opts.Columns,
                opts.Discover,
                opts.Select,
                opts.Tree,
                opts.QueryHelp,
                inheritedPrereleaseOption,
            };
            var unsupportedParentOption = packageCommand.Options.FirstOrDefault(
                option => !acceptedParentOptions.Contains(option)
                    && parseResult.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                CommandError.Write(
                    $"{unsupportedParentOption.Name} is not available with package query.");
                return 1;
            }

            if (parseResult.GetValue(inheritedPackageArgument) is { Length: > 0 })
            {
                CommandError.Write(
                    "A package inspection target is not available with package query; "
                    + "place 'query' immediately after 'package'.");
                return 1;
            }

            NuGetSourceOptions sourceOptions =
                opts.ParseNuGetSourceOptions(parseResult);
            if (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null)
            {
                CommandError.Write(
                    "Package Query currently uses NuGet.org and cannot be combined "
                    + "with source overrides.");
                return 1;
            }

            string[]? discover = opts.ParseDiscover(parseResult);
            OutputFormat format = opts.ResolveFormat(parseResult);
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Package Query",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }
            if (discover is not null)
            {
                var discoveryOptions = new PackageQueryOptions
                {
                    Plan = ((PackageQueryPlanResult.Accepted)PackageQuery.PlanInput(
                        "dotnet-inspect",
                        maximumMatches: null)).Plan,
                    Discover = discover,
                    Tree = opts.ParseTree(parseResult),
                    JsonOutput = format == OutputFormat.Json,
                    Tabular = opts.ResolveTabular(parseResult),
                    Tsv = opts.ResolveTsv(parseResult),
                    Jsonl = opts.ResolveJsonl(parseResult),
                    Columns = opts.ParseColumns(parseResult),
                    Fields = opts.ParseFields(parseResult),
                    RowSelection = rowSelection,
                    Count = parseResult.GetValue(opts.Count),
                };
                return await PackageQueryCommand.ExecuteAsync(
                    discoveryOptions,
                    new CommandContext(verbose: false),
                    ct);
            }

            string? input = parseResult.GetValue(inputArg);
            if (string.IsNullOrWhiteSpace(input))
            {
                CommandError.Write(
                    "Package Query requires an exact package ID or a literal "
                    + "package-ID prefix ending in '*'.");
                return 1;
            }

            string[]? select = opts.ParseSelect(parseResult);
            if (select is not null)
            {
                SelectResult selection = SelectResolver.ResolveSelectAsSections(
                    select,
                    [PackageProfileSections.Packages],
                    categories: new Dictionary<string, string[]>());
                if (SelectOutput.WriteUnresolved(selection))
                    return 1;
                if (selection.Sections?.Contains(PackageProfileSections.Packages) != true)
                {
                    CommandError.Write(
                        "Package Query data selection must include Packages.");
                    return 1;
                }
            }

            if (!PackageQueryOptions.TryCreate(
                    input,
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    parseResult.GetValue(nuspecOnlyOption),
                    CliExecutionBoundCommandRegistry.GetPreparedValue(parseResult),
                    rowSelection,
                    parseResult.GetValue(inheritedPrereleaseOption)
                        || parseResult.GetValue(prereleaseOption),
                    out PackageQueryOptions? options,
                    out OptionError error))
            {
                CommandError.Write(error);
                return 1;
            }

            options = options! with
            {
                RowSelection = rowSelection,
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = format == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
            };
            return await PackageQueryCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                ct);
        });

        CliRowSelectionCommandRegistry.Register(
            queryCommand,
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
            _ => true);
        CliExecutionBoundCommandRegistry.Register(
            queryCommand,
            takeOption,
            _ => PackageQueryOptions.MaximumCandidates,
            isActive: static _ => true);

        return queryCommand;
    }
}
