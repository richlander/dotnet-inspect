using System.Collections.Immutable;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

/// <summary>An inert, owner-issued population registration.</summary>
public abstract record WorkspaceRegistration
{
    private protected WorkspaceRegistration() { }

    public sealed record ExactLibrary : WorkspaceRegistration
    {
        public ExactLibrary(ExactLibrarySourceCoordinate coordinate)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            Coordinate = coordinate;
        }

        public ExactLibrarySourceCoordinate Coordinate { get; }
    }

    public sealed record PackagePrefix : WorkspaceRegistration
    {
        public PackagePrefix(PackagePrefixDeclaration prefix)
        {
            ArgumentNullException.ThrowIfNull(prefix);
            Prefix = prefix;
        }

        public PackagePrefixDeclaration Prefix { get; }
    }

    public sealed record Ecosystem : WorkspaceRegistration
    {
        public Ecosystem(WorkspaceEcosystemRegistrationDeclaration declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            Declaration = declaration;
        }

        public WorkspaceEcosystemRegistrationDeclaration Declaration { get; }
    }
}

public sealed class WorkspaceRegistrationRevisionIdentity
{
    internal WorkspaceRegistrationRevisionIdentity() { }
}

/// <summary>A complete resource-free registration snapshot for one exact Workspace.</summary>
public sealed class WorkspaceRegistrationRevision
{
    internal WorkspaceRegistrationRevision(
        InspectionWorkspaceIdentity workspace,
        ImmutableArray<WorkspaceRegistration> registrations)
    {
        Workspace = workspace;
        Identity = new();
        Registrations = registrations;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceRegistrationRevisionIdentity Identity { get; }
    public ImmutableArray<WorkspaceRegistration> Registrations { get; }
}

public abstract record WorkspaceRegistrationReadResult
{
    private protected WorkspaceRegistrationReadResult() { }

    public sealed record Available(
        WorkspaceRegistrationRevision Revision) : WorkspaceRegistrationReadResult;

    public sealed record Unavailable(
        WorkspaceRegistrationRevision LastRevision,
        ArtifactRootFailure RuntimeFailure) : WorkspaceRegistrationReadResult;
}

public enum WorkspaceRegistrationRejection
{
    Malformed,
    ForeignWorkspace,
    RevisionMismatch,
    DuplicateIdentity,
}

public abstract record WorkspaceRegistrationOperationResult
{
    private protected WorkspaceRegistrationOperationResult() { }

    public sealed record Committed(
        WorkspaceRegistrationRevision Revision) : WorkspaceRegistrationOperationResult;

    public sealed record NoEffect(
        WorkspaceRegistrationRevision Revision) : WorkspaceRegistrationOperationResult;

    public sealed record Rejected(
        WorkspaceRegistrationRevision Revision,
        WorkspaceRegistrationRejection Reason) : WorkspaceRegistrationOperationResult;

    public sealed record Unavailable(
        WorkspaceRegistrationRevision LastRevision,
        ArtifactRootFailure RuntimeFailure) : WorkspaceRegistrationOperationResult;
}
