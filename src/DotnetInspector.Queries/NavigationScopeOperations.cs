using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum NavigationCoordinateRetentionDisposition
{
    ExactPath,
    MemberFallbackToType,
    TypeFallbackToLibrary,
    LibraryFallbackToAggregate,
    PackageFallback,
    ActiveLibraryContainmentTruncated,
}

/// <summary>
/// Detached owner evidence and Navigation's retention decision for one exact
/// Package-coordinate successor.
/// </summary>
public sealed class NavigationCoordinateRetentionResult
{
    internal NavigationCoordinateRetentionResult(
        CoordinatePackageObservation source,
        CoordinatePackageObservation destination,
        NavigationCoordinateRetentionDisposition disposition,
        NavigationInitialization initialization,
        string detail,
        CoordinateLibraryPairingResult? libraryPairing = null,
        ApiCoordinateCorrespondenceResult? typeCorrespondence = null,
        ApiCoordinateCorrespondenceResult? memberCorrespondence = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));

        SourceWorkspace = source.Occurrence.Identity.WorkspaceIdentity;
        DestinationWorkspace = destination.Occurrence.Identity.WorkspaceIdentity;
        Source = source.Occurrence.Package;
        Destination = destination.Occurrence.Package;
        Disposition = disposition;
        Initialization = initialization;
        Detail = detail;
        LibraryPairing = libraryPairing?.Detach();
        TypeCorrespondence = typeCorrespondence?.Detach();
        MemberCorrespondence = memberCorrespondence?.Detach();
    }

    public WorkspacePackageDescriptor Source { get; }

    public WorkspacePackageDescriptor Destination { get; }

    public InspectionWorkspaceIdentity SourceWorkspace { get; }

    public InspectionWorkspaceIdentity DestinationWorkspace { get; }

    public NavigationCoordinateRetentionDisposition Disposition { get; }

    public NavigationInitialization Initialization { get; }

    public string Detail { get; }

    public CoordinateLibraryPairingEvidence? LibraryPairing { get; }

    public ApiCoordinateCorrespondenceEvidence? TypeCorrespondence { get; }

    public ApiCoordinateCorrespondenceEvidence? MemberCorrespondence { get; }
}

