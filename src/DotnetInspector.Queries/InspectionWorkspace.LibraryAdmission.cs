using System.Collections.Immutable;
using DotnetInspector.Libraries;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque process-local identity for one accepted Workspace Library admission.
/// </summary>
public sealed class WorkspaceLibraryAdmissionIdentity
{
    internal WorkspaceLibraryAdmissionIdentity()
    {
    }
}

/// <summary>
/// Opaque process-local identity for one physically admitted Library.
/// </summary>
public sealed class WorkspaceLibraryOccurrenceIdentity :
    InspectionWorkspaceOccurrenceIdentity
{
    internal WorkspaceLibraryOccurrenceIdentity(
        InspectionWorkspaceIdentity workspaceIdentity)
        : base(workspaceIdentity)
    {
    }
}

/// <summary>
/// Resource-free evidence for one exact Library physically admitted to a
/// Workspace.
/// </summary>
public sealed class WorkspaceLibraryOccurrence
{
    internal WorkspaceLibraryOccurrence(
        WorkspaceLibraryOccurrenceIdentity identity,
        WorkspaceLibraryAdmissionIdentity admission,
        LibraryReference library)
    {
        Identity = identity;
        Admission = admission;
        Library = library;
    }

    public WorkspaceLibraryOccurrenceIdentity Identity { get; }

    public WorkspaceLibraryAdmissionIdentity Admission { get; }

    public LibraryReference Library { get; }
}

/// <summary>
/// Resource-free receipt for one atomically accepted session-backed Library
/// batch.
/// </summary>
public sealed class WorkspaceLibraryAdmissionReceipt
{
    internal WorkspaceLibraryAdmissionReceipt(
        WorkspaceRegistrationRevision registrationRevision,
        WorkspaceLibraryAdmissionIdentity identity,
        ImmutableArray<WorkspaceLibraryOccurrence> occurrences)
    {
        RegistrationRevision = registrationRevision;
        Identity = identity;
        Occurrences = occurrences;
    }

    public InspectionWorkspaceIdentity Workspace =>
        RegistrationRevision.Workspace;

    public WorkspaceRegistrationRevision RegistrationRevision
    {
        get;
    }

    public WorkspaceLibraryAdmissionIdentity Identity { get; }

    public ImmutableArray<WorkspaceLibraryOccurrence> Occurrences
    {
        get;
    }
}

public enum WorkspaceLibraryAdmissionRejection
{
    ForeignWorkspace,
    RevisionMismatch,
    WorkspaceClosing,
    WorkspaceClosed,
}

public enum WorkspaceLibraryAdmissionCleanupFailureKind
{
    LibraryOwner,
    ArtifactSession,
}

/// <summary>
/// Cleanup failure retained with the exact resource kind and optional Library.
/// </summary>
public sealed class WorkspaceLibraryAdmissionCleanupFailure
{
    internal WorkspaceLibraryAdmissionCleanupFailure(
        WorkspaceLibraryAdmissionCleanupFailureKind kind,
        LibraryReference? library,
        Exception failure)
    {
        Kind = kind;
        Library = library;
        Failure = failure;
    }

    public WorkspaceLibraryAdmissionCleanupFailureKind Kind { get; }

    public LibraryReference? Library { get; }

    public Exception Failure { get; }
}

/// <summary>
/// Closed result of transferring one session-backed Library batch to a
/// Workspace.
/// </summary>
public abstract class WorkspaceLibraryAdmissionOutcome
{
    private protected WorkspaceLibraryAdmissionOutcome()
    {
    }

    public sealed class Accepted : WorkspaceLibraryAdmissionOutcome
    {
        internal Accepted(WorkspaceLibraryAdmissionReceipt receipt) =>
            Receipt = receipt;

        public WorkspaceLibraryAdmissionReceipt Receipt { get; }
    }

    public abstract class NotAccepted :
        WorkspaceLibraryAdmissionOutcome
    {
        private protected NotAccepted(
            WorkspaceRegistrationRevision currentRevision,
            WorkspaceLibraryAdmissionRejection reason)
        {
            CurrentRevision = currentRevision;
            Reason = reason;
        }

        public WorkspaceRegistrationRevision CurrentRevision
        {
            get;
        }

        public WorkspaceLibraryAdmissionRejection Reason { get; }
    }

    public sealed class Rejected : NotAccepted
    {
        internal Rejected(
            WorkspaceRegistrationRevision currentRevision,
            WorkspaceLibraryAdmissionRejection reason)
            : base(currentRevision, reason)
        {
        }
    }

    public sealed class Failed : NotAccepted
    {
        internal Failed(
            WorkspaceRegistrationRevision currentRevision,
            WorkspaceLibraryAdmissionRejection reason,
            ImmutableArray<
                WorkspaceLibraryAdmissionCleanupFailure> cleanupFailures)
            : base(currentRevision, reason)
        {
            CleanupFailures = cleanupFailures;
        }

