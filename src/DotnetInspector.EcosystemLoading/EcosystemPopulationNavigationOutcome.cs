using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

/// <summary>
/// Why historical Ecosystem admission evidence cannot produce a current
/// Navigation contribution.
/// </summary>
public enum EcosystemPopulationNavigationUnavailableReason
{
    RegistrationNotCurrent,
}

/// <summary>
/// Why a current revision cannot consume historical Ecosystem contribution
/// evidence.
/// </summary>
public enum EcosystemPopulationNavigationRejection
{
    ForeignWorkspace,
}

/// <summary>
/// Current resource-free projection of one admitted Focus Library under one
/// exact Ecosystem registration occurrence.
/// </summary>
public sealed class EcosystemPopulationNavigationLibraryContribution
{
    internal EcosystemPopulationNavigationLibraryContribution(
        WorkspaceRegistrationRevision historicalRevision,
        WorkspaceRegistrationRevision currentRevision,
        WorkspaceEcosystemContributionRelation ecosystemRelation,
        WorkspaceLibraryAdmissionReceipt admission,
        WorkspaceLibraryOccurrence library)
    {
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
/// Closed classification of one historical Ecosystem Focus contribution
/// against one current Workspace registration revision.
/// </summary>
public abstract class EcosystemPopulationNavigationOutcome
{
    private protected EcosystemPopulationNavigationOutcome()
    {
    }

    public sealed class Available : EcosystemPopulationNavigationOutcome
    {
        internal Available(
            EcosystemPopulationNavigationLibraryContribution contribution) =>
            Contribution = contribution;

        public EcosystemPopulationNavigationLibraryContribution Contribution
        {
            get;
        }
    }

    public sealed class Unavailable : EcosystemPopulationNavigationOutcome
    {
        internal Unavailable(
            WorkspaceRegistrationRevision currentRevision,
            EcosystemPopulationNavigationUnavailableReason reason)
        {
            CurrentRevision = currentRevision;
            Reason = reason;
        }

        public WorkspaceRegistrationRevision CurrentRevision { get; }

        public EcosystemPopulationNavigationUnavailableReason Reason { get; }
    }

    public sealed class Rejected : EcosystemPopulationNavigationOutcome
    {
        internal Rejected(
            WorkspaceRegistrationRevision currentRevision,
            EcosystemPopulationNavigationRejection reason)
        {
            CurrentRevision = currentRevision;
            Reason = reason;
        }

        public WorkspaceRegistrationRevision CurrentRevision { get; }

        public EcosystemPopulationNavigationRejection Reason { get; }
    }
}
