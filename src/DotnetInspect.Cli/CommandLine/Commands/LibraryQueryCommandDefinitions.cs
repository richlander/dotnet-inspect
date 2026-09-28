using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryQueryCommandDefinitions
{
    internal static Command Create(
        SharedOptions opts,
        Command libraryCommand,
        Argument<string?> inheritedSourceArgument)
    {
        var command = new Command(
            "query",
            "Query an explicit directory or platform-framework Library population");
        var sourceArgument = new Argument<string?>("directory")
        {
            Description =
                "Directory whose top-level *.dll files form the Library population",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description =
                "Platform reference-pack population "
                + "(runtime, aspnetcore, netstandard, or family@version)",
            Arity = ArgumentArity.ExactlyOne,
        };
        var takeOption = new Option<string[]>("--take")
        {
            Description =
                "Maximum Library candidates to inspect "
                + $"(default {LibraryQuery.DefaultMaximumCandidates}; "
                + $"maximum {LibraryQuery.MaximumCandidates})",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description =
                "Minified JSON (use with --json or --envelope)",
        };

        command.Arguments.Add(sourceArgument);
        command.Options.Add(platformOption);
        command.Options.Add(takeOption);
        command.Options.Add(opts.RowWhere);
        command.Options.Add(opts.Json);
        command.Options.Add(compactOption);
        opts.AddTableOptionsTo(command);
        command.Options.Add(opts.Limit);
        command.Options.Add(opts.Rows);
        command.Options.Add(opts.Head);
        command.Options.Add(opts.Tail);
        command.Options.Add(opts.Lines);
        command.Options.Add(opts.TailLines);
        command.Options.Add(opts.Count);
        command.Options.Add(opts.Fields);
        command.Options.Add(opts.Columns);
        command.Options.Add(opts.Discover);
        command.Options.Add(opts.QueryHelp);
        command.Options.Add(opts.Select);
        command.Options.Add(opts.Tree);
        opts.AddNuGetOptionsTo(command);
        opts.AddEnvelopeOptionTo(
            command,
            opts.Limit,
            opts.Rows,
            opts.Head,
            opts.Tail,
            opts.Lines,
            opts.TailLines,
            opts.Count,
            opts.Discover,
            opts.QueryHelp,
            opts.Select);
        command.Validators.Add(result =>
        {
            if (result.GetResult(compactOption) is { Implicit: false }
                && !result.GetValue(opts.Json)
                && !result.GetValue(opts.Envelope))
            {
                result.AddError(
                    "--compact requires library query --json or --envelope.");
            }
            if (result.GetValue(opts.Json)
                && result.GetValue(opts.Tree)
                && result.GetResult(opts.Discover)
                    is not { Implicit: false })
            {
                result.AddError(
                    "--tree with library query --json requires schema discovery.");
            }
        });

        command.SetAction(async (parseResult, ct) =>
        {
            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Envelope,
                opts.Json,
                opts.Markdown,
                opts.Table,
                opts.Tsv,
                opts.Jsonl,
                opts.NoHeaders,
                opts.Limit,
                opts.Count,
                opts.Source,
                opts.AddSource,
                opts.NuGetConfig,
                opts.Rows,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines,
                opts.Fields,
                opts.Columns,
                opts.Discover,
                opts.Select,
                opts.Tree,
                opts.QueryHelp,
                opts.RowWhere,
            };
            Option? unsupportedParentOption =
                libraryCommand.Options.FirstOrDefault(option =>
                    !acceptedParentOptions.Contains(option)
                    && parseResult.GetResult(option)
                        is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                CommandError.Write(
                    $"{unsupportedParentOption.Name} is not available "
                    + "with library query.");
                return 1;
            }

            if (parseResult.GetValue(inheritedSourceArgument) is not null)
            {
                CommandError.Write(
                    "A Library inspection target is not available with "
                    + "library query; place 'query' immediately after 'library'.");
                return 1;
            }

            string[]? discover = opts.ParseDiscover(parseResult);
            bool envelopeOutput = parseResult.GetValue(opts.Envelope);
            OutputFormat format = envelopeOutput
                ? OutputFormat.Json
                : opts.ResolveFormat(parseResult);
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Library Query",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }
            if (!LibraryQueryOptions.TryCreate(
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    CliExecutionBoundCommandRegistry.GetPreparedValue(
                        parseResult),
                    rowSelection,
                    out LibraryQueryPlan? plan,
                    out OptionError planError))
            {
                CommandError.Write(planError);
                return 1;
            }

            LibraryQueryPopulation? population = null;
            string? directory = parseResult.GetValue(sourceArgument);
            string? platform = parseResult.GetValue(platformOption);
            if (discover is null)
            {
                if (string.IsNullOrWhiteSpace(directory)
                    == string.IsNullOrWhiteSpace(platform))
                {
                    CommandError.Write(
                        "Library Query requires exactly one directory or "
                        + "--platform framework.");
                    return 1;
                }
                population = !string.IsNullOrWhiteSpace(directory)
                    ? new LibraryQueryPopulation.Directory(directory)
                    : new LibraryQueryPopulation.PlatformFramework(
                        platform!);
            }

            string[]? select = opts.ParseSelect(parseResult);
            HashSet<string>? includeSections = null;
            if (select is not null)
            {
                SelectResult selection =
                    SelectResolver.ResolveSelectAsSections(
                        select,
                        LibraryQuerySections.Catalog
                            .SelectableSectionNames,
                        categories:
                            LibraryQuerySections.Catalog
                                .SelectionCategoryMap);
                if (SelectOutput.WriteUnresolved(selection))
                    return 1;
                includeSections = selection.Sections;
                if (parseResult.GetValue(opts.Count)
                    && (includeSections is not { Count: 1 }
                        || !includeSections.Contains(
                            LibraryQuerySections.LibrariesName)))
                {
                    CommandError.Write(
                        "Library Query --count supports the Libraries section only.");
                    return 1;
                }
                if (!parseResult.GetValue(opts.Count)
                    && !OutputFormatResolver
                        .ValidateSingleSectionForTabular(
                            opts.IsTableExplicitlySet(parseResult),
                            includeSections))
                {
                    return 1;
                }
            }

            var options = new LibraryQueryOptions
            {
                Plan = plan!,
                Population = population,
                SourceOptions =
                    opts.ParseNuGetSourceOptions(parseResult),
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = format == OutputFormat.Json,
                EnvelopeOutput = envelopeOutput,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular =
                    !envelopeOutput
                    && opts.ResolveTabular(parseResult),
                Tsv =
                    !envelopeOutput
                    && opts.ResolveTsv(parseResult),
                Jsonl =
                    !envelopeOutput
                    && opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Discover = discover,
                IncludeSections = includeSections,
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Tree = opts.ParseTree(parseResult),
            };
            return await LibraryQueryCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                ct);
        });

        CliRowSelectionCommandRegistry.Register(
            command,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation
                    .ValidateLineSelectionForOutput(
                        opts.IsJsonDocumentOutput(result),
                        lowering));
        CliExecutionBoundCommandRegistry.Register(
            command,
            takeOption,
            _ => LibraryQuery.MaximumCandidates,
            isActive: static _ => true);

        return command;
    }
}