        public ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>
            CleanupFailures
        { get; }
    }
}

public enum WorkspaceLibraryOperationRejection
{
    ForeignWorkspace,
    UnknownOccurrence,
    WorkspaceClosing,
    WorkspaceClosed,
    OwnerRetiring,
    OwnerReleased,
}

/// <summary>
/// Closed result of requesting operation authority for one admitted Library
/// occurrence.
/// </summary>
public abstract class WorkspaceLibraryOperationIssueOutcome
{
    private protected WorkspaceLibraryOperationIssueOutcome()
    {
    }

    public sealed class Issued : WorkspaceLibraryOperationIssueOutcome
    {
        internal Issued(LibraryOperationLease lease) => Lease = lease;

        public LibraryOperationLease Lease { get; }
    }

    public sealed class Rejected : WorkspaceLibraryOperationIssueOutcome
    {
        internal Rejected(
            WorkspaceLibraryOperationRejection reason,
            LibraryContentOwnerState? ownerState = null)
        {
            Reason = reason;
            OwnerState = ownerState;
        }

        public WorkspaceLibraryOperationRejection Reason { get; }

        public LibraryContentOwnerState? OwnerState { get; }
    }
}

/// <summary>
/// Terminal retirement evidence for one accepted Workspace Library
/// admission.
/// </summary>
public sealed class WorkspaceLibraryAdmissionCloseResult
{
    internal WorkspaceLibraryAdmissionCloseResult(
        WorkspaceLibraryAdmissionReceipt admission,
        ImmutableArray<
            WorkspaceLibraryAdmissionCleanupFailure> cleanupFailures)
    {
        Admission = admission;
        CleanupFailures = cleanupFailures;
    }

    public WorkspaceLibraryAdmissionReceipt Admission { get; }

    public ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>
        CleanupFailures
    { get; }

    public bool Succeeded => CleanupFailures.IsEmpty;
}

public sealed partial class InspectionWorkspace
{
    readonly List<WorkspaceLibraryAdmissionRegistration>
        _libraryAdmissions = [];

    /// <summary>
    /// Atomically transfers one exact Artifact session and its non-empty
    /// Library-owner batch to this Workspace.
    /// </summary>
    /// <remarks>
    /// Argument validation completes before ownership transfers. Once this
    /// method returns an outcome, the Workspace has either accepted the whole
    /// batch or settled every supplied owner and then the session.
    /// </remarks>
    public ValueTask<WorkspaceLibraryAdmissionOutcome>
        AdmitLibraryBatchAsync(
            WorkspaceRegistrationRevision expectedRegistrations,
            ArtifactSetSession artifactSession,
            IReadOnlyList<LibraryContentOwner> owners)
    {
        ArgumentNullException.ThrowIfNull(expectedRegistrations);
        ArgumentNullException.ThrowIfNull(artifactSession);
        ArgumentNullException.ThrowIfNull(owners);

        LibraryContentOwner[] ownerSnapshot = [.. owners];
        ValidateLibraryAdmissionBatch(
            artifactSession,
            ownerSnapshot);

        WorkspaceRegistrationRevision currentRegistrations;
        WorkspaceLibraryAdmissionRejection? rejection;
        lock (_gate)
        {
            currentRegistrations = _registrationRevision;
            if (_artifactSessions.Any(registration =>
                    ReferenceEquals(
                        registration.Session,
                        artifactSession))
                || _libraryAdmissions.Any(registration =>
                    ReferenceEquals(
                        registration.ArtifactSession,
                        artifactSession)))
            {
                throw new InvalidOperationException(
                    "The Artifact session is already owned by this Workspace.");
            }

            rejection =
                _state switch
                {
                    InspectionWorkspaceState.Closing =>
                        WorkspaceLibraryAdmissionRejection
                            .WorkspaceClosing,
                    InspectionWorkspaceState.Closed =>
                        WorkspaceLibraryAdmissionRejection
                            .WorkspaceClosed,
                    _ when !ReferenceEquals(
                        expectedRegistrations.Workspace,
                        _identity) =>
                        WorkspaceLibraryAdmissionRejection
                            .ForeignWorkspace,
                    _ when !ReferenceEquals(
                        expectedRegistrations.Identity,
                        currentRegistrations.Identity) =>
                        WorkspaceLibraryAdmissionRejection
                            .RevisionMismatch,
                    _ => null,
                };
            if (rejection is null)
            {
                var admissionIdentity =
                    new WorkspaceLibraryAdmissionIdentity();
                ImmutableArray<WorkspaceLibraryOccurrence>
                    occurrences =
                    [
                        .. ownerSnapshot.Select(owner =>
                            new WorkspaceLibraryOccurrence(
                                new(
                                    _identity),
                                admissionIdentity,
                                owner.Reference)),
                    ];
                var receipt =
                    new WorkspaceLibraryAdmissionReceipt(
                        currentRegistrations,
                        admissionIdentity,
                        occurrences);
                _libraryAdmissions.Add(
                    new WorkspaceLibraryAdmissionRegistration(
                        receipt,
                        artifactSession,
                        [.. ownerSnapshot]));
                return ValueTask.FromResult<
                    WorkspaceLibraryAdmissionOutcome>(
                        new WorkspaceLibraryAdmissionOutcome
                            .Accepted(receipt));
            }
        }

        return RejectLibraryAdmissionAsync(
            currentRegistrations,
            rejection.Value,
            artifactSession,
            ownerSnapshot);
    }