/// <summary>
/// Transitional orchestration for an accepted protected Package-coordinate
/// replacement. Scope submission and all artifact access remain invocation-local.
/// </summary>
public static partial class NavigationScopeOperations
{
    public static async ValueTask<NavigationScopeEvaluationResult>
        EvaluateCoordinateReplacementAsync(
            InspectionWorkspace workspace,
            NavigationScopeEvaluationRequest request,
            WorkspaceScopeRequest scopeRequest,
            PackageRootBinding sourceBinding,
            ViewFacetRegistry registry,
            NavigationFacetAvailabilityProvider availability,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scopeRequest);
        ArgumentNullException.ThrowIfNull(sourceBinding);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);
        ValidateRequest(
            workspace,
            request,
            scopeRequest,
            sourceBinding,
            out WorkspacePackageOccurrenceDescriptor sourceOccurrence,
            out PackageRootBinding destinationBinding);

        if (sourceOccurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready sourceReady)
        {
            WorkspaceScopeOperationResult settlement =
                await workspace.SubmitScopeRequestAsync(
                    scopeRequest,
                    cancellationToken).ConfigureAwait(false);
            return Evaluate(
                request,
                settlement,
                cancellationToken.IsCancellationRequested
                    ? new NavigationScopePreparation.Aborted(
                        "Protected coordinate replacement was cancelled before "
                            + "the source Package Root could be admitted.")
                    : new NavigationScopePreparation.Failed(
                        "The retained source Package Root is not ready."),
                registry);
        }

        WorkspaceScopeOperationResult? submitted = null;
        try
        {
            ArtifactRootResult<NavigationScopeEvaluationResult> sourceAccess =
                await workspace.ExecutePackageRootQueryAsync(
                    (PackageArtifactRootCorrespondence)
                        sourceOccurrence.Occurrence.Correspondence,
                    sourceReady.Generation,
                    async (sourceRealization, _) =>
                    {
                        CoordinatePackageObservationResult sourceObservation =
                            CoordinateLibraryPairingQuery.ObserveAdmitted(
                                workspace,
                                sourceBinding,
                                sourceOccurrence,
                                sourceRealization,
                                cancellationToken);
                        if (sourceObservation
                            is CoordinatePackageObservationResult.Unavailable
                                sourceUnavailable)
                        {
                            submitted =
                                await workspace.SubmitScopeRequestAsync(
                                    scopeRequest,
                                    cancellationToken).ConfigureAwait(false);
                            return Evaluate(
                                request,
                                submitted,
                                new NavigationScopePreparation.Failed(
                                    "The retained source Package could not be "
                                        + "observed for coordinate retention: "
                                        + sourceUnavailable.Failure.Detail),
                                registry);
                        }

                        CoordinatePackageObservation source =
                            ((CoordinatePackageObservationResult.Available)
                                sourceObservation).Observation;
                        submitted =
                            await workspace.SubmitScopeRequestAsync(
                                scopeRequest,
                                cancellationToken).ConfigureAwait(false);
                        return await PrepareSettlementAsync(
                            workspace,
                            request,
                            submitted,
                            sourceOccurrence,
                            sourceBinding,
                            sourceRealization,
                            source,
                            destinationBinding,
                            registry,
                            availability,
                            cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            if (sourceAccess
                is ArtifactRootResult<
                    NavigationScopeEvaluationResult>.Available available)
            {
                return available.Value;
            }

            ArtifactRootFailure failure =
                ((ArtifactRootResult<
                    NavigationScopeEvaluationResult>.Rejected)sourceAccess)
                    .Failure;
            submitted =
                await workspace.SubmitScopeRequestAsync(
                    scopeRequest,
                    cancellationToken).ConfigureAwait(false);
            return Evaluate(
                request,
                submitted,
                cancellationToken.IsCancellationRequested
                    ? new NavigationScopePreparation.Aborted(
                        "Protected coordinate replacement was cancelled before "
                            + "source admission.")
                    : new NavigationScopePreparation.Failed(
                        $"The retained source Package Root rejected admission: "
                            + $"{failure}."),
                registry);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            submitted ??=
                await workspace.SubmitScopeRequestAsync(
                    scopeRequest,
                    cancellationToken).ConfigureAwait(false);
            return Evaluate(
                request,
                submitted,
                new NavigationScopePreparation.Aborted(
                    "Protected coordinate replacement was cancelled."),
                registry);
        }
    }

    static async ValueTask<NavigationScopeEvaluationResult>
        PrepareSettlementAsync(
            InspectionWorkspace workspace,
            NavigationScopeEvaluationRequest request,
            WorkspaceScopeOperationResult settlement,
            WorkspacePackageOccurrenceDescriptor sourceOccurrence,
            PackageRootBinding sourceBinding,
            PackageAssemblyContextRealization sourceRealization,
            CoordinatePackageObservation sourceObservation,
            PackageRootBinding destinationBinding,
            ViewFacetRegistry registry,
            NavigationFacetAvailabilityProvider availability,
            CancellationToken cancellationToken)
    {
        if (settlement is WorkspaceScopeOperationResult.Unavailable)
        {
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Historical(),
                registry);
        }

        WorkspaceScopeSnapshot scope = CurrentScope(settlement);
        WorkspacePackageOccurrenceDescriptor? selected =
            SelectedOccurrence(request, settlement, scope);
        if (selected is null)
        {
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(scope, Package: null, availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            workspace.Identity))),
                registry);
        }

        if (selected.Realization.Status
                is not ArtifactRootRealizationStatus.Ready selectedReady)
        {
            var package = StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(workspace.Identity),
                selected.Occurrence);
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(
                        scope,
                        Package: null,
                        availability,
                        new(selected)),
                    new(
                        Context:
                            new NavigationRetainedSubjectContext(package))),
                registry);
        }

        if (selected.Occurrence == sourceOccurrence.Occurrence)
        {
            if (!ReferenceEquals(
                    selectedReady.Generation,
                    ((ArtifactRootRealizationStatus.Ready)
                        sourceOccurrence.Realization.Status).Generation))
            {
                return Evaluate(
                    request,
                    settlement,
                    new NavigationScopePreparation.Failed(
                        "The retained source occurrence changed generation "
                            + "during protected replacement."),
                    registry);
            }

            NavigationPackageEvaluation package =
                NavigationPackageEvaluationFactory.Create(
                    selected,
                    sourceBinding,
                    sourceRealization,
                    cancellationToken);
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(scope, package, availability),
                    new(
                        request.Basis.ActiveSubject,
                        request.Basis.RetainedContext,
                        request.Basis.LensOutcome.Basis
                            is NavigationLensEvaluationBasis.ExactRequest exact
                                ? exact.Request
                                : null)),
                registry);
        }

        ArtifactRootResult<DestinationPreparation> destinationAccess =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    selected.Occurrence.Correspondence,
                selectedReady.Generation,
                (realization, token) =>
                {
                    CoordinatePackageObservationResult observed =
                        CoordinateLibraryPairingQuery.ObserveAdmitted(
                            workspace,
                            destinationBinding,
                            selected,
                            realization,
                            token);
                    if (observed
                        is CoordinatePackageObservationResult.Unavailable
                            unavailable)
                    {
                        return ValueTask.FromResult(
                            new DestinationPreparation(
                                Package: null,
                                Observation: null,
                                Failure:
                                    "The requested destination Package could "
                                        + "not be observed: "
                                        + unavailable.Failure.Detail));
                    }

                    return ValueTask.FromResult(
                        new DestinationPreparation(
                            NavigationPackageEvaluationFactory.Create(
                                selected,
                                destinationBinding,
                                realization,
                                token),
                            ((CoordinatePackageObservationResult.Available)
                                observed).Observation,
                            Failure: null));
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (destinationAccess
            is ArtifactRootResult<DestinationPreparation>.Rejected rejected)
        {
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Failed(
                    $"The requested destination Package Root rejected "
                        + $"preparation: {rejected.Failure}."),
                registry);
        }

        DestinationPreparation destination =
            ((ArtifactRootResult<DestinationPreparation>.Available)
                destinationAccess).Value;
        if (destination.Failure is not null)
        {
            return Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Failed(destination.Failure),
                registry);
        }

        NavigationPackageEvaluation destinationPackage =
            destination.Package
            ?? throw new InvalidOperationException(
                "Successful destination preparation omitted Package facts.");
        CoordinatePackageObservation destinationObservation =
            destination.Observation
            ?? throw new InvalidOperationException(
                "Successful destination preparation omitted its observation.");
        NavigationCoordinateRetentionResult retention =
            await NavigationCoordinateRetentionPolicy.PrepareAsync(
                workspace,
                workspace,
                request.Basis,
                sourceObservation,
                sourceRealization,
                destinationObservation,
                destinationPackage,
                cancellationToken).ConfigureAwait(false);
        NavigationScopeEvaluationResult evaluation =
            Evaluate(
                request,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(scope, destinationPackage, availability),
                    retention.Initialization),
                registry);
        return evaluation.WithCoordinateRetention(retention);
    }
}

