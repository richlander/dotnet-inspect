using System.Collections.Immutable;
using DotnetInspector.Platforms;

namespace DotnetInspector.Queries;

/// <summary>
/// The effective focal scope of one member call-graph operation.
/// </summary>
public enum MemberCallGraphFocalLength
{
    Everything,
}

/// <summary>
/// One exact registered Platform population included in a call-graph scope.
/// </summary>
public sealed class MemberCallGraphPlatformPopulationScope
{
    internal MemberCallGraphPlatformPopulationScope(
        WorkspaceEcosystemContributionRelation ecosystem,
        WorkspaceEcosystemPopulationDeclaration.Platform population)
    {
        Ecosystem = ecosystem;
        Population = population;
    }

    public WorkspaceEcosystemContributionRelation Ecosystem { get; }

    public WorkspaceEcosystemPopulationDeclaration.Platform Population
    {
        get;
    }

    public PlatformFamily Family => Population.Population.Family;
}

/// <summary>
/// Resource-free evidence for the effective population scope of one member
/// call-graph operation.
/// </summary>
public sealed class MemberCallGraphFocalScopeReceipt
{
    private MemberCallGraphFocalScopeReceipt(
        WorkspaceScopeRevisionIdentity scopeRevision,
        WorkspaceRegistrationRevisionIdentity registrationRevision,
        MemberCallGraphFocalLength focalLength,
        ImmutableArray<WorkspaceEcosystemContributionRelation> ecosystems,
        ImmutableArray<MemberCallGraphPlatformPopulationScope>
            platformPopulations)
    {
        ScopeRevision = scopeRevision;
        RegistrationRevision = registrationRevision;
        FocalLength = focalLength;
        Ecosystems = ecosystems;
        PlatformPopulations = platformPopulations;
    }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public WorkspaceRegistrationRevisionIdentity RegistrationRevision
    {
        get;
    }

    public MemberCallGraphFocalLength FocalLength { get; }

    public ImmutableArray<WorkspaceEcosystemContributionRelation> Ecosystems
    {
        get;
    }

    public ImmutableArray<MemberCallGraphPlatformPopulationScope>
        PlatformPopulations
    {
        get;
    }

    /// <summary>
    /// Captures the currently implemented member call-graph scope:
    /// every population available through the exact Workspace revisions.
    /// </summary>
    public static MemberCallGraphFocalScopeReceipt CaptureEverything(
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registrations);
        if (!ReferenceEquals(
                scope.Revision.Workspace,
                registrations.Workspace))
        {
            throw new ArgumentException(
                "Call-graph Scope and registration revisions must belong to the same Workspace.");
        }

        ImmutableArray<WorkspaceEcosystemContributionRelation> ecosystems =
            registrations.EcosystemContributions;
        var platformPopulations =
            ImmutableArray.CreateBuilder<
                MemberCallGraphPlatformPopulationScope>();
        foreach (WorkspaceEcosystemContributionRelation ecosystem
            in ecosystems)
        {
            foreach (WorkspaceEcosystemPopulationDeclaration population
                in ecosystem.Ecosystem.Declaration.Populations)
            {
                if (population
                    is WorkspaceEcosystemPopulationDeclaration.Platform
                        platform)
                {
                    platformPopulations.Add(
                        new MemberCallGraphPlatformPopulationScope(
                            ecosystem,
                            platform));
                }
            }
        }

        return new(
            scope.Revision.Identity,
            registrations.Identity,
            MemberCallGraphFocalLength.Everything,
            ecosystems,
            platformPopulations.ToImmutable());
    }
}
