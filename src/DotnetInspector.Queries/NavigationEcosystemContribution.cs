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
/// One exact Ecosystem declaration retained by one current Workspace
/// registration revision.
/// </summary>
public sealed class NavigationEcosystemRegistrationReference
{
    public NavigationEcosystemRegistrationReference(
        WorkspaceRegistrationRevision revision,
        WorkspaceEcosystemRegistrationDeclaration registration)
    {
        Revision = revision;
        Registration = registration;
    }

    public InspectionWorkspaceIdentity Workspace => Revision.Workspace;

    public WorkspaceRegistrationRevision Revision { get; }

    public WorkspaceEcosystemRegistrationDeclaration Registration { get; }
}

/// <summary>
/// Current resource-free projection of one admitted Library under one exact
/// Ecosystem declaration.
/// </summary>
public sealed class NavigationEcosystemLibraryContribution
{
    public NavigationEcosystemLibraryContribution(
        WorkspaceRegistrationRevision historicalRevision,
        NavigationEcosystemRegistrationReference ecosystem,
        WorkspaceLibraryAdmissionReceipt admission,
        WorkspaceLibraryOccurrence library)
    {
        HistoricalRevision = historicalRevision;
        Ecosystem = ecosystem;
        Admission = admission;
        Library = library;
    }

    public InspectionWorkspaceIdentity Workspace => Ecosystem.Workspace;

    public WorkspaceRegistrationRevision HistoricalRevision { get; }

    public NavigationEcosystemRegistrationReference Ecosystem { get; }

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
