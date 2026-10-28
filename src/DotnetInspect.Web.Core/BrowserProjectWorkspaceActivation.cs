using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

internal abstract record BrowserRetainedWorkspaceInput
{
    private protected BrowserRetainedWorkspaceInput() { }

    internal sealed record Restoration(CompleteRestorationRequestBasis Request)
        : BrowserRetainedWorkspaceInput;

    internal sealed record Project(
        InspectionEnvelope<ProjectInputDocument> Inspection,
        TraversalTargetFrameworkPolicy TraversalTargetPolicy)
        : BrowserRetainedWorkspaceInput;
}

internal abstract record BrowserRetainedWorkspaceOrigin
{
    private protected BrowserRetainedWorkspaceOrigin() { }

    internal sealed record Restored(
        CompleteRestorationRequestBasis Request,
        CompleteRestorationProjection Projection,
        CommittedScenarioDefinitionSet? Definition)
        : BrowserRetainedWorkspaceOrigin;

    internal sealed record Project(BrowserRetainedWorkspaceInput.Project Input)
        : BrowserRetainedWorkspaceOrigin;
}

internal abstract record BrowserWorkspaceConstructionResult
{
    private protected BrowserWorkspaceConstructionResult() { }

    internal sealed record Prepared(BrowserPreparedWorkspaceActivation Activation)
        : BrowserWorkspaceConstructionResult;

    internal sealed record Superseded : BrowserWorkspaceConstructionResult;