internal static class NavigationCoordinateRetentionPolicy
{
    internal static async ValueTask<NavigationCoordinateRetentionResult>
        PrepareAsync(
            InspectionWorkspace sourceWorkspace,
            InspectionWorkspace destinationWorkspace,
            NavigationWorkspaceSnapshot basis,
            CoordinatePackageObservation sourceObservation,
            PackageAssemblyContextRealization sourceRealization,
            CoordinatePackageObservation destinationObservation,
            NavigationPackageEvaluation destinationPackage,
            CancellationToken cancellationToken)
    {
        NavigationRetainedSubjectContext sourceContext =
            basis.RetainedContext
            ?? throw new InvalidOperationException(
                "A protected coordinate replacement requires retained source "
                    + "Package context.");
        var destinationPackageSubject =
            StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(
                    destinationWorkspace.Identity),
                destinationPackage.Occurrence.Occurrence);
        NavigationSubjectInventory inventory =
            NavigationWorkspaceSnapshotEvaluation.ClassifySubjectInventory(
                destinationPackageSubject,
                destinationPackage);
        StructuralSubjectIdentity.AllLibrariesSubject? aggregate =
            destinationPackage.Libraries.IsEmpty
                ? null
                : StructuralSubjectIdentity.ForAllLibraries(
                    destinationPackageSubject);

