using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

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
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: static _ => false);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);

        command.SetAction(parseResult =>
        {
            if (!TryGetRowSelection(
                    parseResult,
                    out RowSelectionIntent<string>? rowSelection))
                return 1;

            return EcosystemCommand.Execute(new EcosystemOptions
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
                RowSelection = rowSelection,
                Format = opts.ResolveFormat(parseResult),
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
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }

    private static bool TryGetRowSelection(
        ParseResult parseResult,
        out RowSelectionIntent<string>? rowSelection)
    {
        if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Ecosystem",
                out rowSelection,
                out string? error))
        {
            CommandError.Write(error!);
            return false;
        }

        return true;
    }
}