    internal sealed record Failed(CompleteRestorationFailure Failure)
        : BrowserWorkspaceConstructionResult;
}

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserRetainedWorkspaceActivationOwner
{
    internal async ValueTask<BrowserRetainedWorkspacePosting> SelectProjectInspectorAsync(
        string retainedDefinitionId,
        string realizationId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        BrowserRetainedWorkspacePosting posting;
        NavigationAction action;
        lock (_gate)
        {
            posting = _active is { Origin: BrowserRetainedWorkspaceOrigin.Project } active
                && active.RetainedDefinitionId == retainedDefinitionId
                && active.RealizationId == realizationId
                    ? active
                    : throw new InvalidOperationException("The Project realization is no longer active.");
            action = posting.Navigation.Snapshot.Lenses
                .Select(lens => lens.Action)
                .FirstOrDefault(candidate => candidate?.Id == actionId)
                ?? throw new InvalidOperationException("The Project inspector action is no longer available.");
        }

        WorkspaceRealizationOperationAdmission admission =
            await EnterOperationAsync(retainedDefinitionId, cancellationToken).ConfigureAwait(false);
        if (admission is not WorkspaceRealizationOperationAdmission.Admitted admitted)
            throw new InvalidOperationException("The Project realization is unavailable.");
        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        if (!ReferenceEquals(operation.Realization, posting.Realization))
            throw new InvalidOperationException("The Project realization changed.");
        NavigationConsumerResult navigation = await posting.NavigationState.ExecuteAsync(
            action,
            (request, token) => posting.NavigationPreparation.PrepareAsync(operation, request, token),
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Project Navigation was retired.");
        lock (_gate)
        {
            if (!ReferenceEquals(_active, posting)
                || navigation.Authority is null
                || !posting.NavigationState.ValidateAuthority(navigation.Authority))
            {
                throw new InvalidOperationException("The Project inspector selection was superseded.");
            }
            return _active = posting with { Navigation = navigation };
        }
    }

    async Task<BrowserWorkspaceConstructionResult> PrepareActivationAsync(
        BrowserRetainedWorkspaceActivationIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.Request.Input is BrowserRetainedWorkspaceInput.Project project)
        {
            return await PrepareProjectAsync(intent, project, cancellationToken)
                .ConfigureAwait(false);
        }

        CompleteRestorationResult<BrowserPreparedWorkspaceActivation> result =
            await RestoreAsync(intent, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>.Activated ready
                when ReferenceEquals(ready.Workspace.Intent, intent.Identity)
                    && ReferenceEquals(ready.Activation.Intent, intent.Identity) =>
                new BrowserWorkspaceConstructionResult.Prepared(ready.Activation),
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>.Superseded =>
                new BrowserWorkspaceConstructionResult.Superseded(),
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>.Failed failed =>
                new BrowserWorkspaceConstructionResult.Failed(failed.Failure),
            _ => throw new InvalidOperationException(
                "Complete restoration returned an unsupported Browser construction result."),
        };
    }

    async Task<BrowserWorkspaceConstructionResult> PrepareProjectAsync(
        BrowserRetainedWorkspaceActivationIntent intent,
        BrowserRetainedWorkspaceInput.Project input,
        CancellationToken cancellationToken)
    {
        ProjectWorkspacePlanOutcome plan = ProjectWorkspaceConstruction.CreatePlan(
            input.Inspection.Content, input.TraversalTargetPolicy);
        if (plan is ProjectWorkspacePlanOutcome.Rejected)
        {
            return ProjectFailure(
                "The Project document was rejected by shared Project inspection.");
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            intent.Revocation, cancellationToken);
        CancellationToken operationToken = cancellation.Token;
        BrowserWorkspaceRealizationHost host = _host;
        BrowserWorkspaceRealizationCandidateStartResult start;
        try
        {
            start = await host.BeginCandidateAsync(
                ((ProjectWorkspacePlanOutcome.Available)plan).Plan,
                operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            return new BrowserWorkspaceConstructionResult.Superseded();
        }
        if (start is not BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            return start switch
            {
                BrowserWorkspaceRealizationCandidateStartResult.Superseded =>
                    new BrowserWorkspaceConstructionResult.Superseded(),
                BrowserWorkspaceRealizationCandidateStartResult.CapacityUnavailable capacity =>
                    ProjectFailure(
                        $"The Browser Workspace capacity of {capacity.Capacity.Limit} realizations is unavailable."),
                BrowserWorkspaceRealizationCandidateStartResult.Closed =>
                    ProjectFailure("The Browser Workspace host is closed."),
                _ => throw new InvalidOperationException(
                    "The Browser Workspace host returned an unsupported construction result."),
            };
        }

        BrowserWorkspaceConstructionResult result;
        try
        {
            result = await PrepareProjectCandidateAsync(
                host, intent, input, prepared.Candidate, operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            result = new BrowserWorkspaceConstructionResult.Superseded();
        }
        catch (Exception failure)
        {
            CompleteRestorationFailure? cleanup =
                await BrowserPreparedWorkspaceActivation.SettleCandidateAsync(
                    host, prepared.Candidate).ConfigureAwait(false);
            if (cleanup is not null)
                throw new AggregateException(failure, new InvalidOperationException(cleanup.Message));
            throw;
        }

        if (result is not BrowserWorkspaceConstructionResult.Prepared)
        {
            CompleteRestorationFailure? cleanup =
                await BrowserPreparedWorkspaceActivation.SettleCandidateAsync(
                    host, prepared.Candidate).ConfigureAwait(false);
            if (cleanup is not null)
                return new BrowserWorkspaceConstructionResult.Failed(cleanup);
        }
        return result;
    }

    async Task<BrowserWorkspaceConstructionResult> PrepareProjectCandidateAsync(
        BrowserWorkspaceRealizationHost host,
        BrowserRetainedWorkspaceActivationIntent intent,
        BrowserRetainedWorkspaceInput.Project input,
        BrowserWorkspaceRealizationCandidate candidate,
        CancellationToken cancellationToken)
    {
        CompleteRestorationExecutionOptions options = _optionsFactory();
        NavigationOperationInitialization navigation;
        using (WorkspaceRealizationConstructionLease construction = candidate.EnterConstruction())
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceScopeReadResult read = await construction.Workspace
                .GetScopeSnapshotAsync().ConfigureAwait(false);
            if (read is not WorkspaceScopeReadResult.Available available)
            {
                var unavailable = (WorkspaceScopeReadResult.Unavailable)read;
                return ProjectFailure($"The candidate Project Scope is unavailable: {unavailable.RuntimeFailure}.");
            }

            WorkspaceProjectOccurrence project = available.Snapshot.Project
                ?? throw new InvalidOperationException("The Project candidate omitted its Project occurrence.");
            StructuralSubjectIdentity subject = StructuralSubjectIdentity.ForProject(
                StructuralSubjectIdentity.ForWorkspace(candidate.Realization), project);
            NavigationRestorationPreparationResult preparation =
                NavigationTransitions.PrepareRestoration(
                    candidate.Realization,
                    new(available.Snapshot, Package: null, options.FacetAvailability),
                    options.Facets,
                    new(subject));
            if (preparation is not NavigationRestorationPreparationResult.Prepared ready)
            {
                string message = preparation switch
                {
                    NavigationRestorationPreparationResult.Unavailable unavailable => unavailable.Message,
                    NavigationRestorationPreparationResult.Failed failed => failed.Message,
                    NavigationRestorationPreparationResult.Rejected rejectedNavigation => rejectedNavigation.Message,
                    _ => throw new InvalidOperationException("Unknown Project Navigation preparation result."),
                };
                return ProjectFailure($"The candidate Project Navigation could not be prepared: {message}");
            }
            navigation = ready.Initialization;
        }

        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceRealizationCandidateCompletionResult completion =
            await host.CompleteCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (completion is WorkspaceRealizationCandidateCompletionResult.Rejected rejected)
        {
            return ProjectFailure(
                $"Browser Project completion was rejected: {rejected.Reason}"
                + (rejected.RuntimeFailure is null ? "." : $": {rejected.RuntimeFailure}."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        BrowserRetainedWorkspaceActivationRequest request = intent.Request;
        var posting = new BrowserRetainedWorkspacePostingDraft(
            request.RetainedDefinitionId,
            request.Label,
            request.CanonicalLocation,
            new BrowserRetainedWorkspaceOrigin.Project(input),
            navigation,
            [],
            [],
            BrowserRetainedNavigationPreparation.Create(options));
        return new BrowserWorkspaceConstructionResult.Prepared(
            new(host, candidate, intent.Identity, posting));
    }

    static BrowserWorkspaceConstructionResult.Failed ProjectFailure(string message) =>
        new(new CompleteRestorationFailure.HostConstructionFailed(message));
}
