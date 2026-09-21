namespace DotnetInspector.Queries;

/// <summary>
/// Why historical Ecosystem admission evidence cannot produce a current
/// Navigation contribution.
/// </summary>
public enum NavigationEcosystemContributionUnavailableReason
{
    RegistrationNotCurrent,
}

/// <summary>
/// Why a current revision cannot consume historical Ecosystem contribution
/// evidence.
/// </summary>
public enum NavigationEcosystemContributionRejection
{
    ForeignWorkspace,
}

/// <summary>
/// Current resource-free projection of one admitted Library under one exact
/// Ecosystem registration occurrence.
/// </summary>
public sealed class NavigationEcosystemLibraryContribution
{
    public NavigationEcosystemLibraryContribution(
        WorkspaceRegistrationRevision historicalRevision,
        WorkspaceRegistrationRevision currentRevision,
        WorkspaceEcosystemContributionRelation ecosystemRelation,
        WorkspaceLibraryAdmissionReceipt admission,
        WorkspaceLibraryOccurrence library)
    {
        ArgumentNullException.ThrowIfNull(historicalRevision);
        ArgumentNullException.ThrowIfNull(currentRevision);
        ArgumentNullException.ThrowIfNull(ecosystemRelation);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(library);
        if (!ReferenceEquals(
                historicalRevision.Workspace,
                currentRevision.Workspace)
            || !ReferenceEquals(
                currentRevision.Workspace,
                ecosystemRelation.Workspace)
            || !currentRevision.EcosystemContributions.Contains(
                ecosystemRelation)
            || !historicalRevision.Registrations.Any(
                registration =>
                    registration is WorkspaceRegistration.Ecosystem ecosystem
                    && ReferenceEquals(
                        ecosystem.Declaration,
                        ecosystemRelation.Ecosystem.Declaration))
            || !ReferenceEquals(
                admission.RegistrationRevision,
                historicalRevision)
            || !admission.Occurrences.Contains(library))
        {
            throw new ArgumentException(
                "Navigation Ecosystem contribution evidence must preserve one"
                + " exact Workspace, registration, relation, admission, and"
                + " Library association.");
        }

        HistoricalRevision = historicalRevision;
        CurrentRevision = currentRevision;
        EcosystemRelation = ecosystemRelation;
        Admission = admission;
        Library = library;
    }

    public InspectionWorkspaceIdentity Workspace => EcosystemRelation.Workspace;

    public WorkspaceRegistrationRevision HistoricalRevision { get; }

    public WorkspaceRegistrationRevision CurrentRevision { get; }

    public WorkspaceEcosystemContributionRelation EcosystemRelation { get; }

    public WorkspaceEcosystemRegistrationOccurrence Ecosystem =>
        EcosystemRelation.Ecosystem;

    public WorkspaceLibraryAdmissionReceipt Admission { get; }

    public WorkspaceLibraryOccurrence Library { get; }
}

/// <summary>
/// Closed classification of one historical Ecosystem contribution against one
/// current Workspace registration revision.
/// </summary>
public abstract class NavigationEcosystemContributionOutcome
{
    private protected NavigationEcosystemContributionOutcome()
    {
    }

    public sealed class Available : NavigationEcosystemContributionOutcome
    {
        public Available(NavigationEcosystemLibraryContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            Contribution = contribution;
        }

        public NavigationEcosystemLibraryContribution Contribution { get; }
    }

    public sealed class Unavailable : NavigationEcosystemContributionOutcome
    {
        public Unavailable(
            WorkspaceRegistrationRevision currentRevision,
            NavigationEcosystemContributionUnavailableReason reason)
        {
            ArgumentNullException.ThrowIfNull(currentRevision);
            CurrentRevision = currentRevision;
            Reason = reason;
        }

        public WorkspaceRegistrationRevision CurrentRevision { get; }

        public NavigationEcosystemContributionUnavailableReason Reason
        {
            get;
        }
    }

    public sealed class Rejected : NavigationEcosystemContributionOutcome
    {
        public Rejected(
            WorkspaceRegistrationRevision currentRevision,
            NavigationEcosystemContributionRejection reason)
        {
            ArgumentNullException.ThrowIfNull(currentRevision);
            CurrentRevision = currentRevision;
            Reason = reason;
        }

        public WorkspaceRegistrationRevision CurrentRevision { get; }

        public NavigationEcosystemContributionRejection Reason { get; }
    }
}
