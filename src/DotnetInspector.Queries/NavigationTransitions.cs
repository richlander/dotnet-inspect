using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Stateless Navigation transitions. Hosts serialize current-slot commits and
/// execute issued work under their own operation/resource authority outside that
/// short critical section. No callback, task, or service is retained here.
/// </summary>
public static class NavigationTransitions
{
    public static NavigationOperationInitialization Initialize(
        InspectionWorkspaceIdentity workspace,
        NavigationEvaluationFacts facts,
        ViewFacetRegistry registry,
        NavigationInitialization? initialization = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(registry);
        if (initialization?.Lens is not null)
        {
            throw new ArgumentException(
                "Exact-lens initialization must use canonical restoration "
                    + "preparation.",
                nameof(initialization));
        }
        NavigationEvaluation.ValidateFacts(workspace, null, facts, facts.Package?.Occurrence.Occurrence);
        NavigationWorkspaceSnapshot snapshot = NavigationWorkspaceSnapshotEvaluation.Evaluate(
            new NavigationWorkspaceSnapshotRequest
            {
                Scope = facts.Scope,
                Package = facts.Package,
                ActiveSubject = initialization?.Subject,
                RetainedContext = initialization?.Context,
            }, registry, facts.Availability);
        return Initialize(snapshot, registry, facts.Availability);
    }

