using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

public enum NavigationCoordinateSuccessorFailureKind
{
    SameWorkspace,
    ForeignSourceWorkspace,
    ForeignDestinationScope,
    SourceOccurrenceUnavailable,
    SourceBindingMismatch,
    DestinationOccurrenceUnavailable,
    DestinationBindingMismatch,
    SourceRootUnavailable,
    SourceObservationUnavailable,
    DestinationRootUnavailable,
    DestinationObservationUnavailable,
}

public sealed record NavigationCoordinateSuccessorFailure
{
    internal NavigationCoordinateSuccessorFailure(
        NavigationCoordinateSuccessorFailureKind kind,
        string detail,
        ArtifactRootFailure? rootFailure = null,
        CoordinateLibraryPairingFailure? observationFailure = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Kind = kind;
        Detail = detail;
        RootFailure = rootFailure;
        ObservationFailure = observationFailure;
    }

    public NavigationCoordinateSuccessorFailureKind Kind { get; }

    public string Detail { get; }

    public ArtifactRootFailure? RootFailure { get; }

    public CoordinateLibraryPairingFailure? ObservationFailure { get; }
}

/// <summary>
/// Closed result of preparing fresh Navigation state for one explicitly
/// supplied successor Workspace.
/// </summary>
public abstract record NavigationCoordinateSuccessorPreparationResult
{
    private protected NavigationCoordinateSuccessorPreparationResult() { }

    public sealed record Prepared
        : NavigationCoordinateSuccessorPreparationResult
    {
        internal Prepared(
            NavigationOperationInitialization initialization,
            NavigationCoordinateRetentionResult retention)
        {
            ArgumentNullException.ThrowIfNull(initialization);
            ArgumentNullException.ThrowIfNull(retention);
            Initialization = initialization;
            Retention = retention;
        }

        public NavigationOperationInitialization Initialization { get; }

        public NavigationCoordinateRetentionResult Retention { get; }
    }

    public sealed record NotPrepared
        : NavigationCoordinateSuccessorPreparationResult
    {
        internal NotPrepared(
            NavigationRestorationPreparationResult preparation,
            NavigationCoordinateRetentionResult retention)
        {
            ArgumentNullException.ThrowIfNull(preparation);
            ArgumentNullException.ThrowIfNull(retention);
            if (preparation
                is NavigationRestorationPreparationResult.Prepared)
            {
                throw new ArgumentException(
                    "A prepared Navigation result must use the Prepared arm.",
                    nameof(preparation));
            }

            Preparation = preparation;
            Retention = retention;
        }

        public NavigationRestorationPreparationResult Preparation { get; }

        public NavigationCoordinateRetentionResult Retention { get; }
    }

