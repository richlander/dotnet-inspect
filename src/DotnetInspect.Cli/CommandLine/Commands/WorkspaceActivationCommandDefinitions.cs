using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

internal static class WorkspaceActivationCommandDefinitions
{
    internal const string CommandName = "activate-workspace-packet";

    internal static Option<string?> CreatePacketOption() => new("-w")
    {
        Description = "Apply one canonical Workspace packet for this invocation",
        Arity = ArgumentArity.ExactlyOne,
    };

    internal static Command Create(SharedOptions opts)
    {
        var command = new Command(CommandName, "Apply a Workspace packet for this invocation")
        {
            Hidden = true,
        };
        var packet = new Argument<string?>("packet") { Arity = ArgumentArity.ZeroOrOne };
        command.Arguments.Add(packet);
        Option<string?> packetOption = CreatePacketOption();
        command.Options.Add(packetOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddCountOptionTo(command);
        // Program owns these invocation-wide policies, before command dispatch.
        command.Options.Add(new Option<bool>("--offline"));
        command.Options.Add(new Option<int?>("--http-timeout"));
        command.Validators.Add(result =>
        {
            string? explicitPacket = result.GetValue(packetOption);
            string? rawPacket = result.GetValue(packet);
            if ((explicitPacket is null) == (rawPacket is null))
                result.AddError("Packet activation requires exactly one packet, via -w PACKET or raw packet input.");
        });
        bool RenderedLines(System.CommandLine.ParseResult result) =>
            WorkspacePacketActivation.UsesRenderedLineOutput(
                result.GetValue(packetOption) ?? result.GetValue(packet));

        command.SetAction(async (result, cancellationToken) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    result, "Packet result", out RowSelectionIntent<string>? rows,
                    out string? error))
            {
                CommandError.Write(error!);
                return 1;
            }
            return await WorkspacePacketActivation.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packet = result.GetValue(packetOption) ?? result.GetValue(packet),
                    Inventory = true,
                    Format = opts.ResolveFormat(result),
                    Count = result.GetValue(opts.Count),
                    RowSelection = RenderedLines(result) ? null : rows,
                    Rows = RenderedLines(result) ? opts.ParseRows(result) : null,
                    NoHeader = result.GetValue(opts.NoHeaders),
                    Verbose = result.GetValue(opts.Verbose),
                }, cancellationToken);
        });
        CliRowSelectionCommandRegistry.Register(command,
            new(opts.Limit, opts.Rows, top: null, orderBy: null,
                opts.Head, opts.Tail, opts.Lines, opts.TailLines),
            CliRowSelectionCapabilities.HeadTail | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: result => !RenderedLines(result),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result), lowering));
        CliRowSelectionCommandRegistry.Register(command,
            new(opts.Limit, opts.Rows, top: null, orderBy: null,
                opts.Head, opts.Tail, opts.Lines, opts.TailLines),
            CliRowSelectionCapabilities.HeadTail | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: RenderedLines,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result), lowering),
            defaultUnit: CliRowSelectionDefaultUnit.RenderedLines);
        return command;
    }
}
