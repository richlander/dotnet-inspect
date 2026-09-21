using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

/// <summary>
/// One exact Focus contribution witness and its current Navigation
/// classification.
/// </summary>
public sealed class EcosystemPopulationNavigationContribution
{
    internal EcosystemPopulationNavigationContribution(
        EcosystemPopulationLibraryContributionWitness source,
        NavigationEcosystemContributionOutcome outcome)
    {
        Source = source;
        Outcome = outcome;
    }

    public EcosystemPopulationLibraryContributionWitness Source { get; }

    public NavigationEcosystemContributionOutcome Outcome { get; }
}

/// <summary>
/// Projects accepted Focus witnesses into current Navigation contribution
/// evidence without inferring Package ancestry.
/// </summary>
public static class EcosystemPopulationNavigationProjection
{
    public static IReadOnlyList<EcosystemPopulationNavigationContribution>
        Project(
            WorkspaceRegistrationRevision currentRevision,
            EcosystemPopulationAdmissionResult admission)
    {
        ArgumentNullException.ThrowIfNull(currentRevision);
        ArgumentNullException.ThrowIfNull(admission);

        return Array.AsReadOnly(
            admission.Contributions
                .Select(
                    source =>
                    {
                        EcosystemPopulationLibraryAdmissionCorrespondence
                            correspondence = source.Correspondence;
                        WorkspaceRegistrationRevision historicalRevision =
                            correspondence.LoadReceipt.Request
                                .RegistrationRevision;
                        WorkspaceEcosystemRegistrationDeclaration
                            registration = correspondence.LoadReceipt.Request
                                .Registration;
                        return new EcosystemPopulationNavigationContribution(
                            source,
                            Evaluate(
                                currentRevision,
                                historicalRevision,
                                registration,
                                correspondence));
                    })
                .ToArray());
    }

    static NavigationEcosystemContributionOutcome Evaluate(
        WorkspaceRegistrationRevision currentRevision,
        WorkspaceRegistrationRevision historicalRevision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationLibraryAdmissionCorrespondence correspondence)
    {
        if (!ReferenceEquals(
                currentRevision.Workspace,
                historicalRevision.Workspace))
        {
            return new NavigationEcosystemContributionOutcome.Rejected(
                currentRevision,
                NavigationEcosystemContributionRejection.ForeignWorkspace);
        }

        WorkspaceEcosystemContributionRelation? ecosystemRelation =
            FindExactContribution(
                currentRevision,
                registration);
        if (ecosystemRelation is null)
        {
            return new NavigationEcosystemContributionOutcome.Unavailable(
                currentRevision,
                NavigationEcosystemContributionUnavailableReason
                    .RegistrationNotCurrent);
        }

        return new NavigationEcosystemContributionOutcome.Available(
            new NavigationEcosystemLibraryContribution(
                historicalRevision,
                currentRevision,
                ecosystemRelation,
                correspondence.Admission,
                correspondence.Occurrence));
    }

    static WorkspaceEcosystemContributionRelation? FindExactContribution(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration)
    {
        foreach (WorkspaceEcosystemContributionRelation contribution
            in revision.EcosystemContributions)
        {
            if (ReferenceEquals(
                    contribution.Ecosystem.Declaration,
                    registration))
            {
                return contribution;
            }
        }

        return null;
    }
}
