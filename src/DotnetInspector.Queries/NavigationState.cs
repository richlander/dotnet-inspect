using System.Collections.Immutable;
using System.Globalization;

namespace DotnetInspector.Queries;

/// <summary>Semantic state and action publication are independently versioned.</summary>
public sealed record NavigationPublication(string Revision, string Generation);

/// <summary>
/// Product-issued Navigation state for one exact Workspace realization. The host
/// retains the current value, not a service, and commits transitions against that
/// current slot. Consumer snapshots are never accepted as semantic prior state.
/// </summary>
public sealed class NavigationState
{
    internal NavigationState(NavigationStateData data) => Data = data;

    internal NavigationStateData Data { get; }
    internal NavigationWorkspaceSnapshot InstalledSnapshot => Data.Installed;

    public string Id => Data.Projection.Session;
    public InspectionWorkspaceIdentity Workspace => Data.Installed.Workspace.Identity;
    public NavigationConsumerSnapshot Snapshot => Data.Consumer;
    public NavigationConsumerScopeStatus Scope => Data.Scope;
    public NavigationPublication Publication => new(Data.Revision, Data.Consumer.Generation);
}

internal sealed record NavigationStateData(
    NavigationWorkspaceSnapshot Installed,
    NavigationConsumerSnapshot Consumer,
    NavigationConsumerScopeStatus Scope,
    NavigationProjectionState Projection,
    ImmutableDictionary<string, NavigationActionTarget> Actions)
{
    internal ImmutableDictionary<string, NavigationAction> ConsumedActions { get; init; } =
        ImmutableDictionary.Create<string, NavigationAction>(StringComparer.Ordinal);
    internal bool ActionsNeedRenewal { get; init; }
    internal long NextIntent { get; init; } = 1;
    internal long NextRevision { get; init; } = 1;
    internal string Intent => $"{Projection.Session}:i:{NextIntent.ToString(CultureInfo.InvariantCulture)}";
    internal string Revision => $"{Projection.Session}:r:{NextRevision.ToString(CultureInfo.InvariantCulture)}";
    internal NavigationEvaluationRequest? Explicit { get; init; }
    internal NavigationEvaluationRequest? MaintenanceAttempt { get; init; }
    internal ImmutableArray<NavigationRequest> Maintenance { get; init; } = [];
    internal ImmutableArray<NavigationRequest> Synchronization { get; init; } = [];
    internal NavigationPublication? Acknowledged { get; init; }
    internal NavigationEffectAuthority? Effect { get; init; }
    internal NavigationEffectAuthority? ConsumerInstallation { get; init; }
    internal NavigationScopeEvaluationRequest? ProtectedScope { get; init; }
}

/// <summary>Exact identity of one product-issued request, including queued work.</summary>
public sealed class NavigationRequest
{
    internal NavigationRequest(string session, string id, NavigationOperationKind operation)
    {
        Session = session;
        Id = id;
        Operation = operation;
    }

    public string Session { get; }
    public string Id { get; }
    public NavigationOperationKind Operation { get; }
}

/// <summary>
/// One exact evaluation attempt. The ticket carries data, never access authority.
/// Workspace admission and resource settlement remain with their existing owner.
/// </summary>
public sealed class NavigationEvaluationRequest
{
    internal NavigationEvaluationRequest(
        NavigationRequest request,
        string attempt,
        NavigationStateData basis,
        WorkspacePackageOccurrence? occurrence,
        NavigationActionTarget? target,
        NavigationConsumerRequest? consumerRequest)
    {
        Identity = request;
        Attempt = attempt;
        Basis = basis.Installed;
        Publication = new(basis.Revision, basis.Consumer.Generation);
        Intent = basis.Intent;
        Occurrence = occurrence;
        Target = target;
        ConsumerRequest = consumerRequest;
    }

    public NavigationRequest Identity { get; }
    public string Request => Identity.Id;
    public NavigationOperationKind Operation => Identity.Operation;
    public string Attempt { get; }
    public string Intent { get; }
    public InspectionWorkspaceIdentity Workspace => Basis.Workspace.Identity;
    public WorkspacePackageOccurrence? Occurrence { get; }
    public NavigationPublication Publication { get; }
    internal NavigationWorkspaceSnapshot Basis { get; }
    internal NavigationActionTarget? Target { get; }
    internal NavigationConsumerRequest? ConsumerRequest { get; }
}

