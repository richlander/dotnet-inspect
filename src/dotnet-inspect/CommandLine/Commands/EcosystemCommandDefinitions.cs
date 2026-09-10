using System.CommandLine;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

public static class EcosystemCommandDefinitions
{
    public static Command CreateEcosystemCommand(SharedOptions opts)
    {
        var command = new Command(
            EcosystemCommand.Name,
            "Inspect the ecosystems this build knows about");

        // One operand, and no sub-verb. It names a registry row rather than routing to a
        // subject: drill-in belongs to library, type, and member.
        var ecosystemArgument = new Argument<string?>("ecosystem")
        {
            Description = "One registered ecosystem to narrow to (id or title)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        command.Arguments.Add(ecosystemArgument);

        var frameworkOption = new Option<string?>("--framework")
        {
            Description =
                "Platform framework whose prune inventory to read (runtime, aspnetcore, netstandard). @version for specific",
        };
        command.Options.Add(frameworkOption);

        opts.AddJsonOptionTo(command);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.PlainText);

        command.SetAction((parseResult) =>
        {
            OutputFormat format = opts.ResolveFormat(parseResult);
            return EcosystemCommand.Execute(new EcosystemOptions
            {
                Ecosystem = parseResult.GetValue(ecosystemArgument),
                Framework = parseResult.GetValue(frameworkOption),
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
