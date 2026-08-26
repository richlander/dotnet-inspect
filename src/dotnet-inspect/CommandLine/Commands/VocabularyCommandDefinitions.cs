using System.CommandLine;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class VocabularyCommandDefinitions
{
    public static Command CreateVocabularyCommand(SharedOptions opts)
    {
        var command = new Command(
            VocabularyCommand.Name,
            "Inspect product-owned values accepted by rich queries");
        opts.AddJsonOptionTo(command);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.PlainText);

        command.SetAction((parseResult) =>
        {
            OutputFormat format = opts.ResolveFormat(parseResult);
            return VocabularyCommand.Execute(new VocabularyOptions
            {
                Discover = opts.ParseDiscover(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Tree = opts.ParseTree(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Format = format,
                JsonOutput = format == OutputFormat.Json,
                PlainText = format == OutputFormat.PlainText,
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
            });
        });

        return command;
    }
}