/// <summary>
/// Exact resource-free Navigation attempt protecting one Scope association.
/// It carries correlation data, never Scope submission or preparation authority.
/// </summary>
public sealed class NavigationScopeEvaluationRequest
{
    internal NavigationScopeEvaluationRequest(
        NavigationRequest request,
        string attempt,
        string intent,
        WorkspaceScopeOperationAssociation association,
        NavigationWorkspaceSnapshot basis)
    {
        Identity = request;
        Attempt = attempt;
        Intent = intent;
        Association = association;
        Basis = basis;
    }

    public NavigationRequest Identity { get; }
    public string Request => Identity.Id;
    public string Attempt { get; }
    public string Intent { get; }
    public InspectionWorkspaceIdentity Workspace => Association.Workspace;
    public WorkspaceScopeOperationAssociation Association { get; }
    internal NavigationWorkspaceSnapshot Basis { get; }
}

/// <summary>Facts for one invocation. Availability is never retained in state or results.</summary>
public sealed record NavigationEvaluationFacts(
    WorkspaceScopeSnapshot Scope,
    NavigationPackageEvaluation? Package,
    NavigationFacetAvailabilityProvider Availability,
    NavigationNonReadyPackageEvaluation? NonReadyPackage = null);

public sealed record NavigationInitialization(
    StructuralSubjectIdentity? Subject = null,
    NavigationRetainedSubjectContext? Context = null,
    NavigationLensIdentity? Lens = null);

public enum NavigationRestorationRejectionKind
{
    InvalidContext,
    SubjectOutsideContext,
    LensRequiresSubject,
    LensSubjectMismatch,
    Registry,
}

public enum NavigationRestorationFailureKind
{
    PackageNotPrepared,
    IncompleteInventory,
}

/// <summary>
/// Closed result of preparing Navigation state inside one fresh unpublished
/// Workspace. Only <see cref="Prepared"/> carries state or effect authority.
/// </summary>
public abstract record NavigationRestorationPreparationResult
{
    private protected NavigationRestorationPreparationResult(
        InspectionWorkspaceIdentity workspace,
        NavigationInitialization request)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        Workspace = workspace;
        Request = request;
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public NavigationInitialization Request { get; }

    public sealed record Prepared : NavigationRestorationPreparationResult
    {
        internal Prepared(
            InspectionWorkspaceIdentity workspace,
            NavigationInitialization request,
            NavigationOperationInitialization initialization)
            : base(workspace, request)
        {
            ArgumentNullException.ThrowIfNull(initialization);
            Initialization = initialization;
        }

        public NavigationOperationInitialization Initialization { get; }
    }

    public sealed record Unavailable : NavigationRestorationPreparationResult
    {
        internal Unavailable(
            InspectionWorkspaceIdentity workspace,
            NavigationInitialization request,
            StructuralSubjectIdentity subject,
            string message)
            : base(workspace, request)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            Subject = subject;
            Message = message;
        }

        public StructuralSubjectIdentity Subject { get; }

        public string Message { get; }
    }

    public sealed record Failed : NavigationRestorationPreparationResult
    {
        internal Failed(
            InspectionWorkspaceIdentity workspace,
            NavigationInitialization request,
            StructuralSubjectIdentity subject,
            NavigationRestorationFailureKind kind,
            string message,
            NavigationTypeInventoryOutcome? inventory = null)
            : base(workspace, request)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == NavigationRestorationFailureKind.IncompleteInventory
                && inventory is null)
            {
                throw new ArgumentException(
                    "An incomplete-inventory failure requires its exact evidence.",
                    nameof(inventory));
            }

            Subject = subject;
            Kind = kind;
            Message = message;
            Inventory = inventory;
        }

        public StructuralSubjectIdentity Subject { get; }

        public NavigationRestorationFailureKind Kind { get; }

        public string Message { get; }

        public NavigationTypeInventoryOutcome? Inventory { get; }
    }

    public sealed record Rejected : NavigationRestorationPreparationResult
    {
        internal Rejected(
            InspectionWorkspaceIdentity workspace,
            NavigationInitialization request,
            NavigationRestorationRejectionKind kind,
            string message,
            NavigationLensActivationResult.Rejected? lensResolution = null)
            : base(workspace, request)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == NavigationRestorationRejectionKind.Registry
                && lensResolution is null)
            {
                throw new ArgumentException(
                    "A Registry rejection requires its exact activation evidence.",
                    nameof(lensResolution));
            }

            Kind = kind;
            Message = message;
            LensResolution = lensResolution;
        }

        public NavigationRestorationRejectionKind Kind { get; }

        public string Message { get; }

        public NavigationLensActivationResult.Rejected? LensResolution
        {
            get;
        }
    }
}

