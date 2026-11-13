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
    internal static Func<System.CommandLine.ParseResult, CancellationToken, Task<int>> Configure(
        SharedOptions opts,
        Command command,
        Argument<string?> sourceArgument,
        Option<string?> platformOption,
        Option<bool> compactOption,
        Option<bool> librariesOption)
    {
        command.Validators.Add(result =>
        {
            if (!result.GetValue(librariesOption))
                return;
            if (result.GetResult(compactOption) is { Implicit: false }
                && !result.GetValue(opts.Json)
                && !result.GetValue(opts.Envelope))
            {
                result.AddError(
                    "--compact requires find --libraries --json or --envelope.");
            }
            if (result.GetValue(opts.Json)
                && result.GetValue(opts.Tree)
                && result.GetResult(opts.Discover)
                    is not { Implicit: false })
            {
                result.AddError(
                    "--tree with find --libraries --json requires schema discovery.");
            }
        });

        return async (parseResult, ct) =>
        {
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
            int? take =
                CliExecutionBoundCommandRegistry.GetPreparedValue(
                    parseResult);
            if (!LibraryQueryOptions.TryCreate(
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    take,
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
                ExplicitTake = take is not null,
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
        };
    }
}
