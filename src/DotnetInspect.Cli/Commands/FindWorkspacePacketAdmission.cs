using DotnetInspect.Cli.Options;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal static class FindWorkspacePacketAdmission
{
    internal static bool TryAdmit(
        FindOptions options,
        CancellationToken cancellationToken,
        out FindOptions admitted,
        out string? error)
    {
        admitted = options;
        error = null;
        if (options.WorkspacePacket is null)
            return true;
        if (options.Ecosystems is not null || options.Packages.Length != 0
            || options.Assemblies.Length != 0 || options.PlatformAssemblies.Length != 0
            || options.PlatformFrameworks.Length != 0 || options.Projects.Length != 0
            || options.BinPaths.Length != 0 || options.PackagePrefixSpecified
            || options.PackagePrefix is not null || options.Tfm is not null
            || options.SourceSelection is { UsesImplicitPlatform: false })
        {
            error = "--workspace cannot be combined with another Find source selector or --tfm.";
            return false;
        }
        try
        {
            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
                WorkspacePacketRestoration.GetPacketInput(options.WorkspacePacket, "--workspace"),
                cancellationToken);
            _ = WorkspaceSharePacketTransposer.ToCommittedDefinitions(packet, cancellationToken);
            if (packet.Tabs.Count != 0 || packet.Contexts.Count != 0
                || packet.PackageSources.Count != 0 || packet.Registrations.Count == 0
                || packet.Registrations.Any(registration => registration is not WorkspaceRegistration.Ecosystem ecosystem
                    || ecosystem.Declaration.Populations.Any(
                        population => population is WorkspaceEcosystemPopulationDeclaration.ExactLibrary)))
            {
                error = "Find --workspace currently requires a registration-only Ecosystem Workspace; "
                    + "direct content, exact Libraries, independent prefixes, package sources, and empty Workspaces are not supported.";
                return false;
            }
            WorkspaceRegistration.Ecosystem[] ecosystems =
                [.. packet.Registrations.Cast<WorkspaceRegistration.Ecosystem>()];
            var ids = new List<EcosystemPackId>();
            foreach (WorkspaceRegistration.Ecosystem ecosystem in ecosystems)
            {
                if (!EcosystemPackId.TryCreate(ecosystem.Declaration.Id.Value, out EcosystemPackId? id))
                {
                    error = "Find --workspace requires canonical ecosystem identities.";
                    return false;
                }
                bool platform = ecosystem.Declaration.Populations.Any(
                    population => population is WorkspaceEcosystemPopulationDeclaration.Platform);
                if (platform && !ecosystem.Declaration.CorePackages.IsEmpty)
                {
                    error = $"Find cannot search mixed Platform and core-package populations in Ecosystem '{id}'.";
                    return false;
                }
                ids.Add(id);
            }
            admitted = options with
            {
                Ecosystems = [.. ids],
                WorkspaceEcosystems = ecosystems,
            };
            return true;
        }
        catch (Exception exception) when (exception is WorkspaceSharePacketException
            or InspectionDefinitionException or ArgumentException or InvalidDataException)
        {
            error = $"The Find Workspace packet is invalid: {exception.Message}";
            return false;
        }
    }
}