        CoordinateLibraryPairingResult? pairing = null;
        ApiCoordinateCorrespondenceResult? typeCorrespondence = null;
        ApiCoordinateCorrespondenceResult? memberCorrespondence = null;
        StructuralSubjectIdentity? retainedLibrary = null;
        StructuralSubjectIdentity.TypeSubject? retainedType = null;
        StructuralSubjectIdentity.MemberSubject? retainedMember = null;
        NavigationCoordinateRetentionDisposition disposition;
        string detail;

        if (sourceContext.Library
            is StructuralSubjectIdentity.AllLibrariesSubject)
        {
            retainedLibrary = aggregate;
            disposition = retainedLibrary is null
                ? NavigationCoordinateRetentionDisposition.PackageFallback
                : NavigationCoordinateRetentionDisposition.ExactPath;
            detail = retainedLibrary is null
                ? "The destination has no available aggregate Library."
                : "The destination aggregate Library remains available.";
        }
        else if (sourceContext.Library
            is StructuralSubjectIdentity.LibrarySubject sourceLibrary)
        {
            if (sourceContext.Type is { } sourceType)
            {
                typeCorrespondence =
                    await ApiCoordinateCorrespondenceQuery
                        .ExecuteAdmittedSourceAsync(
                            sourceWorkspace,
                            destinationWorkspace,
                            sourceType,
                            sourceObservation,
                            sourceRealization,
                            destinationObservation,
                            cancellationToken).ConfigureAwait(false);
                pairing = typeCorrespondence.LibraryPairing;
            }
            else
            {
                pairing = CoordinateLibraryPairingQuery.Execute(
                    sourceLibrary,
                    sourceObservation,
                    destinationObservation);
            }

            StructuralSubjectIdentity.LibrarySubject? pairedLibrary =
                pairing.Status == CoordinateLibraryPairingStatus.Exact
                    ? pairing.Destination?.Subject
                    : null;
            bool pairedAvailable =
                pairedLibrary is not null
                && inventory.Libraries.Any(
                    library => library.Subject == pairedLibrary);
            retainedLibrary = pairedAvailable
                ? pairedLibrary
                : aggregate;
            disposition = pairedAvailable
                ? NavigationCoordinateRetentionDisposition.ExactPath
                : retainedLibrary is not null
                    ? NavigationCoordinateRetentionDisposition
                        .LibraryFallbackToAggregate
                    : NavigationCoordinateRetentionDisposition.PackageFallback;
            detail = pairedAvailable
                ? "The exact paired destination Library remains available."
                : retainedLibrary is not null
                    ? "Library correspondence did not retain an exact "
                        + "destination Library; Navigation used the destination "
                        + "aggregate."
                    : "Library correspondence did not retain an exact "
                        + "destination Library or aggregate.";

            if (sourceContext.Type is { }
                && typeCorrespondence is { } typeResult)
            {
                StructuralSubjectIdentity.TypeSubject? destinationType =
                    typeResult.Status
                            == ApiCoordinateCorrespondenceStatus.Exact
                        ? typeResult.Destination
                            as StructuralSubjectIdentity.TypeSubject
                        : null;
                bool typeAvailable =
                    destinationType is not null
                    && inventory.Types.Rows.Any(
                        row => row.Subject == destinationType);
                bool activeLibraryContainment =
                    basis.ActiveSubject
                        is StructuralSubjectIdentity.LibrarySubject
                    && pairedLibrary is not null
                    && destinationType is not null
                    && destinationType.Library != pairedLibrary;
                if (activeLibraryContainment)
                {
                    retainedLibrary = pairedLibrary;
                    disposition = NavigationCoordinateRetentionDisposition
                        .ActiveLibraryContainmentTruncated;
                    detail =
                        "The exact Type moved to a different defining Library; "
                        + "the active paired Library remains selected and lower "
                        + "context was truncated.";
                }
                else if (typeAvailable)
                {
                    retainedLibrary = destinationType!.Library;
                    retainedType = destinationType;
                    if (sourceContext.Member is { } sourceMember)
                    {
                        ApiDeclarationKind sourceKind =
                            SourceMemberKind(basis, sourceMember);
                        memberCorrespondence =
                            await ApiCoordinateCorrespondenceQuery
                                .ExecuteAdmittedSourceAsync(
                                    sourceWorkspace,
                                    destinationWorkspace,
                                    sourceMember,
                                    sourceKind,
                                    sourceObservation,
                                    sourceRealization,
                                    destinationObservation,
                                    cancellationToken).ConfigureAwait(false);
                        StructuralSubjectIdentity.MemberSubject?
                            destinationMember =
                                memberCorrespondence.Status
                                        == ApiCoordinateCorrespondenceStatus
                                            .Exact
                                    ? memberCorrespondence.Destination
                                        as StructuralSubjectIdentity
                                            .MemberSubject
                                    : null;
                        bool memberAvailable =
                            destinationMember is not null
                            && destinationMember.DeclaringType
                                == destinationType
                            && inventory.Types.Rows
                                .Where(
                                    row =>
                                        row.Subject == destinationType)
                                .SelectMany(row => row.Members)
                                .Any(
                                    row =>
                                        row.Subject
                                            == destinationMember);
                        if (memberAvailable)
                        {
                            retainedMember = destinationMember;
                            disposition =
                                NavigationCoordinateRetentionDisposition
                                    .ExactPath;
                            detail =
                                "The exact destination Type and Member remain "
                                + "available under the same defining Library.";
                        }
                        else
                        {
                            disposition =
                                NavigationCoordinateRetentionDisposition
                                    .MemberFallbackToType;
                            detail =
                                "Member correspondence did not retain an exact "
                                + "available Member; Navigation fell back to "
                                + "the exact destination Type.";
                        }
                    }
                    else
                    {
                        disposition =
                            NavigationCoordinateRetentionDisposition.ExactPath;
                        detail =
                            "The exact destination Type remains available in "
                            + "its defining Library.";
                    }
                }
                else
                {
                    retainedLibrary = pairedAvailable
                        ? pairedLibrary
                        : aggregate;
                    disposition = retainedLibrary is not null
                        ? NavigationCoordinateRetentionDisposition
                            .TypeFallbackToLibrary
                        : NavigationCoordinateRetentionDisposition
                            .PackageFallback;
                    detail =
                        "Type correspondence did not retain an exact available "
                        + "destination Type; Navigation truncated lower context.";
                }
            }
        }
        else
        {
            disposition =
                NavigationCoordinateRetentionDisposition.ExactPath;
            detail = "The retained source path contains only its Package.";
        }

