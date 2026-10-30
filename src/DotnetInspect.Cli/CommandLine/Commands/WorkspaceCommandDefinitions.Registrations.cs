using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

public static partial class WorkspaceCommandDefinitions
{
    static Command CreateWorkspaceRegistrationCommand(bool ecosystem)
    {
        var command = new Command(ecosystem ? "ecosystem" : "prefix",
            ecosystem ? "Compose shipped Ecosystem registrations in a Workspace"
                : "Compose independent literal Package prefix registrations in a Workspace");
        command.SetAction(_ => { HelpWriter.WriteHelp(command); return 0; });
        command.Subcommands.Add(CreateRegistrationEditCommand(ecosystem, add: true));
        command.Subcommands.Add(CreateRegistrationEditCommand(ecosystem, add: false));
        return command;
    }

    static Command CreateRegistrationEditCommand(bool ecosystem, bool add)
    {
        var command = new Command(add ? "add" : "remove",
            add ? "Append a registration; omit --packet to create a registration-only Workspace"
                : "Remove one registration and emit a new Workspace packet");
        var value = new Argument<string>(ecosystem ? "ecosystem" : "prefix")
        {
            Description = ecosystem ? "Shipped Ecosystem name or canonical ecosystem.NAME identity"
                : "Literal Package ID prefix, such as Microsoft.Extensions.",
        };
        var packet = PacketOption();
        Option<string?> share = WorkspaceShareOption.Create(
            "Emit the derived Workspace as a canonical packet (default) or URL");
        command.Arguments.Add(value);
        command.Options.Add(packet);
        command.Options.Add(share);
        command.SetAction((result, cancellationToken) =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = result.GetValue(value)!;
                string? input = result.GetValue(packet);
                if (input is null && !add)
                    throw new ArgumentException("Removing a Workspace registration requires --packet.");
                WorkspaceSharePacket? source = input is null ? null
                    : WorkspaceSharePacketCodec.Decode(
                        WorkspacePacketRestoration.GetPacketInput(input, "--packet"), cancellationToken);
                WorkspaceSharePacketProjectionResult edited = ecosystem
                    ? add
                        ? WorkspaceRegistrationEditor.AddEcosystem(source,
                            WorkspaceCommand.ParseEcosystemRegistration(text).Declaration)
                        : WorkspaceRegistrationEditor.RemoveEcosystem(source!,
                            WorkspaceEcosystemRegistrationId.Create(WorkspaceCommand.ParseEcosystemPackId(text).Value))
                    : add
                        ? WorkspaceRegistrationEditor.AddPrefix(source, new PackagePrefixDeclaration(text))
                        : WorkspaceRegistrationEditor.RemovePrefix(source!, new PackagePrefixDeclaration(text));
                if (edited.Failure is { } failure)
                {
                    CommandError.Write($"Workspace registration edit failed ({failure.Kind}).", [failure.Message]);
                    return Task.FromResult(1);
                }
                return Task.FromResult(WorkspaceShareOutput.Write(edited.Packet!,
                    WorkspaceShareOption.Parse(result, share) ?? WorkspaceShareFormat.Packet));
            }
            catch (Exception error) when (error is ArgumentException
                or WorkspaceSharePacketException or InvalidDataException)
            {
                CommandError.Write("The Workspace registration edit is invalid.", [error.Message]);
                return Task.FromResult(1);
            }
        });
        return command;
    }
}
