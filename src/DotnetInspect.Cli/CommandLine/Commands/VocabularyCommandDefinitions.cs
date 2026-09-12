using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class VocabularyCommandDefinitions
{
    public static Command CreateVocabularyCommand(SharedOptions opts)
    {
        var command = new Command(
            VocabularyCommand.Name,
            "Inspect product-owned values accepted by rich queries");
        opts.AddJsonOptionTo(command);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: result =>
                result.GetResult(opts.Discover) is { Implicit: false });
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.PlainText);
        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");

        command.SetAction((parseResult) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Vocabulary",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string[]? discover = opts.ParseDiscover(parseResult);
            OutputFormat format = opts.ResolveFormat(parseResult);
            return VocabularyCommand.Execute(new VocabularyOptions
            {
                Discover = discover,
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Tree = opts.ParseTree(parseResult),
                Count = parseResult.GetValue(opts.Count),
                RowSelection = rowSelection,
                Rows = discover is not null ? opts.ParseRows(parseResult) : null,
                Format = format,
                JsonOutput = format == OutputFormat.Json,
                PlainText = format == OutputFormat.PlainText,
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
            });
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
                linesOption,
                tailLinesOption),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window,
            result => opts.ParseDiscover(result) is null);

        return command;
    }
}