        var destinationContext = new NavigationRetainedSubjectContext(
            destinationPackageSubject,
            retainedLibrary,
            retainedType,
            retainedMember);
        StructuralSubjectIdentity active =
            RetainedActiveSubject(
                basis.ActiveSubject,
                sourceContext,
                destinationPackageSubject,
                destinationContext);
        bool exactActive = ActiveSubjectRetainedExactly(
            basis.ActiveSubject,
            sourceContext,
            active,
            destinationContext,
            pairing);
        NavigationLensIdentity? lens =
            exactActive
            && basis.LensOutcome.Basis
                is NavigationLensEvaluationBasis.ExactRequest exact
                ? new NavigationLensIdentity(active, exact.Request.Facet)
                : null;
        var initialization =
            new NavigationInitialization(
                active,
                destinationContext,
                lens);
        return new(
            sourceObservation,
            destinationObservation,
            disposition,
            initialization,
            detail,
            pairing,
            typeCorrespondence,
            memberCorrespondence);
    }

    static StructuralSubjectIdentity RetainedActiveSubject(
        StructuralSubjectIdentity sourceActive,
        NavigationRetainedSubjectContext source,
        StructuralSubjectIdentity.PackageSubject destinationPackage,
        NavigationRetainedSubjectContext destination)
    {
        if (sourceActive == source.Package.Workspace)
            return destinationPackage.Workspace;
        if (sourceActive == source.Package)
            return destinationPackage;
        if (sourceActive == source.Library)
            return destination.Library ?? destinationPackage;
        if (sourceActive == source.Type)
        {
            return destination.Type
                ?? destination.Library
                ?? destinationPackage;
        }
        if (sourceActive == source.Member)
        {
            return destination.Member
                ?? destination.Type
                ?? destination.Library
                ?? destinationPackage;
        }
        throw new InvalidOperationException(
            "The active source subject is outside its retained context.");
    }

    static bool ActiveSubjectRetainedExactly(
        StructuralSubjectIdentity sourceActive,
        NavigationRetainedSubjectContext source,
        StructuralSubjectIdentity destinationActive,
        NavigationRetainedSubjectContext destination,
        CoordinateLibraryPairingResult? pairing)
    {
        if (sourceActive == source.Package.Workspace
            || sourceActive == source.Package)
        {
            return true;
        }
        if (sourceActive
            is StructuralSubjectIdentity.AllLibrariesSubject)
        {
            return destinationActive
                is StructuralSubjectIdentity.AllLibrariesSubject;
        }
        if (sourceActive
            is StructuralSubjectIdentity.LibrarySubject)
        {
            return pairing?.Status
                    == CoordinateLibraryPairingStatus.Exact
                && destination.Library
                    is StructuralSubjectIdentity.LibrarySubject exactLibrary
                && exactLibrary == pairing.Destination?.Subject
                && destinationActive == exactLibrary;
        }
        if (sourceActive == source.Type)
            return destinationActive == destination.Type;
        if (sourceActive == source.Member)
            return destinationActive == destination.Member;
        return false;
    }

    static ApiDeclarationKind SourceMemberKind(
        NavigationWorkspaceSnapshot basis,
        StructuralSubjectIdentity.MemberSubject member)
    {
        NavigationMemberInventoryRow row =
            basis.Inventory?.Types.Rows
                .SingleOrDefault(type => type.Subject == member.DeclaringType)
                ?.Members
                .SingleOrDefault(candidate => candidate.Subject == member)
            ?? throw new InvalidOperationException(
                "The retained source Member has no exact inventory row.");
        return row.ProducerRow.Kind switch
        {
            "property" => ApiDeclarationKind.Property,
            "event" => ApiDeclarationKind.Event,
            "field" or "constant" => ApiDeclarationKind.Field,
            "method" or "constructor" or "finalizer" or "operator"
                or "explicit-interface-implementation" or "extension-method" =>
                ApiDeclarationKind.Method,
            _ => throw new InvalidOperationException(
                "The retained source Member has no supported declaration kind."),
        };
    }
}

