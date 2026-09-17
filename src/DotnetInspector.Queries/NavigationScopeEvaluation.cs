namespace DotnetInspector.Queries;

internal static class NavigationScopeEvaluation
{
    internal static NavigationScopeEvaluationResult Evaluate(
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeOperationResult settlement,
        NavigationScopePreparation preparation,
        ViewFacetRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(registry);
        if (!ReferenceEquals(request.Association, settlement.Association))
        {
            throw new ArgumentException(
                "Protected Navigation evaluation requires the exact Scope association.",
                nameof(settlement));
        }

        NavigationConsumerScopeOutcome scopeOutcome = ScopeOutcome(settlement);
        if (settlement is WorkspaceScopeOperationResult.Unavailable unavailable)
        {
            if (preparation is not NavigationScopePreparation.Historical)
            {
                throw new ArgumentException(
                    "An unavailable Scope settlement requires historical-only preparation.",
                    nameof(preparation));
            }
            WorkspaceScopeSnapshot historical =
                unavailable.LastSnapshot ?? request.Basis.Scope;
            NavigationWorkspaceSnapshot historicalSnapshot =
                NavigationWorkspaceSnapshotEvaluation
                    .WithScopePreparationBoundary(
                        request.Basis,
                        historical,
                        selectedOccurrence: null,
                        NavigationDescriptorState.Unavailable,
                        registry);
            return new(
                request,
                settlement,
                NavigationSnapshotDetachment.Detach(historicalSnapshot),
                new(
                    NavigationScopeSnapshotKind.Historical,
                    unavailable.RuntimeFailure),
                new(
                    NavigationOutcomeKind.Unavailable,
                    FailureSource: NavigationFailureSource.Prerequisite,
                    Message:
                        $"Workspace Scope is unavailable: "
                        + $"{unavailable.RuntimeFailure}.",
                    Scope: scopeOutcome));
        }
        if (preparation is NavigationScopePreparation.Historical)
        {
            throw new ArgumentException(
                "A current Scope settlement cannot be prepared as historical evidence.",
                nameof(preparation));
        }

        WorkspaceScopeSnapshot scope = CurrentSnapshot(settlement);
        NavigationInitialization? requested = Initialization(preparation);
        WorkspacePackageOccurrence? selected =
            SelectOccurrence(request, settlement, scope, requested);
        NavigationWorkspaceSnapshot snapshot;
        NavigationConsumerOutcome preparationOutcome;
        NavigationTypeInventoryOutcome? incompleteInventory = null;
        NavigationLensActivationResult? resolution = null;
        switch (preparation)
        {
            case NavigationScopePreparation.Ready ready:
                if (!ReferenceEquals(ready.Facts.Scope, scope))
                {
                    throw new ArgumentException(
                        "Ready Navigation facts must consume the exact complete Scope result snapshot.",
                        nameof(preparation));
                }
                NavigationInitialization initialization =
                    EffectiveInitialization(
                        request,
                        selected,
                        ready.Initialization,
                        settlement is WorkspaceScopeOperationResult.Committed
                            or WorkspaceScopeOperationResult.NoEffect
                            && request.Association.HasExplicitTarget);
                ValidateSelectedFacts(selected, ready.Facts);
                NavigationRestorationPreparationResult restored =
                    NavigationTransitions.PrepareRestoration(
                        request.Workspace,
                        ready.Facts,
                        registry,
                        initialization);
                switch (restored)
                {
                    case NavigationRestorationPreparationResult.Prepared prepared:
                        snapshot =
                            prepared.Initialization.State.InstalledSnapshot;
                        resolution =
                            prepared.Initialization.Result
                                .LensResolution?.Activation;
                        preparationOutcome =
                            NavigationEvaluation.SnapshotOutcome(snapshot);
                        break;
                    case NavigationRestorationPreparationResult.Unavailable result:
                        snapshot = Boundary(
                            NavigationDescriptorState.Unavailable);
                        preparationOutcome = new(
                            NavigationOutcomeKind.Unavailable,
                            FailureSource:
                                NavigationFailureSource.Preparation,
                            Message: result.Message);
                        break;
                    case NavigationRestorationPreparationResult.Failed result:
                        snapshot = Boundary(
                            NavigationDescriptorState.Failed);
                        incompleteInventory =
                            result.Inventory is null
                                ? null
                                : NavigationSnapshotDetachment.Detach(
                                    result.Inventory);
                        preparationOutcome = new(
                            NavigationOutcomeKind.Failed,
                            FailureSource:
                                NavigationFailureSource.Preparation,
                            Message: result.Message);
                        break;
                    case NavigationRestorationPreparationResult.Rejected result:
                        snapshot = Boundary(
                            NavigationDescriptorState.Failed);
                        resolution = result.LensResolution;
                        preparationOutcome = new(
                            NavigationOutcomeKind.Failed,
                            NavigationRejectionKind.ScopeOperation,
                            NavigationFailureSource.Preparation,
                            result.Message);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown Navigation restoration preparation.");
                }
                break;
            case NavigationScopePreparation.Unavailable result:
                snapshot = Boundary(
                    NavigationDescriptorState.Unavailable);
                preparationOutcome = new(
                    NavigationOutcomeKind.Unavailable,
                    FailureSource: NavigationFailureSource.Preparation,
                    Message: result.Message);
                break;
            case NavigationScopePreparation.Failed result:
                snapshot = Boundary(NavigationDescriptorState.Failed);
                preparationOutcome = new(
                    NavigationOutcomeKind.Failed,
                    FailureSource: NavigationFailureSource.Preparation,
                    Message: result.Message);
                break;
            case NavigationScopePreparation.Aborted result:
                snapshot = Boundary(
                    NavigationDescriptorState.Unavailable);
                preparationOutcome = new(
                    NavigationOutcomeKind.Aborted,
                    FailureSource: NavigationFailureSource.Prerequisite,
                    Message: result.Message);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown protected Navigation preparation.");
        }

        NavigationConsumerOutcome outcome =
            preparationOutcome.Kind is NavigationOutcomeKind.Applied
                ? SettlementOutcome(settlement, scopeOutcome)
                : preparationOutcome with { Scope = scopeOutcome };
        return new(
            request,
            settlement,
            NavigationSnapshotDetachment.Detach(snapshot),
            new(NavigationScopeSnapshotKind.Current),
            outcome,
            incompleteInventory,
            resolution);

        NavigationWorkspaceSnapshot Boundary(
            NavigationDescriptorState state) =>
            NavigationWorkspaceSnapshotEvaluation
                .WithScopePreparationBoundary(
                    request.Basis,
                    scope,
                    selected,
                    state,
                    registry);
    }

