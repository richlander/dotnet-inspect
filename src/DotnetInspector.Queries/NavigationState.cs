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
    public NavigationPublication Publication => new(Data.Revision, Data.Consumer.Generation);
}

internal sealed record NavigationStateData(
    NavigationWorkspaceSnapshot Installed,
    NavigationConsumerSnapshot Consumer,
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

/// <summary>Facts for one invocation. Availability is never retained in state or results.</summary>
public sealed record NavigationEvaluationFacts(
    WorkspaceScopeSnapshot Scope,
    NavigationPackageEvaluation? Package,
    NavigationFacetAvailabilityProvider Availability,
    NavigationNonReadyPackageEvaluation? NonReadyPackage = null);

public sealed record NavigationInitialization(
    StructuralSubjectIdentity? Subject = null,
    NavigationRetainedSubjectContext? Context = null);

/// <summary>Expected preparation failures are data; unexpected exceptions propagate.</summary>
public abstract record NavigationPreparation
{
    private protected NavigationPreparation() { }

    public sealed record Ready(NavigationEvaluationFacts Facts) : NavigationPreparation;
    public sealed record Unavailable(string Message) : NavigationPreparation;
    public sealed record Failed(string Message) : NavigationPreparation;
    public sealed record Aborted(string Message) : NavigationPreparation;
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
}

/// <summary>Exact product-peer evidence returned by this operation, never a session side channel.</summary>
public sealed record NavigationLensResolution(
    string Request,
    NavigationOperationKind Operation,
    NavigationEffectAuthority Authority,
    NavigationLensActivationResult Activation,
    DescendantSubjectLensRequest? DescendantRequest);

public sealed record NavigationOperationResult(
    NavigationConsumerResult Consumer,
    NavigationLensResolution? LensResolution);

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
        NavigationCompletionRejection? rejection = null)
    {
        Previous = previous;
        State = ReferenceEquals(previous.Data, next) ? previous : new(next);
        Request = request;
        Work = work;
        Result = result;
        AuthorityResult = authorityResult;
        Rejection = rejection;
    }

    internal NavigationState Previous { get; }
    public NavigationState State { get; }
    public NavigationRequest? Request { get; }
    public NavigationEvaluationRequest? Work { get; }
    public NavigationOperationResult? Result { get; }
    public NavigationAuthorityResult? AuthorityResult { get; }
    public NavigationCompletionRejection? Rejection { get; }
}
