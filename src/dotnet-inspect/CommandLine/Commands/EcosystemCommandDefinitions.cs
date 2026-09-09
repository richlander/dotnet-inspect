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
            "Inspect product-configured knowledge about .NET ecosystems");
        var ecosystemArgument = new Argument<string?>("ecosystem")
        {
            Description =
                "Ecosystem short name or canonical ID (for example aspire or ecosystem.aspire)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        command.Arguments.Add(ecosystemArgument);
        opts.AddJsonOptionTo(command);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.PlainText);
        // The lowering bindings require identities for unsupported line modes;
        // leaving these options unattached keeps them outside the public command.
        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");

        command.SetAction(parseResult =>
            EcosystemCommand.Execute(new EcosystemOptions
            {
                Ecosystem = parseResult.GetValue(ecosystemArgument),
                Discover = opts.ParseDiscover(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Tree = opts.ParseTree(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Format = opts.ResolveFormat(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
            }));

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
            isActive: static _ => true);

        return command;
    }
}