/// <summary>Expected preparation failures are data; unexpected exceptions propagate.</summary>
public abstract record NavigationPreparation
{
    private protected NavigationPreparation() { }

    public sealed record Ready(NavigationEvaluationFacts Facts) : NavigationPreparation;
    public sealed record Unavailable(string Message) : NavigationPreparation;
    public sealed record Failed(string Message) : NavigationPreparation;
    public sealed record Aborted(string Message) : NavigationPreparation;
}

/// <summary>
/// Invocation-local preparation for one protected Scope settlement. Ready facts
/// may name an exact Navigation-owned subject/context/lens successor.
/// </summary>
public abstract record NavigationScopePreparation
{
    private protected NavigationScopePreparation() { }

    public sealed record Ready(
        NavigationEvaluationFacts Facts,
        NavigationInitialization? Initialization = null)
        : NavigationScopePreparation;

    public sealed record Unavailable(
        string Message,
        NavigationInitialization? Initialization = null)
        : NavigationScopePreparation;

    public sealed record Failed(
        string Message,
        NavigationInitialization? Initialization = null)
        : NavigationScopePreparation;

    public sealed record Aborted(
        string Message,
        NavigationInitialization? Initialization = null)
        : NavigationScopePreparation;

    public sealed record Historical : NavigationScopePreparation;
}

/// <summary>
/// Detached protected-Scope evaluation evidence. Only correlated completion may
/// issue consumer effect authority.
/// </summary>
public sealed class NavigationScopeEvaluationResult
{
    internal NavigationScopeEvaluationResult(
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeOperationResult settlement,
        NavigationWorkspaceSnapshot snapshot,
        NavigationConsumerScopeStatus scope,
        NavigationConsumerOutcome outcome,
        NavigationTypeInventoryOutcome? incompleteInventory = null,
        NavigationLensActivationResult? resolution = null,
        NavigationCoordinateRetentionResult? coordinateRetention = null)
    {
        Request = request;
        Settlement = settlement;
        Snapshot = snapshot;
        Scope = scope;
        Outcome = outcome;
        IncompleteInventory = incompleteInventory;
        Resolution = resolution;
        CoordinateRetention = coordinateRetention;
    }

    public NavigationScopeEvaluationRequest Request { get; }
    public WorkspaceScopeOperationResult Settlement { get; }
    public NavigationConsumerScopeStatus Scope { get; }
    public NavigationConsumerOutcome Outcome { get; }
    public NavigationLensActivationResult? Resolution { get; }
    public NavigationCoordinateRetentionResult? CoordinateRetention { get; }
    internal NavigationWorkspaceSnapshot Snapshot { get; }
    internal NavigationTypeInventoryOutcome? IncompleteInventory { get; }

    internal NavigationScopeEvaluationResult WithCoordinateRetention(
        NavigationCoordinateRetentionResult coordinateRetention) =>
        new(
            Request,
            Settlement,
            Snapshot,
            Scope,
            Outcome with
            {
                CoordinateRetention = new(
                    coordinateRetention.Disposition,
                    coordinateRetention.Detail,
                    coordinateRetention.LibraryPairing?.Status,
                    coordinateRetention.TypeCorrespondence?.Status,
                    coordinateRetention.MemberCorrespondence?.Status),
            },
            IncompleteInventory,
            Resolution,
            coordinateRetention);
}

/// <summary>Detached evaluation evidence; only completion may issue effect authority.</summary>
public sealed class NavigationEvaluationResult
{
    internal NavigationEvaluationResult(
        NavigationEvaluationRequest request,
        NavigationWorkspaceSnapshot snapshot,
        NavigationConsumerOutcome outcome,
        NavigationLensActivationResult? resolution = null,
        DescendantSubjectLensRequest? descendant = null,
        NavigationTypeInventoryOutcome? incompleteInventory = null)
    {
        Request = request;
        Snapshot = snapshot;
        Outcome = outcome;
        Resolution = resolution;
        Descendant = descendant;
        IncompleteInventory = incompleteInventory;
    }

