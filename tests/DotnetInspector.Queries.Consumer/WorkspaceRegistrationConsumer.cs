using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.QueriesConsumer;

public sealed record WorkspaceRegistrationObservation(
    WorkspaceRegistrationRevision Revision,
    ImmutableArray<ExactLibrarySourceCoordinate> ExactLibraries,
    ImmutableArray<PackagePrefixDeclaration> PackagePrefixes,
    ImmutableArray<WorkspaceEcosystemRegistrationDeclaration> Ecosystems);

public static class WorkspaceRegistrationConsumer
{
    public static InspectionWorkspace Create(
        ImmutableArray<WorkspaceRegistration> registrations,
        bool asynchronous) =>
        asynchronous
            ? InspectionWorkspace.CreateAsynchronous(registrations)
            : new InspectionWorkspace(registrations);

    public static WorkspaceRegistrationObservation Observe(InspectionWorkspace workspace)
    {
        if (workspace.GetRegistrationSnapshot() is not WorkspaceRegistrationReadResult.Available available)
            throw new InvalidOperationException("The Workspace registration revision is unavailable.");

        var libraries = ImmutableArray.CreateBuilder<ExactLibrarySourceCoordinate>();
        var prefixes = ImmutableArray.CreateBuilder<PackagePrefixDeclaration>();
        var ecosystems = ImmutableArray.CreateBuilder<WorkspaceEcosystemRegistrationDeclaration>();
        foreach (WorkspaceRegistration registration in available.Revision.Registrations)
        {
            switch (registration)
            {
                case WorkspaceRegistration.ExactLibrary library:
                    libraries.Add(library.Coordinate);
                    break;
                case WorkspaceRegistration.PackagePrefix prefix:
                    prefixes.Add(prefix.Prefix);
                    break;
                case WorkspaceRegistration.Ecosystem ecosystem:
                    ecosystems.Add(ecosystem.Declaration);
                    break;
                default:
                    throw new InvalidOperationException("Unknown Workspace registration arm.");
            }
        }
        return new(available.Revision, libraries.ToImmutable(), prefixes.ToImmutable(), ecosystems.ToImmutable());
    }

    public static WorkspaceRegistrationOperationResult Replace(
        InspectionWorkspace workspace,
        WorkspaceRegistrationRevision expectedRevision,
        ImmutableArray<WorkspaceRegistration> registrations) =>
        workspace.ReplaceRegistrations(expectedRevision, registrations);
}
