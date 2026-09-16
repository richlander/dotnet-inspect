namespace DotnetInspector.Queries;

internal static class NavigationEvaluation
{
    internal static NavigationEvaluationResult Evaluate(
        NavigationEvaluationRequest request, NavigationPreparation preparation, ViewFacetRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(registry);
        if (preparation is not NavigationPreparation.Ready ready)
        {
            NavigationConsumerOutcome outcome = preparation switch
            {
                NavigationPreparation.Unavailable result => new(NavigationOutcomeKind.Unavailable, Message: result.Message),
                NavigationPreparation.Failed result => new(NavigationOutcomeKind.Failed,
                    FailureSource: NavigationFailureSource.Preparation, Message: result.Message),
                NavigationPreparation.Aborted result => new(NavigationOutcomeKind.Aborted,
                    FailureSource: NavigationFailureSource.Prerequisite, Message: result.Message),
                _ => throw new InvalidOperationException("Unknown Navigation preparation."),
            };
            return new(request, request.Basis, outcome);
        }
        NavigationEvaluationFacts facts = ready.Facts;
        ValidateFacts(
            request.Workspace,
            request.Basis,
            facts,
            request.Occurrence,
            request.Operation);
        NavigationEvaluationResult evaluated = request.Operation == NavigationOperationKind.Maintenance
            ? Refresh(request, facts, registry)
            : Activate(request, facts, registry);
        if (ReferenceEquals(evaluated.Snapshot, request.Basis))
            return evaluated;
        return new(request, NavigationSnapshotDetachment.Detach(
                NavigationWorkspaceSnapshotEvaluation.WithDescendantLenses(evaluated.Snapshot, registry, facts.Availability)),
            evaluated.Outcome, evaluated.Resolution, evaluated.Descendant,
            evaluated.IncompleteInventory is { } incomplete ? NavigationSnapshotDetachment.Detach(incomplete) : null);
    }

