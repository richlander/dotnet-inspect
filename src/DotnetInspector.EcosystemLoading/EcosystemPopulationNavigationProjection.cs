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
        EcosystemPopulationNavigationOutcome outcome)
    {
        Source = source;
        Outcome = outcome;
    }

    public EcosystemPopulationLibraryContributionWitness Source { get; }

    public EcosystemPopulationNavigationOutcome Outcome { get; }
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

    static EcosystemPopulationNavigationOutcome Evaluate(
        WorkspaceRegistrationRevision currentRevision,
        WorkspaceRegistrationRevision historicalRevision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationLibraryAdmissionCorrespondence correspondence)
    {
        if (!ReferenceEquals(
                currentRevision.Workspace,
                historicalRevision.Workspace))
        {
            return new EcosystemPopulationNavigationOutcome.Rejected(
                currentRevision,
                EcosystemPopulationNavigationRejection.ForeignWorkspace);
        }

        WorkspaceEcosystemContributionRelation? historicalRelation =
            FindExactContribution(
                historicalRevision,
                registration);
        if (historicalRelation is null
            || !currentRevision.EcosystemContributions.Contains(
                historicalRelation))
        {
            return new EcosystemPopulationNavigationOutcome.Unavailable(
                currentRevision,
                EcosystemPopulationNavigationUnavailableReason
                    .RegistrationNotCurrent);
        }

        return new EcosystemPopulationNavigationOutcome.Available(
            new EcosystemPopulationNavigationLibraryContribution(
                historicalRevision,
                currentRevision,
                historicalRelation,
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
