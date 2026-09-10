using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.RowSelection;
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
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        // The lowering bindings require identities for unsupported line modes;
        // leaving these options unattached keeps them outside the public command.
        var linesOption = new Option<bool>("--lines");
        var tailLinesOption = new Option<bool>("--tail-lines");

        command.SetAction(parseResult =>
        {
            if (!TryGetRows(parseResult, out RowWindow? rows))
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
                Rows = rows,
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
                linesOption,
                tailLinesOption),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window,
            isActive: static _ => true);

        return command;
    }

    private static bool TryGetRows(
        ParseResult parseResult,
        out RowWindow? rows)
    {
        if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Ecosystem",
                out RowSelectionIntent<string>? intent,
                out string? error))
        {
            CommandError.Write(error!);
            rows = null;
            return false;
        }

        if (intent is null || intent.Operations.Count == 0)
        {
            rows = null;
            return true;
        }

        if (intent.Operations.Count != 1)
        {
            throw new InvalidOperationException(
                "Ecosystem row selection must lower to exactly one operation.");
        }

        RowSelectionIntentOperation<string> operation =
            intent.Operations[0];
        rows = operation.Kind switch
        {
            RowSelectionStageKind.Head => RowWindow.Head(operation.Count),
            RowSelectionStageKind.Tail => RowWindow.Tail(operation.Count),
            RowSelectionStageKind.Window when operation.Start is int start =>
                RowWindow.Range(start, operation.End),
            _ => throw new InvalidOperationException(
                $"Unsupported ecosystem row-selection operation '{operation.Kind}'."),
        };
        return true;
    }
}
