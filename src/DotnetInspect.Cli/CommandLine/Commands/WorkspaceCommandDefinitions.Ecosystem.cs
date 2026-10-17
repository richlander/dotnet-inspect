using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

public static partial class WorkspaceCommandDefinitions
{
    static Command CreateWorkspaceEcosystemCommand()
    {
        var command = new Command("ecosystem",
            "Create, replace, or remove portable Ecosystem declarations without acquisition");
        command.SetAction(_ => { HelpWriter.WriteHelp(command); return 0; });
        command.Subcommands.Add(CreateEcosystemEditCommand("add"));
        command.Subcommands.Add(CreateEcosystemEditCommand("update"));
        command.Subcommands.Add(CreateEcosystemEditCommand("remove"));
        return command;
    }

    static Command CreateEcosystemEditCommand(string operation)
    {
        var command = new Command(operation, operation switch
        {
            "add" => "Append an Ecosystem; omit --packet to create an ecosystem-only Workspace",
            "update" => "Replace one Ecosystem declaration in place, retaining its identity",
            _ => "Remove one Ecosystem declaration",
        });
        var id = new Argument<string>("id")
        {
            Description = "Canonical ecosystem.NAME identity",
        };
        var packet = PacketOption();
        var from = new Option<string?>("--from")
        {
            Description = "Use a shipped Ecosystem declaration as the starting value",
        };
        var roots = EcosystemValuesOption("--namespace-root", "Namespace root");
        var packages = EcosystemValuesOption("--core-package", "Unversioned core Package ID");
        var platform = EcosystemValuesOption("--platform", "Platform population family");
        platform.AcceptOnlyFromAmong(Enum.GetNames<PlatformFamily>());
        var prefix = EcosystemValuesOption("--package-prefix", "Literal package-prefix population");
        var library = EcosystemValuesOption("--library",
            "Exact Library population: package@version/assembly@assembly-version");
        Option<string?> share = WorkspaceShareOption.Create(
            "Emit the derived Workspace as a canonical packet (default) or URL");
        command.Arguments.Add(id);
        command.Options.Add(packet);
        command.Options.Add(share);
        if (operation != "remove")
        {
            command.Options.Add(from);
            command.Options.Add(roots);
            command.Options.Add(packages);
            command.Options.Add(platform);
            command.Options.Add(prefix);
            command.Options.Add(library);
        }

        command.SetAction((result, cancellationToken) =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WorkspaceEcosystemRegistrationId identity =
                    WorkspaceEcosystemRegistrationId.Create(result.GetValue(id)!);
                string? input = result.GetValue(packet);
                if (input is null && operation != "add")
                    throw new ArgumentException($"'workspace ecosystem {operation}' requires --packet.");
                WorkspaceSharePacket? source = input is null ? null
                    : WorkspaceSharePacketCodec.Decode(
                        WorkspacePacketRestoration.GetPacketInput(input, "--packet"),
                        cancellationToken);

                WorkspaceSharePacketProjectionResult edited;
                if (operation == "remove")
                {
                    edited = WorkspaceEcosystemEditor.Remove(source!, identity);
                }
                else
                {
                    WorkspaceEcosystemRegistrationDeclaration? basis = result.GetValue(from) is { } name
                        ? WorkspaceCommand.ParseEcosystemRegistration(name).Declaration : null;
                    string[] namespaces = result.GetValue(roots) ?? [];
                    string[] corePackages = result.GetValue(packages) ?? [];
                    WorkspaceEcosystemPopulationDeclaration[] populations =
                        ParseEcosystemPopulations(result, platform, prefix, library);
                    var declaration = new WorkspaceEcosystemRegistrationDeclaration(
                        identity,
                        result.GetResult(roots) is null && basis is not null
                            ? basis.NamespaceRoots : namespaces,
                        result.GetResult(packages) is null && basis is not null
                            ? basis.CorePackages : corePackages.Select(value => new PackageCoordinate(value)),
                        populations.Length == 0 && basis is not null ? basis.Populations : populations,
                        basis?.IntegrationScanner);
                    edited = operation == "add"
                        ? WorkspaceEcosystemEditor.Add(source, declaration)
                        : WorkspaceEcosystemEditor.Update(source!, declaration);
                }

                if (edited.Failure is { } failure)
                {
                    CommandError.Write($"Workspace Ecosystem edit failed ({failure.Kind}).", [failure.Message]);
                    return Task.FromResult(1);
                }
                return Task.FromResult(WorkspaceShareOutput.Write(
                    edited.Packet!, WorkspaceShareOption.Parse(result, share) ?? WorkspaceShareFormat.Packet));
            }
            catch (Exception error) when (error is ArgumentException
                or WorkspaceSharePacketException or InvalidDataException)
            {
                CommandError.Write("The Workspace Ecosystem edit is invalid.", [error.Message]);
                return Task.FromResult(1);
            }
        });
        return command;
    }

    static Option<string[]> EcosystemValuesOption(string name, string description) => new(name)
    {
        Description = description + "; repeat to provide ordered values",
        AllowMultipleArgumentsPerToken = false,
    };

    static WorkspaceEcosystemPopulationDeclaration[] ParseEcosystemPopulations(
        ParseResult result,
        Option<string[]> platform,
        Option<string[]> prefix,
        Option<string[]> library)
    {
        var populations = new List<WorkspaceEcosystemPopulationDeclaration>();
        for (int index = 0; index + 1 < result.Tokens.Count; index++)
        {
            Token token = result.Tokens[index];
            if (token.Type != TokenType.Option)
                continue;
            string value = result.Tokens[index + 1].Value;
            if (token.Value == platform.Name)
                populations.Add(new WorkspaceEcosystemPopulationDeclaration.Platform(
                    new PlatformLibraryPopulationDeclaration(Enum.Parse<PlatformFamily>(value))));
            else if (token.Value == prefix.Name)
                populations.Add(new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration(value)));
            else if (token.Value == library.Name)
                populations.Add(new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    WorkspaceCommand.ParseExactPackageLibrary(value)));
        }
        return [.. populations];
    }
}