public static partial class NavigationScopeOperations
{
    static NavigationScopeEvaluationResult Evaluate(
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeOperationResult settlement,
        NavigationScopePreparation preparation,
        ViewFacetRegistry registry) =>
        NavigationTransitions.EvaluateScopeOperation(
            request,
            settlement,
            settlement is WorkspaceScopeOperationResult.Unavailable
                ? new NavigationScopePreparation.Historical()
                : preparation,
            registry);

    static WorkspaceScopeSnapshot CurrentScope(
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
                "The Scope settlement has no current snapshot.",
                nameof(settlement)),
        };

    static WorkspacePackageOccurrenceDescriptor? SelectedOccurrence(
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeOperationResult settlement,
        WorkspaceScopeSnapshot scope)
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
            return requested
                ?? throw new InvalidOperationException(
                    "A successful explicit Scope result omitted its requested "
                        + "occurrence.");
        }

        WorkspacePackageOccurrence? active = request.Basis.ActiveOccurrence;
        return active is null
            ? null
            : scope.Packages.FirstOrDefault(
                candidate => candidate.Occurrence == active);
    }

    static void ValidateRequest(
        InspectionWorkspace workspace,
        NavigationScopeEvaluationRequest request,
        WorkspaceScopeRequest scopeRequest,
        PackageRootBinding sourceBinding,
        out WorkspacePackageOccurrenceDescriptor sourceOccurrence,
        out PackageRootBinding destinationBinding)
    {
        if (!ReferenceEquals(request.Association, scopeRequest.Association))
        {
            throw new ArgumentException(
                "Protected replacement requires the exact accepted Scope "
                    + "request association.",
                nameof(scopeRequest));
        }
        if (!ReferenceEquals(workspace.Identity, request.Workspace))
        {
            throw new ArgumentException(
                "Protected replacement requires the accepted Workspace.",
                nameof(workspace));
        }
        if (scopeRequest.Association.Kind
                != WorkspaceScopeOperationKind.Replace
            || scopeRequest.Target is null)
        {
            throw new ArgumentException(
                "Coordinate replacement requires an explicit Replace target.",
                nameof(scopeRequest));
        }
        if (!ReferenceEquals(
                scopeRequest.Association.ExpectedRevisionWorkspace,
                workspace.Identity)
            || !ReferenceEquals(
                scopeRequest.Association.ExpectedRevision,
                request.Basis.Scope.Revision.Identity))
        {
            throw new ArgumentException(
                "Coordinate replacement must be issued against the accepted "
                    + "Navigation Scope revision.",
                nameof(scopeRequest));
        }

        WorkspacePackageOccurrence source =
            request.Basis.ActiveOccurrence
            ?? throw new ArgumentException(
                "Coordinate replacement requires one retained source "
                    + "occurrence.",
                nameof(request));
        sourceOccurrence =
            request.Basis.Scope.Packages.SingleOrDefault(
                candidate => candidate.Occurrence == source)
            ?? throw new ArgumentException(
                "The retained source occurrence is absent from the accepted "
                    + "Navigation Scope.",
                nameof(request));
        if (sourceOccurrence.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence sourceCorrespondence
            || !sourceCorrespondence.Matches(
                PackageArtifactRootRequest.From(sourceBinding))
            || !sourceBinding.ReferencesRetainedContent())
        {
            throw new ArgumentException(
                "The source binding must identify the exact retained source "
                    + "occurrence and content generation.",
                nameof(sourceBinding));
        }

        destinationBinding =
            scopeRequest.Packages.SingleOrDefault(
                binding =>
                    scopeRequest.Target.Correspondence.Matches(
                        PackageArtifactRootRequest.From(binding)))
            ?? throw new ArgumentException(
                "The explicit Scope target must identify exactly one "
                    + "destination binding in the issued request.",
                nameof(scopeRequest));
    }

    sealed record DestinationPreparation(
        NavigationPackageEvaluation? Package,
        CoordinatePackageObservation? Observation,
        string? Failure);
}
