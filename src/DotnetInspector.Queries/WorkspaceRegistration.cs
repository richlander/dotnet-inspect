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

/// <summary>
/// Opaque process-local identity for one exact Ecosystem registration retained
/// by a Workspace.
/// </summary>
public sealed class WorkspaceEcosystemRegistrationOccurrenceIdentity :
    InspectionWorkspaceOccurrenceIdentity
{
    internal WorkspaceEcosystemRegistrationOccurrenceIdentity(
        InspectionWorkspaceIdentity workspace)
        : base(workspace)
    {
    }
}

/// <summary>
/// One exact Ecosystem registration occurrence in one Workspace.
/// </summary>
public sealed class WorkspaceEcosystemRegistrationOccurrence
{
    internal WorkspaceEcosystemRegistrationOccurrence(
        WorkspaceEcosystemRegistrationOccurrenceIdentity identity,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        Identity = identity;
        Declaration = declaration;
    }

    public WorkspaceEcosystemRegistrationOccurrenceIdentity Identity { get; }

    public InspectionWorkspaceIdentity Workspace =>
        Identity.WorkspaceIdentity;

    public WorkspaceEcosystemRegistrationDeclaration Declaration { get; }
}

/// <summary>
/// Opaque process-local identity for one Workspace-to-Ecosystem contribution
/// relation.
/// </summary>
public sealed class WorkspaceEcosystemContributionRelationIdentity
{
    internal WorkspaceEcosystemContributionRelationIdentity(
        InspectionWorkspaceIdentity workspace)
    {
        Workspace = workspace;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
}

/// <summary>
/// Owner-issued evidence that one Workspace currently contributes one exact
/// Ecosystem registration occurrence.
/// </summary>
public sealed class WorkspaceEcosystemContributionRelation
{
    internal WorkspaceEcosystemContributionRelation(
        WorkspaceEcosystemContributionRelationIdentity identity,
        WorkspaceEcosystemRegistrationOccurrence ecosystem)
    {
        Identity = identity;
        Ecosystem = ecosystem;
    }

    public WorkspaceEcosystemContributionRelationIdentity Identity { get; }

    public InspectionWorkspaceIdentity Workspace => Identity.Workspace;

    public WorkspaceEcosystemRegistrationOccurrence Ecosystem { get; }
}

/// <summary>A complete resource-free registration snapshot for one exact Workspace.</summary>
public sealed class WorkspaceRegistrationRevision
{
    internal WorkspaceRegistrationRevision(
        InspectionWorkspaceIdentity workspace,
        WorkspacePlan plan,
        WorkspaceRegistrationRevision? previous = null)
    {
        Workspace = workspace;
        Identity = new();
        Plan = plan;
        EcosystemContributions = CreateEcosystemContributions(
            workspace,
            plan.Registrations,
            previous);
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceRegistrationRevisionIdentity Identity { get; }
    public WorkspacePlan Plan { get; }
    public ImmutableArray<WorkspaceRegistration> Registrations => Plan.Registrations;

    public ImmutableArray<WorkspaceEcosystemContributionRelation>
        EcosystemContributions { get; }

    static ImmutableArray<WorkspaceEcosystemContributionRelation>
        CreateEcosystemContributions(
            InspectionWorkspaceIdentity workspace,
            ImmutableArray<WorkspaceRegistration> registrations,
            WorkspaceRegistrationRevision? previous)
    {
        var contributions =
            ImmutableArray.CreateBuilder<
                WorkspaceEcosystemContributionRelation>();
        foreach (WorkspaceRegistration registration in registrations)
        {
            if (registration is not WorkspaceRegistration.Ecosystem ecosystem)
                continue;

            WorkspaceEcosystemContributionRelation? retained =
                FindContribution(
                    previous?.EcosystemContributions ?? [],
                    ecosystem.Declaration);
            contributions.Add(
                retained
                ?? CreateContribution(workspace, ecosystem.Declaration));
        }

        return contributions.ToImmutable();
    }

    static WorkspaceEcosystemContributionRelation? FindContribution(
        ImmutableArray<WorkspaceEcosystemContributionRelation> contributions,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        foreach (WorkspaceEcosystemContributionRelation contribution
            in contributions)
        {
            if (ReferenceEquals(
                    contribution.Ecosystem.Declaration,
                    declaration))
            {
                return contribution;
            }
        }

        return null;
    }

    static WorkspaceEcosystemContributionRelation CreateContribution(
        InspectionWorkspaceIdentity workspace,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        var occurrence = new WorkspaceEcosystemRegistrationOccurrence(
            new WorkspaceEcosystemRegistrationOccurrenceIdentity(workspace),
            declaration);
        return new WorkspaceEcosystemContributionRelation(
            new WorkspaceEcosystemContributionRelationIdentity(workspace),
            occurrence);
    }
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
