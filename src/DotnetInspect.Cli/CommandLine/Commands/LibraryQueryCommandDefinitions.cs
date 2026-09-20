using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryQueryCommandDefinitions
{
    internal static Command Create(
        SharedOptions opts,
        Command parentCommand,
        Argument<string?> parentSourceArgument)
    {
        var command = new Command(
            "query",
            "Query an explicit population of .NET Libraries");
        var sourcesArgument = new Argument<string[]>("sources")
        {
            Description =
                "Library files or directories whose top-level .dll files form the population",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var takeOption = new Option<int?>("--take")
        {
            Description =
                "Maximum Library candidates to evaluate "
                + $"(default and maximum {LibraryQuery.DefaultMaximumCandidates})",
        };
        takeOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is int value
                && (value <= 0
                    || value > LibraryQuery.DefaultMaximumCandidates))
            {
                result.AddError(
                    $"--take must be between 1 and "
                    + $"{LibraryQuery.DefaultMaximumCandidates}.");
            }
        });

        command.Arguments.Add(sourcesArgument);
        command.Options.Add(takeOption);
        command.Options.Add(opts.RowWhere);
        command.Options.Add(opts.Json);
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
        command.Options.Add(opts.Tree);
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
            opts.QueryHelp);

        command.SetAction((parseResult, ct) =>
        {
            if (parseResult.GetResult(parentSourceArgument)
                    is { Tokens.Count: > 0 })
            {
                CommandError.Write(
                    "A Library inspection source cannot precede library query; "
                    + "place 'query' immediately after 'library'.");
                return Task.FromResult(1);
            }

            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Envelope,
                opts.Json,
                opts.Markdown,
                opts.Table,
                opts.Tsv,
                opts.Jsonl,
                opts.NoHeaders,
                opts.RowWhere,
                opts.Limit,
                opts.Count,
                opts.Rows,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines,
                opts.Fields,
                opts.Columns,
                opts.Discover,
                opts.QueryHelp,
                opts.Tree,
            };
            Option? unsupportedParentOption =
                parentCommand.Options.FirstOrDefault(
                    option => !acceptedParentOptions.Contains(option)
                        && parseResult.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                CommandError.Write(
                    $"{unsupportedParentOption.Name} is not available with "
                    + "library query.");
                return Task.FromResult(1);
            }

            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Library Query",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return Task.FromResult(1);
            }

            string[]? discover = opts.ParseDiscover(parseResult);
            OutputFormat format = parseResult.GetValue(opts.Envelope)
                ? OutputFormat.Json
                : opts.ResolveFormat(parseResult);
            string[] sources =
                parseResult.GetValue(sourcesArgument) ?? [];
            if (discover is null && sources.Length == 0)
            {
                CommandError.Write(
                    "Library Query requires at least one Library file or directory.");
                return Task.FromResult(1);
            }

            if (!LibraryQueryOptions.TryCreate(
                    sources,
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    parseResult.GetValue(takeOption)
                        ?? LibraryQuery.DefaultMaximumCandidates,
                    out LibraryQueryOptions? options,
                    out OptionError error))
            {
                CommandError.Write(error);
                return Task.FromResult(1);
            }

            string? invalidSource = discover is null
                ? sources.FirstOrDefault(source =>
                    !Directory.Exists(source)
                    && !source.EndsWith(
                        ".dll",
                        StringComparison.OrdinalIgnoreCase)
                    && !source.EndsWith(
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (invalidSource is not null)
            {
                CommandError.Write(
                    $"Library Query source '{invalidSource}' must be a "
                    + ".dll or .exe file or a directory.");
                return Task.FromResult(1);
            }

            options = options! with
            {
                RowSelection = rowSelection,
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = format == OutputFormat.Json,
                EnvelopeOutput = parseResult.GetValue(opts.Envelope),
                Tabular =
                    !parseResult.GetValue(opts.Envelope)
                    && opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Discover = discover,
                Tree = opts.ParseTree(parseResult),
            };
            return Task.FromResult(
                LibraryQueryCommand.Execute(options, ct));
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
            _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }
}