    /// <summary>
    /// Prepares one complete Navigation state inside a fresh unpublished
    /// Workspace. Non-prepared results carry no state or effect authority.
    /// </summary>
    public static NavigationRestorationPreparationResult PrepareRestoration(
        InspectionWorkspaceIdentity workspace,
        NavigationEvaluationFacts facts,
        ViewFacetRegistry registry,
        NavigationInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(facts.Scope);
        ArgumentNullException.ThrowIfNull(facts.Availability);
        if (facts.Package is not null
            && facts.NonReadyPackage is not null)
        {
            throw new ArgumentException(
                "Navigation restoration facts cannot be both prepared and "
                    + "non-ready.",
                nameof(facts));
        }
        if (facts.Scope.Revision.Workspace != workspace)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.InvalidContext,
                "Navigation restoration facts must belong to the exact "
                    + "Workspace.");
        }
        if ((initialization.Subject is { } requestedSubject
                && requestedSubject.Workspace.Identity != workspace)
            || (initialization.Context is { } requestedContext
                && requestedContext.Package.Workspace.Identity != workspace))
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.InvalidContext,
                "Navigation restoration identities must belong to the exact "
                    + "Workspace.");
        }
        if (initialization.Lens is not null
            && initialization.Subject is null)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.LensRequiresSubject,
                "An exact restoration lens requires an explicit subject.");
        }
        if (initialization.Lens is { } lens
            && lens.Subject != initialization.Subject)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.LensSubjectMismatch,
                "The exact restoration lens must bind the requested subject.");
        }
        StructuralSubjectIdentity.WorkspaceSubject workspaceSubject =
            StructuralSubjectIdentity.ForWorkspace(workspace);
        if (initialization.Subject is null
            && initialization.Context?.Library is not null)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.SubjectOutsideContext,
                "A subject-less restoration can retain only Package "
                    + "context.");
        }
        if (initialization.Subject is { } subjectWithoutContext
            && subjectWithoutContext != workspaceSubject
            && initialization.Context is null)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.InvalidContext,
                "A non-Workspace restoration subject requires its exact "
                    + "retained occurrence context.");
        }
        if (initialization.Subject is { } retainedSubject
            && retainedSubject != workspaceSubject
            && initialization.Context is { } retainedPath
            && retainedSubject != retainedPath.Package
            && retainedSubject != retainedPath.Library
            && retainedSubject != retainedPath.Type
            && retainedSubject != retainedPath.Member)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.SubjectOutsideContext,
                "A non-Workspace active subject must equal one retained "
                    + "path node.");
        }
        if (facts.Package is { } prepared
            && !facts.Scope.Packages.Any(
                candidate =>
                    candidate.Occurrence
                        == prepared.Occurrence.Occurrence
                    && ReferenceEquals(
                        candidate.Realization,
                        prepared.Occurrence.Realization)))
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.InvalidContext,
                "Prepared Package facts must join the exact Workspace Scope "
                    + "occurrence and realization.");
        }
        if (facts.NonReadyPackage is { } unsettled
            && !facts.Scope.Packages.Any(
                candidate =>
                    candidate.Occurrence
                        == unsettled.Occurrence.Occurrence
                    && ReferenceEquals(
                        candidate.Realization,
                        unsettled.Occurrence.Realization)))
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                NavigationRestorationRejectionKind.InvalidContext,
                "Non-ready Package facts must join the exact Workspace Scope "
                    + "occurrence and realization.");
        }
        if (initialization.Context is { } context
            && facts.Package is null
            && facts.NonReadyPackage is null)
        {
            WorkspacePackageOccurrenceDescriptor? occurrence =
                facts.Scope.Packages.FirstOrDefault(
                    candidate =>
                        candidate.Occurrence
                            == context.Package.Occurrence);
            if (occurrence is null)
            {
                return new NavigationRestorationPreparationResult.Rejected(
                    workspace,
                    initialization,
                    NavigationRestorationRejectionKind.InvalidContext,
                    "The retained Package occurrence is not present in the "
                        + "exact Workspace Scope.");
            }
            return new NavigationRestorationPreparationResult.Failed(
                workspace,
                initialization,
                context.Package,
                NavigationRestorationFailureKind.PackageNotPrepared,
                "The retained Package occurrence has no complete prepared "
                    + "Navigation facts.");
        }
        if (facts.NonReadyPackage is { } nonReady)
        {
            StructuralSubjectIdentity.PackageSubject package =
                StructuralSubjectIdentity.ForPackage(
                    StructuralSubjectIdentity.ForWorkspace(workspace),
                    nonReady.Occurrence.Occurrence);
            if (initialization.Context is not { } retained)
            {
                return new NavigationRestorationPreparationResult.Rejected(
                    workspace,
                    initialization,
                    NavigationRestorationRejectionKind.InvalidContext,
                    "A non-ready Package restoration requires its exact "
                        + "retained occurrence context.");
            }
            if (retained.Package != package)
            {
                return new NavigationRestorationPreparationResult.Rejected(
                    workspace,
                    initialization,
                    NavigationRestorationRejectionKind.InvalidContext,
                    "The retained context must identify the exact non-ready "
                        + "Package occurrence.");
            }
            return new NavigationRestorationPreparationResult.Failed(
                workspace,
                initialization,
                package,
                NavigationRestorationFailureKind.PackageNotPrepared,
                "The retained Package occurrence is not ready for Navigation "
                    + "restoration.");
        }

        NavigationEvaluation.ValidateFacts(
            workspace,
            basis: null,
            facts,
            facts.Package?.Occurrence.Occurrence);
        NavigationRestorationSubjectPreparation subject =
            NavigationWorkspaceSnapshotEvaluation.PrepareRestoration(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = facts.Scope,
                    Package = facts.Package,
                    ActiveSubject = initialization.Subject,
                    RetainedContext = initialization.Context,
                });
        if (subject
            is NavigationRestorationSubjectPreparation.Rejected rejected)
        {
            return new NavigationRestorationPreparationResult.Rejected(
                workspace,
                initialization,
                rejected.Kind,
                rejected.Message);
        }
        if (subject
            is NavigationRestorationSubjectPreparation.Unavailable unavailable)
        {
            return new NavigationRestorationPreparationResult.Unavailable(
                workspace,
                initialization,
                unavailable.Subject,
                unavailable.Message);
        }
        if (subject
            is NavigationRestorationSubjectPreparation.Failed failed)
        {
            return new NavigationRestorationPreparationResult.Failed(
                workspace,
                initialization,
                failed.Subject,
                NavigationRestorationFailureKind.IncompleteInventory,
                failed.Message,
                NavigationSnapshotDetachment.Detach(failed.Inventory!));
        }

        NavigationWorkspaceSnapshotComposition composition =
            ((NavigationRestorationSubjectPreparation.Ready)subject)
                .Composition;
        NavigationLensActivationResult? activation = null;
        NavigationLensOutcome? lensOutcome = null;
        NavigationFacetAvailabilityProvider availability =
            facts.Availability;
        if (initialization.Lens is { } exactLens)
        {
            IViewFacetAvailabilityFacts activeFacts =
                facts.Availability(
                    composition.ActiveSubject,
                    composition.Inventory)
                ?? throw new InvalidOperationException(
                    "The Navigation facet availability provider returned null.");
            activation = NavigationLensActivation.ResolveExact(
                exactLens,
                registry,
                activeFacts);
            if (activation
                is NavigationLensActivationResult.Rejected registryRejection)
            {
                return new NavigationRestorationPreparationResult.Rejected(
                    workspace,
                    initialization,
                    NavigationRestorationRejectionKind.Registry,
                    "The exact restoration lens is unknown or inapplicable.",
                    registryRejection);
            }
            lensOutcome = activation switch
            {
                NavigationLensActivationResult.Applied applied =>
                    applied.Outcome,
                NavigationLensActivationResult.Unavailable lensUnavailable =>
                    lensUnavailable.Outcome,
                NavigationLensActivationResult.Failed lensFailed =>
                    lensFailed.Outcome,
                _ => throw new InvalidOperationException(
                    "Unknown exact restoration lens result."),
            };
            availability = (candidate, inventory) =>
                candidate == composition.ActiveSubject
                    ? activeFacts
                    : facts.Availability(candidate, inventory);
        }

        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Compose(
                composition,
                registry,
                availability,
                lensOutcome);
        return new NavigationRestorationPreparationResult.Prepared(
            workspace,
            initialization,
            Initialize(
                snapshot,
                registry,
                availability,
                activation));
    }

    static NavigationOperationInitialization Initialize(
        NavigationWorkspaceSnapshot snapshot,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        NavigationLensActivationResult? resolution = null)
    {
        snapshot = NavigationWorkspaceSnapshotEvaluation.WithDescendantLenses(
            snapshot,
            registry,
            availability);
        snapshot = NavigationSnapshotDetachment.Detach(snapshot);
        var projection = new NavigationConsumerProjection(
            new(Guid.NewGuid().ToString("N"), 0,
                ImmutableDictionary<StructuralSubjectIdentity, string>.Empty,
                ImmutableDictionary<NavigationLensIdentity, string>.Empty));
        string session = projection.Freeze().Session;
        (NavigationConsumerSnapshot consumer, Dictionary<string, NavigationActionTarget> actions) =
            projection.Build(snapshot, session);
        var state = new NavigationState(new(snapshot, consumer, projection.Freeze(), actions.ToImmutableDictionary()));
        NavigationTransition transition = CurrentResult(
            state, state.Data, new(session, state.Data.Intent, NavigationOperationKind.Initialize),
            NavigationEvaluation.SnapshotOutcome(snapshot),
            resolution);
        return new(transition.State, transition.Result!);
    }

    /// <summary>
    /// Prevents publication of a transition computed from a replaced host slot.
    /// Call while holding the host's existing short state-update gate.
    /// </summary>
    public static bool CanCommit(NavigationState current, NavigationTransition transition) =>
        ReferenceEquals(current, transition.Previous);

    public static NavigationTransition Begin(NavigationState state, NavigationAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        NavigationRejectionKind? rejection = ValidateAction(state, action, out NavigationActionTarget? target);
        NavigationStateData next = BeginExplicit(state.Data);
        var identity = new NavigationRequest(state.Id, action.Id, action.Kind);
        if (rejection is not null)
            return CurrentResult(state, next, identity, new(NavigationOutcomeKind.Rejected, rejection));
        next = next with
        {
            ConsumedActions = next.ConsumedActions.Add(action.Id, action),
            ActionsNeedRenewal = next.ActionsNeedRenewal || target!.Advertised,
        };
        return IssueExplicit(state, next, identity, target!);
    }

    public static NavigationTransition Begin(NavigationState state, string generation, string actionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        NavigationAction action = state.Data.Actions.TryGetValue(actionId, out NavigationActionTarget? target)
            ? target.Action with { Generation = generation }
            : state.Data.ConsumedActions.TryGetValue(actionId, out NavigationAction? consumed)
                ? consumed with { Generation = generation }
                : new(state.Id, generation, actionId, state.Snapshot.ActiveSubject.Id, NavigationOperationKind.Subject);
        return Begin(state, action);
    }

    public static NavigationTransition BeginLens(NavigationState state, NavigationLensIdentity request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        NavigationStateData next = BeginExplicit(state.Data);
        var projection = new NavigationConsumerProjection(next.Projection);
        string id = projection.Token();
        var identity = new NavigationRequest(state.Id, id, NavigationOperationKind.Lens);
        NavigationConsumerRequest consumerRequest = projection.Request(next.Installed, request.Subject, request);
        next = next with { Projection = projection.Freeze() };
        if (request.Subject.Workspace.Identity != state.Workspace || request.Subject != next.Installed.ActiveSubject)
        {
            return CurrentResult(state, next, identity, new(
                NavigationOutcomeKind.Rejected,
                request.Subject.Workspace.Identity != state.Workspace
                    ? NavigationRejectionKind.ForeignWorkspace : NavigationRejectionKind.SourceMismatch,
                Request: consumerRequest));
        }
        var action = new NavigationAction(
            state.Id, next.Consumer.Generation, id, next.Consumer.ActiveSubject.Id, NavigationOperationKind.Lens);
        return IssueExplicit(state, next, identity,
            new(action, next.Installed.ActiveSubject, request.Subject, request, Advertised: false));
    }

    static NavigationStateData BeginExplicit(NavigationStateData state) =>
        state with
        {
            NextIntent = checked(state.NextIntent + 1),
            Explicit = null,
            Effect = null,
            ConsumerInstallation = null,
        };

    static NavigationTransition IssueExplicit(
        NavigationState state, NavigationStateData next, NavigationRequest identity, NavigationActionTarget target)
    {
        var projection = new NavigationConsumerProjection(next.Projection);
        NavigationConsumerRequest request = projection.Request(next.Installed, target.Subject, target.Lens);
        string attempt = projection.Token();
        next = next with { Projection = projection.Freeze() };
        WorkspacePackageOccurrence? occurrence = target.Action.Kind == NavigationOperationKind.Package
            ? ((StructuralSubjectIdentity.PackageSubject)target.Subject).Occurrence
            : next.Installed.ActiveOccurrence;
        var work = new NavigationEvaluationRequest(identity, attempt, next, occurrence, target, request);
        return new(state, next with { Explicit = work }, identity, work);
    }

    public static NavigationTransition QueueMaintenance(NavigationState state) =>
        Queue(state, NavigationOperationKind.Maintenance);

    public static NavigationTransition QueueSynchronization(NavigationState state) =>
        Queue(state, NavigationOperationKind.Synchronize);

    static NavigationTransition Queue(NavigationState state, NavigationOperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(state);
        var projection = new NavigationConsumerProjection(state.Data.Projection);
        var request = new NavigationRequest(state.Id, projection.Token(), kind);
        NavigationStateData next = state.Data with { Projection = projection.Freeze() };
        next = kind == NavigationOperationKind.Maintenance
            ? next with { Maintenance = next.Maintenance.Add(request) }
            : next with { Synchronization = next.Synchronization.Add(request) };
        return new(state, next, request);
    }

    /// <summary>
    /// Issues only the oldest maintenance request, or a dedicated synchronization
    /// result after maintenance drains. Waiting is data, not a retained task.
    /// </summary>
    public static NavigationTransition Advance(NavigationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        NavigationStateData next = state.Data;
        if (next.Explicit is not null || next.Effect is not null || next.MaintenanceAttempt is not null)
            return new(state, next);
        if (!next.Maintenance.IsEmpty)
        {
            NavigationRequest request = next.Maintenance[0];
            var projection = new NavigationConsumerProjection(next.Projection);
            string attempt = projection.Token();
            next = next with { Projection = projection.Freeze() };
            var work = new NavigationEvaluationRequest(
                request, attempt, next, next.Installed.ActiveOccurrence, null, null);
            return new(state, next with { MaintenanceAttempt = work }, request, work);
        }
        if (!next.Synchronization.IsEmpty)
        {
            NavigationRequest request = next.Synchronization[0];
            return CurrentResult(state, next with { Synchronization = next.Synchronization.RemoveAt(0) },
                request, new(NavigationOutcomeKind.Synchronized));
        }
        return new(state, next);
    }

    public static NavigationEvaluationResult Evaluate(
        NavigationEvaluationRequest request,
        NavigationPreparation preparation,
        ViewFacetRegistry registry) =>
        NavigationEvaluation.Evaluate(request, preparation, registry);

    public static NavigationTransition Complete(
        NavigationState state,
        NavigationEvaluationRequest request,
        NavigationEvaluationResult evaluation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evaluation);
        NavigationStateData next = state.Data;
        if (request.Workspace != state.Workspace)
            return new(state, next, rejection: NavigationCompletionRejection.ForeignWorkspace);
        if (request.Identity.Session != state.Id)
            return new(state, next, rejection: NavigationCompletionRejection.ForeignSession);
        if (!ReferenceEquals(request, evaluation.Request))
            return new(state, next, rejection: NavigationCompletionRejection.WrongTicket);
        bool maintenance = request.Operation == NavigationOperationKind.Maintenance;
        if (!maintenance && request.Intent != next.Intent)
        {
            return new(state, next, request.Identity, result: new(
                new(request.Operation, request.Request, next.Consumer,
                    new(NavigationOutcomeKind.Superseded), Disposition(next), null), null));
        }
        if (!ReferenceEquals(request, maintenance ? next.MaintenanceAttempt : next.Explicit))
            return new(state, next, rejection: NavigationCompletionRejection.StaleAttempt);
        if (maintenance && (request.Intent != next.Intent
            || !ReferenceEquals(request.Basis, next.Installed)
            || request.Publication != state.Publication
            || next.Explicit is not null || next.Effect is not null))
        {
            return new(state, next with { MaintenanceAttempt = null }, request.Identity);
        }
        next = maintenance
            ? next with { MaintenanceAttempt = null, Maintenance = next.Maintenance.RemoveAt(0) }
            : next with { Explicit = null };
        if (!NavigationWorkspaceSnapshotEquality.Equals(next.Installed, evaluation.Snapshot))
        {
            var projection = new NavigationConsumerProjection(next.Projection);
            (NavigationConsumerSnapshot consumer, Dictionary<string, NavigationActionTarget> actions) =
                projection.Build(evaluation.Snapshot, state.Id);
            next = next with
            {
                Installed = evaluation.Snapshot,
                Consumer = consumer,
                Projection = projection.Freeze(),
                Actions = actions.ToImmutableDictionary(),
                ActionsNeedRenewal = false,
                NextRevision = checked(next.NextRevision + 1),
            };
        }
        NavigationConsumerOutcome outcome = evaluation.Outcome;
        if (evaluation.IncompleteInventory is { } incomplete)
        {
            var projection = new NavigationConsumerProjection(next.Projection);
            outcome = outcome with { Diagnostics = [.. incomplete.Evidence.Select(projection.Diagnostic)] };
            next = next with { Projection = projection.Freeze() };
        }
        return CurrentResult(state, next, request.Identity,
            outcome with { Request = request.ConsumerRequest }, evaluation.Resolution, evaluation.Descendant);
    }

    /// <summary>
    /// Settles one host-cancelled or unexpectedly faulted request without hiding
    /// the host failure. An admitted explicit prerequisite abort instead completes
    /// with NavigationPreparation.Aborted and ordinary consumer authority.
    /// </summary>
    public static NavigationTransition Cancel(NavigationState state, NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Session != state.Id)
            return new(state, state.Data, rejection: NavigationCompletionRejection.ForeignSession);
        NavigationStateData next = state.Data;
        if (ReferenceEquals(next.Explicit?.Identity, request))
            return new(state, next with { Explicit = null }, request);
        int index = next.Maintenance.IndexOf(request);
        if (index >= 0)
            return new(state, next with
            {
                Maintenance = next.Maintenance.RemoveAt(index),
                MaintenanceAttempt = ReferenceEquals(next.MaintenanceAttempt?.Identity, request)
                    ? null : next.MaintenanceAttempt,
            }, request);
        index = next.Synchronization.IndexOf(request);
        if (index >= 0)
            return new(state, next with { Synchronization = next.Synchronization.RemoveAt(index) }, request);
        return new(state, next, rejection: NavigationCompletionRejection.StaleAttempt);
    }

    public static bool ValidateAuthority(NavigationState state, NavigationEffectAuthority? authority) =>
        authority is not null && state.Data.Effect == authority && state.Data.Explicit is null
        && authority.Session == state.Id && authority.Revision == state.Data.Revision
        && authority.Intent == state.Data.Intent;

    public static NavigationTransition RecordConsumerInstallation(NavigationState state, NavigationEffectAuthority authority) =>
        ValidateAuthority(state, authority)
            ? new(state, state.Data with { ConsumerInstallation = authority }, authorityResult: NavigationAuthorityResult.Accepted)
            : new(state, state.Data, authorityResult: NavigationAuthorityResult.InvalidAuthority);

    public static NavigationTransition Acknowledge(NavigationState state, NavigationEffectAuthority authority)
    {
        if (!ValidateAuthority(state, authority))
            return new(state, state.Data, authorityResult: NavigationAuthorityResult.InvalidAuthority);
        if (state.Data.ConsumerInstallation != authority)
            return new(state, state.Data, authorityResult: NavigationAuthorityResult.InstallationRequired);
        return new(state, state.Data with
        {
            Acknowledged = state.Publication,
            Effect = null,
            ConsumerInstallation = null,
        }, authorityResult: NavigationAuthorityResult.Accepted);
    }

    public static NavigationTransition Abandon(NavigationState state, NavigationEffectAuthority authority) =>
        ValidateAuthority(state, authority)
            ? new(state, state.Data with { Effect = null, ConsumerInstallation = null },
                authorityResult: NavigationAuthorityResult.Accepted)
            : new(state, state.Data, authorityResult: NavigationAuthorityResult.InvalidAuthority);

    static NavigationSynchronizationDisposition Disposition(NavigationStateData state) =>
        state.Acknowledged == new NavigationPublication(state.Revision, state.Consumer.Generation)
            ? NavigationSynchronizationDisposition.Current
            : NavigationSynchronizationDisposition.SynchronizationRequired;

    static NavigationTransition CurrentResult(
        NavigationState previous, NavigationStateData next, NavigationRequest request,
        NavigationConsumerOutcome outcome, NavigationLensActivationResult? resolution = null,
        DescendantSubjectLensRequest? descendant = null)
    {
        var projection = new NavigationConsumerProjection(next.Projection);
        if (next.ActionsNeedRenewal)
        {
            (NavigationConsumerSnapshot consumer, Dictionary<string, NavigationActionTarget> actions) =
                projection.RenewActions(next.Consumer, next.Actions);
            next = next with { Consumer = consumer, Actions = actions.ToImmutableDictionary(), ActionsNeedRenewal = false };
        }
        var authority = new NavigationEffectAuthority(previous.Id, next.Revision, next.Intent, projection.Token());
        next = next with { Effect = authority, ConsumerInstallation = null, Projection = projection.Freeze() };
        var consumerResult = new NavigationConsumerResult(
            request.Operation, request.Id, next.Consumer, outcome, Disposition(next), authority);
        return new(previous, next, request, result: new(consumerResult,
            resolution is null ? null : new(request.Id, request.Operation, authority, resolution, descendant)));
    }

    static NavigationRejectionKind? ValidateAction(
        NavigationState state, NavigationAction action, out NavigationActionTarget? target)
    {
        target = null;
        if (action.Session != state.Id)
            return NavigationRejectionKind.ForeignSession;
        if (state.Data.ConsumedActions.TryGetValue(action.Id, out NavigationAction? consumed) && consumed == action)
            return NavigationRejectionKind.DuplicateAction;
        if (action.Generation != state.Snapshot.Generation)
            return NavigationRejectionKind.StaleGeneration;
        if (!state.Data.Actions.TryGetValue(action.Id, out target))
            return NavigationRejectionKind.UnknownAction;
        if (action.Source != target.Action.Source || target.Source != state.InstalledSnapshot.ActiveSubject)
            return NavigationRejectionKind.SourceMismatch;
        if (action != target.Action)
            return NavigationRejectionKind.InvalidAction;
        if (target.Subject.Workspace.Identity != state.Workspace)
            return NavigationRejectionKind.ForeignWorkspace;
        if (action.Kind == NavigationOperationKind.DescendantLens
            && !NavigationDescendantLensEvaluation.IsEligibleDescendant(state.InstalledSnapshot, target.Source, target.Subject))
            return NavigationRejectionKind.NonDescendant;
        return null;
    }
}

public sealed record NavigationOperationInitialization(NavigationState State, NavigationOperationResult Result);