    public NavigationEvaluationRequest Request { get; }
    public NavigationConsumerOutcome Outcome { get; }
    public NavigationLensActivationResult? Resolution { get; }
    public DescendantSubjectLensRequest? Descendant { get; }
    internal NavigationWorkspaceSnapshot Snapshot { get; }
    internal NavigationTypeInventoryOutcome? IncompleteInventory { get; }
}

public enum NavigationAuthorityResult
{
    Accepted,
    InvalidAuthority,
    InstallationRequired,
}

public enum NavigationCompletionRejection
{
    ForeignWorkspace,
    ForeignSession,
    WrongTicket,
    StaleAttempt,
    WrongScopeAssociation,
    WrongScopeAttempt,
}

public enum NavigationAdmissionRefusalKind
{
    ProtectedScopeOperation,
    HistoricalScope,
    ForeignWorkspace,
}

/// <summary>A synchronous refusal before ordinary Navigation admission.</summary>
public sealed record NavigationAdmissionRefusal(
    NavigationAdmissionRefusalKind Kind,
    string Message);

public enum NavigationScopeCancellationObservationKind
{
    NoSettlement,
    CorrelatedSettlement,
}

/// <summary>
/// Navigation's interpretation of Scope cancellation control. Only a correlated
/// Settled response carries mutation settlement.
/// </summary>
public sealed record NavigationScopeCancellationObservation(
    NavigationScopeCancellationObservationKind Kind,
    WorkspaceScopeCancellationResult Control,
    WorkspaceScopeOperationResult? Settlement = null);

/// <summary>Exact product-peer evidence returned by this operation, never a session side channel.</summary>
public sealed record NavigationLensResolution(
    string Request,
    NavigationOperationKind Operation,
    NavigationEffectAuthority Authority,
    NavigationLensActivationResult Activation,
    DescendantSubjectLensRequest? DescendantRequest);

public sealed record NavigationOperationResult(
    NavigationConsumerResult Consumer,
    NavigationLensResolution? LensResolution,
    WorkspaceScopeOperationResult? ScopeResult = null,
    NavigationCoordinateRetentionResult? CoordinateRetention = null);

public enum NavigationActionPublicationKind
{
    Published,
    Stale,
    Unavailable,
    Rejected,
    Refused,
}

/// <summary>
/// Result of binding one opaque action to the current retained Navigation
/// publication. Only <see cref="NavigationActionPublicationKind.Published"/>
/// carries an action.
/// </summary>
public sealed record NavigationActionPublicationResult(
    NavigationActionPublicationKind Kind,
    NavigationAction? Action = null,
    NavigationRejectionKind? Rejection = null,
    string? Message = null);

/// <summary>A product transition must be committed against its exact current input slot.</summary>
public sealed class NavigationTransition
{
    internal NavigationTransition(
        NavigationState previous,
        NavigationStateData next,
        NavigationRequest? request = null,
        NavigationEvaluationRequest? work = null,
        NavigationOperationResult? result = null,
        NavigationAuthorityResult? authorityResult = null,
        NavigationCompletionRejection? rejection = null,
        NavigationActionPublicationResult? actionPublication = null,
        NavigationScopeEvaluationRequest? scopeWork = null,
        NavigationAdmissionRefusal? admissionRefusal = null)
    {
        Previous = previous;
        State = ReferenceEquals(previous.Data, next) ? previous : new(next);
        Request = request;
        Work = work;
        Result = result;
        AuthorityResult = authorityResult;
        Rejection = rejection;
        ActionPublication = actionPublication;
        ScopeWork = scopeWork;
        AdmissionRefusal = admissionRefusal;
    }

    internal NavigationState Previous { get; }
    public NavigationState State { get; }
    public NavigationRequest? Request { get; }
    public NavigationEvaluationRequest? Work { get; }
    public NavigationOperationResult? Result { get; }
    public NavigationAuthorityResult? AuthorityResult { get; }
    public NavigationCompletionRejection? Rejection { get; }
    public NavigationActionPublicationResult? ActionPublication { get; }
    public NavigationScopeEvaluationRequest? ScopeWork { get; }
    public NavigationAdmissionRefusal? AdmissionRefusal { get; }
}