    internal static void ValidateFacts(
        InspectionWorkspaceIdentity workspace, NavigationWorkspaceSnapshot? basis,
        NavigationEvaluationFacts facts, WorkspacePackageOccurrence? occurrence,
        NavigationOperationKind? operation = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Scope);
        ArgumentNullException.ThrowIfNull(facts.Availability);
        if (facts.Scope.Revision.Workspace != workspace)
            throw new ArgumentException("Navigation facts require the exact Workspace.", nameof(facts));
        if (facts.NonReadyPackage is { } nonReady)
        {
            bool retainedType =
                operation == NavigationOperationKind.RetainedType;
            if (facts.Package is not null || basis is null
                || !retainedType
                    && occurrence != basis.ActiveOccurrence
                || nonReady.Occurrence.Occurrence != occurrence
                || !retainedType
                    && facts.Scope.Revision.Identity
                        != basis.Scope.Revision.Identity
                || !facts.Scope.Packages.Any(row => row.Occurrence == occurrence
                    && ReferenceEquals(row.Realization, nonReady.Occurrence.Realization)))
                throw new ArgumentException("Non-ready facts require unchanged exact occurrence membership.", nameof(facts));
            return;
        }
        if (facts.Package is { } package)
        {
            if (package.Occurrence.Occurrence != occurrence
                || !facts.Scope.Packages.Any(row => row.Occurrence == occurrence
                    && ReferenceEquals(row.Realization, package.Occurrence.Realization)))
                throw new ArgumentException("Package facts must join the requested occurrence and Scope realization.", nameof(facts));
        }
        else if (occurrence is not null && facts.Scope.Packages.Any(row => row.Occurrence == occurrence))
        {
            throw new ArgumentException("A retained occurrence requires prepared Package facts or an explicit unsettled outcome.", nameof(facts));
        }
    }

    static NavigationEvaluationResult Refresh(
        NavigationEvaluationRequest request, NavigationEvaluationFacts facts, ViewFacetRegistry registry)
    {
        NavigationWorkspaceRefreshResult refreshed = NavigationWorkspaceSnapshotEvaluation.Refresh(
            request.Basis, facts.Scope, facts.Package, registry, facts.Availability, facts.NonReadyPackage);
        return new(request, refreshed.Snapshot,
            refreshed.IncompleteInventory is not null ? IncompleteInventoryOutcome() : SnapshotOutcome(refreshed.Snapshot),
            incompleteInventory: refreshed.IncompleteInventory is { } incomplete
                ? NavigationSnapshotDetachment.Detach(incomplete) : null);
    }

    static NavigationEvaluationResult Activate(
        NavigationEvaluationRequest request, NavigationEvaluationFacts facts, ViewFacetRegistry registry)
    {
        NavigationWorkspaceSnapshot basis = request.Basis;
        NavigationActionTarget target = request.Target!;
        if (target.Action.Kind == NavigationOperationKind.RetainedType)
            return ActivateRetainedType(request, facts, registry, target);
        if (target.Action.Kind == NavigationOperationKind.Package)
        {
            if (facts.Package is null)
                return new(request, basis, new(NavigationOutcomeKind.Unavailable, Message: "The requested occurrence is no longer available."));
            NavigationWorkspaceSnapshot selected = NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest { Scope = facts.Scope, Package = facts.Package }, registry, facts.Availability);
            return new(request, selected, SnapshotOutcome(selected));
        }
        NavigationWorkspaceRefreshResult refresh = NavigationWorkspaceSnapshotEvaluation.Refresh(
            basis, facts.Scope, facts.Package, registry, facts.Availability, facts.NonReadyPackage);
        if (refresh.IncompleteInventory is { } incomplete)
            return new(request, basis, IncompleteInventoryOutcome(), incompleteInventory: NavigationSnapshotDetachment.Detach(incomplete));
        NavigationWorkspaceSnapshot current = refresh.Snapshot;
        if (facts.NonReadyPackage is not null)
        {
            return target.Subject == current.Workspace && target.Action.Kind == NavigationOperationKind.Subject
                ? new(request, NavigationWorkspaceSnapshotEvaluation.WithSubject(
                    current, target.Subject, registry, facts.Availability), new(NavigationOutcomeKind.Applied))
                : new(request, current, NonReadyOutcome(facts.NonReadyPackage.Occurrence.Realization.Status));
        }
        if (target.Action.Kind == NavigationOperationKind.Lens)
        {
            NavigationLensActivationResult lens = NavigationLensActivation.Activate(
                basis.ActiveSubject, target.Lens!, registry, facts.Availability(target.Subject, current.Inventory));
            NavigationLensOutcome? outcome = lens switch
            {
                NavigationLensActivationResult.Applied applied => applied.Outcome,
                NavigationLensActivationResult.Unavailable unavailable => unavailable.Outcome,
                NavigationLensActivationResult.Failed failed => failed.Outcome,
                _ => null,
            };
            if (current.ActiveSubject != target.Subject)
            {
                return new(request, current, lens is NavigationLensActivationResult.Applied
                    ? ActivationOutcome(lens) with
                    {
                        Kind = NavigationOutcomeKind.Failed,
                        FailureSource = NavigationFailureSource.Preparation,
                        Message = "The exact lens resolved, but its subject did not survive reconciliation.",
                    } : ActivationOutcome(lens), lens);
            }
            return new(request, outcome is null ? basis : NavigationWorkspaceSnapshotEvaluation.WithLensOutcome(
                current, outcome, registry, facts.Availability), ActivationOutcome(lens), lens);
        }
        if (!NavigationWorkspaceSnapshotEvaluation.SubjectExists(current, target.Subject))
            return new(request, current, new(NavigationOutcomeKind.Unavailable, Message: "The exact requested subject is no longer available."));
        if (target.Action.Kind == NavigationOperationKind.Subject)
        {
            NavigationWorkspaceSnapshot selected = NavigationWorkspaceSnapshotEvaluation.WithSubject(
                current, target.Subject, registry, facts.Availability);
            return new(request, selected, SnapshotOutcome(selected));
        }
        if (target.Action.Kind == NavigationOperationKind.DescendantLens)
        {
            NavigationDescendantLensResult result = NavigationDescendantLensEvaluation.Evaluate(
                current, new(target.Source, target.Lens!), registry, facts.Availability);
            return result switch
            {
                NavigationDescendantLensResult.Applied applied =>
                    new(request, applied.Snapshot, ActivationOutcome(applied.Activation), applied.Activation, result.Request),
                NavigationDescendantLensResult.Unavailable unavailable =>
                    new(request, basis, ActivationOutcome(unavailable.Activation), unavailable.Activation, result.Request),
                NavigationDescendantLensResult.Failed failed =>
                    new(request, basis, ActivationOutcome(failed.Activation), failed.Activation, result.Request),
                NavigationDescendantLensResult.Rejected rejected =>
                    new(request, basis, rejected.Activation is { } activation ? ActivationOutcome(activation)
                        : new(NavigationOutcomeKind.Rejected, rejected.Validation switch
                        {
                            NavigationDescendantLensRejectionKind.SourceMismatch => NavigationRejectionKind.SourceMismatch,
                            NavigationDescendantLensRejectionKind.ForeignWorkspace => NavigationRejectionKind.ForeignWorkspace,
                            NavigationDescendantLensRejectionKind.ForeignOccurrence => NavigationRejectionKind.ForeignOccurrence,
                            _ => NavigationRejectionKind.NonDescendant,
                        }), rejected.Activation, result.Request),
                _ => throw new InvalidOperationException("Unknown descendant evaluation result."),
            };
        }
        throw new InvalidOperationException("Unknown admitted Navigation action.");
    }

    static NavigationEvaluationResult ActivateRetainedType(
        NavigationEvaluationRequest request,
        NavigationEvaluationFacts facts,
        ViewFacetRegistry registry,
        NavigationActionTarget target)
    {
        NavigationWorkspaceSnapshot basis = request.Basis;
        var type =
            (StructuralSubjectIdentity.TypeSubject)target.Subject;
        if (facts.NonReadyPackage is { } nonReady)
        {
            return new(
                request,
                basis,
                NonReadyOutcome(nonReady.Occurrence.Realization.Status));
        }
        if (facts.Package is null)
        {
            return new(
                request,
                basis,
                new(
                    NavigationOutcomeKind.Unavailable,
                    Message:
                        "The requested Package occurrence is no longer "
                        + "available."));
        }

        var package = StructuralSubjectIdentity.ForPackage(
            basis.Workspace,
            facts.Package.Occurrence.Occurrence);
        if (type.Library.Package != package)
        {
            return new(
                request,
                basis,
                new(
                    NavigationOutcomeKind.Rejected,
                    NavigationRejectionKind.ForeignOccurrence,
                    Message:
                        "The requested Type does not belong to the prepared "
                        + "Package occurrence."));
        }

        NavigationSubjectInventory inventory =
            NavigationWorkspaceSnapshotEvaluation.ClassifySubjectInventory(
                package,
                facts.Package);
        NavigationLibraryInventory? library =
            inventory.Libraries.SingleOrDefault(
                candidate => candidate.Subject == type.Library);
        if (library is null)
        {
            return new(
                request,
                basis,
                new(
                    NavigationOutcomeKind.Rejected,
                    NavigationRejectionKind.ForeignLibrary,
                    Message:
                        "The requested Library does not belong to the exact "
                        + "Package occurrence."));
        }

        int matches = library.Types.Rows.Count(
            candidate => candidate.Subject == type);
        if (matches > 1)
        {
            return new(
                request,
                basis,
                new(
                    NavigationOutcomeKind.Ambiguous,
                    Message:
                        "The exact Type identity occurs more than once in "
                        + "the requested Library."));
        }
        if (matches == 0)
        {
            bool incomplete = !library.Types.Evidence.IsEmpty;
            return new(
                request,
                basis,
                new(
                    incomplete
                        ? NavigationOutcomeKind.Failed
                        : NavigationOutcomeKind.Unavailable,
                    FailureSource:
                        incomplete
                            ? NavigationFailureSource.Preparation
                            : null,
                    Message:
                        incomplete
                            ? "Incomplete inventory cannot establish the "
                                + "requested Type."
                            : "The exact requested Type is no longer "
                                + "available."),
                incompleteInventory:
                    incomplete ? library.Types : null);
        }

        var context = new NavigationRetainedSubjectContext(
            type.Library.Package,
            type.Library,
            type);
        NavigationWorkspaceSnapshot selected =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = facts.Scope,
                    Package = facts.Package,
                    ActiveSubject = type,
                    RetainedContext = context,
                },
                registry,
                facts.Availability);
        return new(
            request,
            selected,
            SnapshotOutcome(selected));
    }

    internal static NavigationConsumerOutcome SnapshotOutcome(NavigationWorkspaceSnapshot snapshot) =>
        snapshot.LensOutcome is NavigationLensOutcome.Suspended suspended ? NonReadyOutcome(suspended.Realization)
            : new(NavigationConsumerProjection.OutcomeKind(snapshot.LensOutcome),
                FailureSource: snapshot.LensOutcome is NavigationLensOutcome.Failed failed
                    ? failed.Failure is NavigationLensFailure.Policy ? NavigationFailureSource.Policy : NavigationFailureSource.Registry
                    : null);

    static NavigationConsumerOutcome NonReadyOutcome(ArtifactRootRealizationStatus status) =>
        new(status is ArtifactRootRealizationStatus.Failed ? NavigationOutcomeKind.Failed : NavigationOutcomeKind.Unavailable,
            FailureSource: NavigationFailureSource.Preparation,
            Message: status is ArtifactRootRealizationStatus.Failed failed
                ? $"The retained Package realization failed: {failed.Failure}." : "The retained Package realization is pending.");

    static NavigationConsumerOutcome IncompleteInventoryOutcome() =>
        new(NavigationOutcomeKind.Failed, FailureSource: NavigationFailureSource.Preparation,
            Message: "Incomplete inventory cannot establish whether the retained subject still exists.");

    static NavigationConsumerOutcome ActivationOutcome(NavigationLensActivationResult activation)
    {
        (NavigationOutcomeKind kind, NavigationLensEvaluationBasis.ExactRequest? basis) = activation switch
        {
            NavigationLensActivationResult.Applied result =>
                (NavigationOutcomeKind.Applied, (NavigationLensEvaluationBasis.ExactRequest)result.Outcome.Basis),
            NavigationLensActivationResult.Unavailable result =>
                (NavigationOutcomeKind.Unavailable, (NavigationLensEvaluationBasis.ExactRequest)result.Outcome.Basis),
            NavigationLensActivationResult.Failed result =>
                (NavigationOutcomeKind.Failed, (NavigationLensEvaluationBasis.ExactRequest)result.Outcome.Basis),
            NavigationLensActivationResult.Rejected { Rejection: NavigationLensRejection.Registry rejected } =>
                (NavigationOutcomeKind.Rejected, rejected.Basis),
            NavigationLensActivationResult.Rejected => (NavigationOutcomeKind.Rejected, null),
            _ => throw new InvalidOperationException("Unknown lens activation result."),
        };
        NavigationConsumerResolution? resolution = basis is null ? null : NavigationConsumerProjection.Resolution(basis.Result);
        return new(kind, kind == NavigationOutcomeKind.Rejected ? NavigationRejectionKind.Registry : null,
            kind == NavigationOutcomeKind.Failed ? NavigationFailureSource.Registry : null,
            resolution?.Message, Resolution: resolution);
    }
}