    public sealed record Failed
        : NavigationCoordinateSuccessorPreparationResult
    {
        internal Failed(NavigationCoordinateSuccessorFailure failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public NavigationCoordinateSuccessorFailure Failure { get; }
    }
}

/// <summary>
/// Prepares a fresh Navigation lineage from exact source and successor
/// Workspace evidence without constructing or publishing either Workspace.
/// </summary>
public static class NavigationCoordinateSuccessorQuery
{
    public static async ValueTask<
        NavigationCoordinateSuccessorPreparationResult> PrepareAsync(
            InspectionWorkspace sourceWorkspace,
            NavigationState sourceNavigation,
            PackageRootBinding sourceBinding,
            InspectionWorkspace destinationWorkspace,
            WorkspaceScopeSnapshot destinationScope,
            PackageRootBinding destinationBinding,
            ViewFacetRegistry registry,
            NavigationFacetAvailabilityProvider availability,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(sourceNavigation);
        ArgumentNullException.ThrowIfNull(sourceBinding);
        ArgumentNullException.ThrowIfNull(destinationWorkspace);
        ArgumentNullException.ThrowIfNull(destinationScope);
        ArgumentNullException.ThrowIfNull(destinationBinding);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(
                sourceWorkspace.Identity,
                destinationWorkspace.Identity))
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind.SameWorkspace,
                "Successor preparation requires separately realized source "
                    + "and destination Workspaces.");
        }

        NavigationWorkspaceSnapshot sourceSnapshot =
            sourceNavigation.CurrentSnapshot;
        if (!ReferenceEquals(
                sourceWorkspace.Identity,
                sourceSnapshot.Workspace.Identity))
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .ForeignSourceWorkspace,
                "The source Navigation state belongs to another Workspace.");
        }
        if (!ReferenceEquals(
                destinationWorkspace.Identity,
                destinationScope.Revision.Workspace))
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .ForeignDestinationScope,
                "The destination Scope belongs to another Workspace.");
        }

        if (sourceSnapshot.ActiveOccurrence is not { } active)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .SourceOccurrenceUnavailable,
                "Successor preparation requires one active source Package "
                    + "occurrence.");
        }
        WorkspacePackageOccurrenceDescriptor? sourceOccurrence =
            sourceSnapshot.Scope.Packages.SingleOrDefault(
                candidate => candidate.Occurrence == active);
        if (sourceOccurrence is null)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .SourceOccurrenceUnavailable,
                "The active source occurrence is absent from the source "
                    + "Navigation Scope.");
        }
        if (sourceSnapshot.RetainedContext is null)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .SourceOccurrenceUnavailable,
                "The source Navigation state has no retained Package context.");
        }
        if (!Matches(sourceOccurrence, sourceBinding))
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind.SourceBindingMismatch,
                "The source binding does not identify the active source "
                    + "occurrence and retained content snapshot.");
        }

        WorkspacePackageOccurrenceDescriptor? destinationOccurrence =
            destinationScope.FindPackageOccurrence(destinationBinding);
        if (destinationOccurrence is null)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .DestinationOccurrenceUnavailable,
                "The destination Scope does not contain the requested "
                    + "successor Package occurrence.");
        }
        if (!Matches(destinationOccurrence, destinationBinding))
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .DestinationBindingMismatch,
                "The destination binding does not identify the requested "
                    + "successor occurrence and retained content snapshot.");
        }

        if (sourceOccurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready sourceReady)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind.SourceRootUnavailable,
                "The active source Package Root is not ready.");
        }
        if (destinationOccurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready destinationReady)
        {
            return Failed(
                NavigationCoordinateSuccessorFailureKind
                    .DestinationRootUnavailable,
                "The requested destination Package Root is not ready.");
        }

        ArtifactRootResult<
            NavigationCoordinateSuccessorPreparationResult> sourceAccess =
            await sourceWorkspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    sourceOccurrence.Occurrence.Correspondence,
                sourceReady.Generation,
                async (sourceRealization, token) =>
                {
                    CoordinatePackageObservationResult sourceObservation =
                        CoordinateLibraryPairingQuery.ObserveAdmitted(
                            sourceWorkspace,
                            sourceBinding,
                            sourceOccurrence,
                            sourceRealization,
                            token);
                    if (sourceObservation
                        is CoordinatePackageObservationResult.Unavailable
                            sourceUnavailable)
                    {
                        return Failed(
                            NavigationCoordinateSuccessorFailureKind
                                .SourceObservationUnavailable,
                            "The active source Package could not be observed: "
                                + sourceUnavailable.Failure.Detail,
                            observationFailure: sourceUnavailable.Failure);
                    }

                    ArtifactRootResult<
                        NavigationCoordinateSuccessorPreparationResult>
                        destinationAccess =
                            await destinationWorkspace
                                .ExecutePackageRootQueryAsync(
                                    (PackageArtifactRootCorrespondence)
                                        destinationOccurrence.Occurrence
                                            .Correspondence,
                                    destinationReady.Generation,
                                    async (destinationRealization, innerToken) =>
                                    {
                                        CoordinatePackageObservationResult
                                            destinationObservation =
                                                CoordinateLibraryPairingQuery
                                                    .ObserveAdmitted(
                                                        destinationWorkspace,
                                                        destinationBinding,
                                                        destinationOccurrence,
                                                        destinationRealization,
                                                        innerToken);
                                        if (destinationObservation
                                            is CoordinatePackageObservationResult
                                                .Unavailable
                                                    destinationUnavailable)
                                        {
                                            return
                                                (NavigationCoordinateSuccessorPreparationResult)
                                                Failed(
                                                NavigationCoordinateSuccessorFailureKind
                                                    .DestinationObservationUnavailable,
                                                "The requested destination "
                                                    + "Package could not be "
                                                    + "observed: "
                                                    + destinationUnavailable
                                                        .Failure.Detail,
                                                observationFailure:
                                                    destinationUnavailable
                                                        .Failure);
                                        }

                                        NavigationPackageEvaluation
                                            destinationPackage =
                                                NavigationPackageEvaluationFactory
                                                    .Create(
                                                        destinationOccurrence,
                                                        destinationBinding,
                                                        destinationRealization,
                                                        innerToken);
                                        NavigationCoordinateRetentionResult
                                            retention =
                                                await NavigationCoordinateRetentionPolicy
                                                    .PrepareAsync(
                                                        sourceWorkspace,
                                                        destinationWorkspace,
                                                        sourceSnapshot,
                                                        ((CoordinatePackageObservationResult
                                                            .Available)
                                                            sourceObservation)
                                                            .Observation,
                                                        sourceRealization,
                                                        ((CoordinatePackageObservationResult
                                                            .Available)
                                                            destinationObservation)
                                                            .Observation,
                                                        destinationPackage,
                                                        innerToken)
                                                    .ConfigureAwait(false);
                                        NavigationRestorationPreparationResult
                                            preparation =
                                                NavigationTransitions
                                                    .PrepareRestoration(
                                                        destinationWorkspace
                                                            .Identity,
                                                        new(
                                                            destinationScope,
                                                            destinationPackage,
                                                            availability),
                                                        registry,
                                                        retention
                                                            .Initialization);
                                        return preparation
                                            is NavigationRestorationPreparationResult
                                                .Prepared prepared
                                            ? (NavigationCoordinateSuccessorPreparationResult)
                                                new NavigationCoordinateSuccessorPreparationResult
                                                .Prepared(
                                                    prepared.Initialization,
                                                    retention)
                                            : new NavigationCoordinateSuccessorPreparationResult
                                                .NotPrepared(
                                                    preparation,
                                                    retention);
                                    },
                                    cancellationToken: token)
                                .ConfigureAwait(false);
                    return destinationAccess switch
                    {
                        ArtifactRootResult<
                            NavigationCoordinateSuccessorPreparationResult>
                            .Available available => available.Value,
                        ArtifactRootResult<
                            NavigationCoordinateSuccessorPreparationResult>
                            .Rejected rejected => Failed(
                                NavigationCoordinateSuccessorFailureKind
                                    .DestinationRootUnavailable,
                                "The requested destination Package Root "
                                    + "rejected access.",
                                rootFailure: rejected.Failure),
                        _ => throw new InvalidOperationException(
                            "Unknown destination Root access outcome."),
                    };
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

        return sourceAccess switch
        {
            ArtifactRootResult<
                NavigationCoordinateSuccessorPreparationResult>
                .Available available => available.Value,
            ArtifactRootResult<
                NavigationCoordinateSuccessorPreparationResult>
                .Rejected rejected => Failed(
                    NavigationCoordinateSuccessorFailureKind
                        .SourceRootUnavailable,
                    "The active source Package Root rejected access.",
                    rootFailure: rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown source Root access outcome."),
        };
    }

    static bool Matches(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding) =>
        occurrence.Occurrence.Correspondence
            is PackageArtifactRootCorrespondence correspondence
        && correspondence.Matches(PackageArtifactRootRequest.From(binding))
        && binding.ReferencesRetainedContent();

    static NavigationCoordinateSuccessorPreparationResult.Failed Failed(
        NavigationCoordinateSuccessorFailureKind kind,
        string detail,
        ArtifactRootFailure? rootFailure = null,
        CoordinateLibraryPairingFailure? observationFailure = null) =>
        new(
            new(
                kind,
                detail,
                rootFailure,
                observationFailure));
}