    static NavigationInitialization? Initialization(
        NavigationScopePreparation preparation) =>
        preparation switch
        {
            NavigationScopePreparation.Ready ready =>
                ready.Initialization,
            NavigationScopePreparation.Unavailable unavailable =>
                unavailable.Initialization,
            NavigationScopePreparation.Failed failed =>
                failed.Initialization,
            NavigationScopePreparation.Aborted aborted =>
                aborted.Initialization,
            _ => null,
        };

    static WorkspaceScopeSnapshot CurrentSnapshot(
        WorkspaceScopeOperationResult settlement) =>
        settlement switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            WorkspaceScopeOperationResult.Rejected rejected =>
                rejected.Snapshot,
            WorkspaceScopeOperationResult.Failed failed =>
                failed.Snapshot,
            WorkspaceScopeOperationResult.Cancelled cancelled =>
                cancelled.Snapshot,
            WorkspaceScopeOperationResult.Superseded superseded =>
                superseded.Snapshot,
            _ => throw new ArgumentException(
                "The Scope settlement does not carry current membership.",
                nameof(settlement)),
        };

    static WorkspacePackageOccurrence? SelectOccurrence(
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeOperationResult settlement,
        WorkspaceScopeSnapshot scope,
        NavigationInitialization? initialization)
    {
        WorkspacePackageOccurrenceDescriptor? requested =
            settlement switch
            {
                WorkspaceScopeOperationResult.Committed committed =>
                    committed.RequestedOccurrence,
                WorkspaceScopeOperationResult.NoEffect noEffect =>
                    noEffect.RequestedOccurrence,
                _ => null,
            };
        if (request.Association.HasExplicitTarget
            && settlement is WorkspaceScopeOperationResult.Committed
                or WorkspaceScopeOperationResult.NoEffect)
        {
            return (requested
                ?? throw new InvalidOperationException(
                    "A successful explicit Scope result omitted its requested occurrence."))
                .Occurrence;
        }

        WorkspacePackageOccurrence? active = request.Basis.ActiveOccurrence;
        if (active is not null
            && scope.Packages.Any(
                candidate => candidate.Occurrence == active))
        {
            return active;
        }

        WorkspacePackageOccurrence? successor =
            initialization?.Context?.Package.Occurrence;
        if (successor is not null)
        {
            if (!scope.Packages.Any(
                    candidate => candidate.Occurrence == successor))
            {
                throw new ArgumentException(
                    "The Navigation-authorized successor is not present in the exact Scope result.",
                    nameof(initialization));
            }
            return successor;
        }
        return null;
    }

    static NavigationInitialization EffectiveInitialization(
        NavigationScopeEvaluationRequest request,
        WorkspacePackageOccurrence? selected,
        NavigationInitialization? initialization,
        bool explicitActivation)
    {
        NavigationLensIdentity? retainedLens =
            request.Basis.LensOutcome.Basis
                is NavigationLensEvaluationBasis.ExactRequest retained
                ? retained.Request
                : null;
        if (selected is null)
        {
            if (initialization?.Context is not null
                || initialization?.Subject is not null
                    && initialization.Subject != request.Basis.Workspace)
            {
                throw new ArgumentException(
                    "Workspace fallback cannot retain a Package descendant.",
                    nameof(initialization));
            }
            NavigationLensIdentity? workspaceLens = initialization?.Lens;
            if (initialization is null
                && request.Basis.ActiveSubject == request.Basis.Workspace)
            {
                workspaceLens = retainedLens;
            }
            return new NavigationInitialization(
                request.Basis.Workspace,
                Lens: workspaceLens);
        }

        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectIdentity.ForPackage(
                request.Basis.Workspace,
                selected);
        if (initialization is null
            && !explicitActivation
            && selected == request.Basis.ActiveOccurrence)
        {
            return new(
                request.Basis.ActiveSubject,
                request.Basis.RetainedContext
                    ?? new NavigationRetainedSubjectContext(package),
                retainedLens);
        }
        if (initialization?.Context is { } context
            && context.Package.Occurrence != selected)
        {
            throw new ArgumentException(
                "Prepared Navigation context must bind the selected Scope occurrence.",
                nameof(initialization));
        }
        return initialization
            ?? new NavigationInitialization(
                Context: new NavigationRetainedSubjectContext(package));
    }

    static void ValidateSelectedFacts(
        WorkspacePackageOccurrence? selected,
        NavigationEvaluationFacts facts)
    {
        WorkspacePackageOccurrence? prepared =
            facts.Package?.Occurrence.Occurrence
            ?? facts.NonReadyPackage?.Occurrence.Occurrence;
        if (selected is null)
        {
            if (prepared is not null)
            {
                throw new ArgumentException(
                    "Workspace fallback cannot consume Package preparation facts.",
                    nameof(facts));
            }
            return;
        }
        if (prepared != selected)
        {
            throw new ArgumentException(
                "Prepared Navigation facts must bind the exact selected Scope occurrence.",
                nameof(facts));
        }
    }

    static NavigationConsumerScopeOutcome ScopeOutcome(
        WorkspaceScopeOperationResult settlement) =>
        settlement switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                new(
                    NavigationScopeSettlementKind.Committed,
                    committed.Association.Kind),
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                new(
                    NavigationScopeSettlementKind.NoEffect,
                    noEffect.Association.Kind),
            WorkspaceScopeOperationResult.Rejected rejected =>
                new(
                    NavigationScopeSettlementKind.Rejected,
                    rejected.Association.Kind,
                    Rejection: rejected.Reason),
            WorkspaceScopeOperationResult.Failed failed =>
                new(
                    NavigationScopeSettlementKind.Failed,
                    failed.Association.Kind,
                    Failure: failed.Failure),
            WorkspaceScopeOperationResult.Cancelled cancelled =>
                new(
                    NavigationScopeSettlementKind.Cancelled,
                    cancelled.Association.Kind),
            WorkspaceScopeOperationResult.Superseded superseded =>
                new(
                    NavigationScopeSettlementKind.Superseded,
                    superseded.Association.Kind),
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                new(
                    NavigationScopeSettlementKind.Unavailable,
                    unavailable.Association.Kind,
                    Failure: unavailable.RuntimeFailure),
            _ => throw new InvalidOperationException(
                "Unknown Scope settlement."),
        };

    static NavigationConsumerOutcome SettlementOutcome(
        WorkspaceScopeOperationResult settlement,
        NavigationConsumerScopeOutcome scope) =>
        settlement switch
        {
            WorkspaceScopeOperationResult.Committed
                or WorkspaceScopeOperationResult.NoEffect =>
                    new(NavigationOutcomeKind.Applied, Scope: scope),
            WorkspaceScopeOperationResult.Rejected rejected =>
                new(
                    NavigationOutcomeKind.Rejected,
                    NavigationRejectionKind.ScopeOperation,
                    Message:
                        $"Workspace Scope rejected the operation: "
                        + $"{rejected.Reason}.",
                    Scope: scope),
            WorkspaceScopeOperationResult.Failed failed =>
                new(
                    NavigationOutcomeKind.Failed,
                    FailureSource: NavigationFailureSource.Prerequisite,
                    Message:
                        $"Workspace Scope failed the operation: "
                        + $"{failed.Failure}.",
                    Scope: scope),
            WorkspaceScopeOperationResult.Cancelled =>
                new(
                    NavigationOutcomeKind.Aborted,
                    FailureSource: NavigationFailureSource.Prerequisite,
                    Message: "Workspace Scope cancelled the operation.",
                    Scope: scope),
            WorkspaceScopeOperationResult.Superseded =>
                new(
                    NavigationOutcomeKind.Superseded,
                    Message:
                        "Workspace Scope superseded the operation with a "
                        + "different operation.",
                    Scope: scope),
            _ => throw new InvalidOperationException(
                "Unknown current Scope settlement."),
        };
}