    /// <summary>
    /// Issues operation authority for one exact physically admitted Library
    /// while the Workspace remains open.
    /// </summary>
    public WorkspaceLibraryOperationIssueOutcome
        IssueLibraryOperation(
            WorkspaceLibraryOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        lock (_gate)
        {
            if (!ReferenceEquals(
                    occurrence.Identity.WorkspaceIdentity,
                    _identity))
            {
                return Rejected(
                    WorkspaceLibraryOperationRejection
                        .ForeignWorkspace);
            }
            if (_state == InspectionWorkspaceState.Closing)
            {
                return Rejected(
                    WorkspaceLibraryOperationRejection
                        .WorkspaceClosing);
            }
            if (_state == InspectionWorkspaceState.Closed)
            {
                return Rejected(
                    WorkspaceLibraryOperationRejection
                        .WorkspaceClosed);
            }

            WorkspaceLibraryAdmissionRegistration? registration =
                _libraryAdmissions.FirstOrDefault(
                    candidate =>
                        candidate.Contains(occurrence));
            if (registration is null)
            {
                return Rejected(
                    WorkspaceLibraryOperationRejection
                        .UnknownOccurrence);
            }

            LibraryOperationLeaseIssueOutcome issued =
                registration.IssueOperation(occurrence);
            return issued switch
            {
                LibraryOperationLeaseIssueOutcome.Issued available =>
                    new WorkspaceLibraryOperationIssueOutcome.Issued(
                        available.Lease),
                LibraryOperationLeaseIssueOutcome.OwnerRetiring =>
                    Rejected(
                        WorkspaceLibraryOperationRejection
                            .OwnerRetiring,
                        LibraryContentOwnerState.Retiring),
                LibraryOperationLeaseIssueOutcome.OwnerReleased released =>
                    Rejected(
                        WorkspaceLibraryOperationRejection
                            .OwnerReleased,
                        released.State),
                LibraryOperationLeaseIssueOutcome.ReferenceMismatch =>
                    throw new InvalidOperationException(
                        "Workspace Library admission lost exact owner correspondence."),
                _ => throw new InvalidOperationException(
                    "Unknown Library operation lease outcome."),
            };
        }
    }

    static WorkspaceLibraryOperationIssueOutcome.Rejected Rejected(
        WorkspaceLibraryOperationRejection reason,
        LibraryContentOwnerState? ownerState = null) =>
        new(reason, ownerState);

    static void ValidateLibraryAdmissionBatch(
        ArtifactSetSession artifactSession,
        IReadOnlyList<LibraryContentOwner> owners)
    {
        if (owners.Count == 0)
        {
            throw new ArgumentException(
                "Workspace Library admission requires at least one owner.",
                nameof(owners));
        }

        var seenOwners = new HashSet<LibraryContentOwner>(
            ReferenceEqualityComparer.Instance);
        var seenLibraries = new HashSet<LibraryReference>(
            ReferenceEqualityComparer.Instance);
        foreach (LibraryContentOwner owner in owners)
        {
            if (owner is null)
            {
                throw new ArgumentException(
                    "Workspace Library owners cannot contain null.",
                    nameof(owners));
            }
            if (owner.State != LibraryContentOwnerState.Active)
            {
                throw new ArgumentException(
                    "Workspace Library admission requires active owners.",
                    nameof(owners));
            }
            if (!seenOwners.Add(owner)
                || !seenLibraries.Add(owner.Reference))
            {
                throw new ArgumentException(
                    "Workspace Library admission requires distinct owners and Library references.",
                    nameof(owners));
            }
            if (owner.Reference.Contents.Any(content =>
                    !ReferenceEquals(
                        content.Generation,
                        artifactSession.Generation)))
            {
                throw new ArgumentException(
                    "Every admitted Library must belong to the supplied Artifact session generation.",
                    nameof(owners));
            }
        }
    }

    static async ValueTask<WorkspaceLibraryAdmissionOutcome>
        RejectLibraryAdmissionAsync(
            WorkspaceRegistrationRevision currentRegistrations,
            WorkspaceLibraryAdmissionRejection reason,
            ArtifactSetSession artifactSession,
            IReadOnlyList<LibraryContentOwner> owners)
    {
        ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>
            cleanupFailures =
                await WorkspaceLibraryAdmissionRegistration
                    .ReleaseResourcesAsync(
                        artifactSession,
                        owners)
                    .ConfigureAwait(false);
        if (cleanupFailures.IsEmpty)
        {
            return new WorkspaceLibraryAdmissionOutcome.Rejected(
                currentRegistrations,
                reason);
        }

        return new WorkspaceLibraryAdmissionOutcome.Failed(
            currentRegistrations,
            reason,
            cleanupFailures);
    }
}

internal sealed class WorkspaceLibraryAdmissionRegistration
{
    readonly ImmutableArray<LibraryContentOwner> _owners;

    internal WorkspaceLibraryAdmissionRegistration(
        WorkspaceLibraryAdmissionReceipt receipt,
        ArtifactSetSession artifactSession,
        ImmutableArray<LibraryContentOwner> owners)
    {
        Receipt = receipt;
        ArtifactSession = artifactSession;
        _owners = owners;
    }

    internal WorkspaceLibraryAdmissionReceipt Receipt { get; }

    internal ArtifactSetSession ArtifactSession { get; }

    internal bool Contains(WorkspaceLibraryOccurrence occurrence) =>
        Receipt.Occurrences.Any(
            candidate => ReferenceEquals(
                candidate,
                occurrence));

    internal LibraryOperationLeaseIssueOutcome IssueOperation(
        WorkspaceLibraryOccurrence occurrence)
    {
        int index = Receipt.Occurrences.IndexOf(occurrence);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "The Library occurrence does not belong to this admission.");
        }

        return _owners[index].IssueOperationLease(
            occurrence.Library);
    }

    internal async Task<WorkspaceLibraryAdmissionCloseResult>
        ReleaseAsync()
    {
        ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>
            cleanupFailures =
                await ReleaseResourcesAsync(
                        ArtifactSession,
                        _owners)
                    .ConfigureAwait(false);
        return new(
            Receipt,
            cleanupFailures);
    }

    internal static async ValueTask<
        ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>>
        ReleaseResourcesAsync(
            ArtifactSetSession artifactSession,
            IReadOnlyList<LibraryContentOwner> owners)
    {
        Task<ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>>[]
            ownerRetirements =
            [
                .. owners.Select(RetireOwnerAsync),
            ];
        ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>[]
            ownerFailures =
                await Task.WhenAll(ownerRetirements)
                    .ConfigureAwait(false);
        var failures =
            ImmutableArray.CreateBuilder<
                WorkspaceLibraryAdmissionCleanupFailure>();
        foreach (ImmutableArray<
            WorkspaceLibraryAdmissionCleanupFailure> ownerFailure
            in ownerFailures)
        {
            failures.AddRange(ownerFailure);
        }

        Exception? sessionFailure = null;
        try
        {
            await artifactSession.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            sessionFailure = failure;
        }

        if (artifactSession.CleanupFailures.Count > 0)
        {
            foreach (Exception failure
                in artifactSession.CleanupFailures)
            {
                failures.Add(
                    new(
                        WorkspaceLibraryAdmissionCleanupFailureKind
                            .ArtifactSession,
                        library: null,
                        failure));
            }
        }
        else if (sessionFailure is not null)
        {
            failures.Add(
                new(
                    WorkspaceLibraryAdmissionCleanupFailureKind
                        .ArtifactSession,
                    library: null,
                    sessionFailure));
        }

        return failures.ToImmutable();
    }

    static async Task<
        ImmutableArray<WorkspaceLibraryAdmissionCleanupFailure>>
        RetireOwnerAsync(LibraryContentOwner owner)
    {
        Exception? retirementFailure = null;
        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            retirementFailure = failure;
        }

        if (owner.CleanupFailures.Count > 0)
        {
            return
            [
                .. owner.CleanupFailures.Select(
                    failure =>
                        new WorkspaceLibraryAdmissionCleanupFailure(
                            WorkspaceLibraryAdmissionCleanupFailureKind
                                .LibraryOwner,
                            owner.Reference,
                            failure)),
            ];
        }
        if (retirementFailure is not null)
        {
            return
            [
                new WorkspaceLibraryAdmissionCleanupFailure(
                    WorkspaceLibraryAdmissionCleanupFailureKind
                        .LibraryOwner,
                    owner.Reference,
                    retirementFailure),
            ];
        }

        return [];
    }
}
