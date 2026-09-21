using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Common source-neutral Artifact publication and complete population handoff
/// outcome.
/// </summary>
public abstract class PlatformPopulationArtifactMaterializationOutcome
{
    private protected PlatformPopulationArtifactMaterializationOutcome(
        PlatformPopulationRealizationResult realization) =>
        Realization = realization;

    public PlatformPopulationRealizationResult Realization { get; }

    public sealed class Completed :
        PlatformPopulationArtifactMaterializationOutcome
    {
        /// <summary>
        /// Creates a source-neutral completed handoff from one owner-issued
        /// population and its adjacent Artifact session.
        /// </summary>
        public static Completed Create(
            PlatformPopulationRealizationResult.Completed population,
            ArtifactSetSession artifacts) =>
            new(population, artifacts);

        internal Completed(
            PlatformPopulationRealizationResult.Completed population,
            ArtifactSetSession artifacts)
            : base(population)
        {
            ArgumentNullException.ThrowIfNull(artifacts);
            if (population.Value.Libraries
                .SelectMany(static library => library.Contents)
                .Any(
                    content => !ReferenceEquals(
                        content.ArtifactReference.Generation,
                        artifacts.Generation)))
            {
                throw new ArgumentException(
                    "Every realized population content item must belong to the returned Artifact session.",
                    nameof(artifacts));
            }

            Population = population;
            Artifacts = artifacts;
        }

        public PlatformPopulationRealizationResult.Completed Population
        {
            get;
        }

        public ArtifactSetSession Artifacts { get; }
    }

    public sealed class Terminal :
        PlatformPopulationArtifactMaterializationOutcome
    {
        internal Terminal(
            PlatformPopulationRealizationResult.Terminal realization)
            : base(realization) =>
            TerminalRealization = realization;

        public PlatformPopulationRealizationResult.Terminal
            TerminalRealization
        { get; }
    }
}

/// <summary>
/// Publishes one or two authoritative population views and performs their
/// atomic Library ownership handoff.
/// </summary>
public static class PlatformHousePopulationArtifactMaterializer
{
    /// <summary>
    /// Preserves outcomes for the expected family and retires any completed
    /// authorities before rejecting a mismatched request family.
    /// </summary>
    public static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        RequireRequestFamilyAsync(
            PlatformPopulationArtifactMaterializationOutcome outcome,
            PlatformFamily expectedFamily,
            string evidenceName)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceName);
        if (!Enum.IsDefined(expectedFamily))
            throw new ArgumentOutOfRangeException(nameof(expectedFamily));

        PlatformHouseReceipt receipt = outcome.Realization.Receipt.HouseReceipt;
        if (receipt.Request.Target.Family == expectedFamily)
            return outcome;

        if (outcome
            is PlatformPopulationArtifactMaterializationOutcome.Completed
                completed)
        {
            PlatformHouseFailureKind[] failures =
                await RetireCompletedAsync(completed).ConfigureAwait(false);
            if (failures.Length != 0)
            {
                return Failed(
                    receipt,
                    failures,
                    $"{evidenceName}.cleanup-failed");
            }
        }

        return Rejected(
            receipt,
            PlatformHouseRejectionKind.InvalidTargetCorrespondence,
            evidenceName);
    }

    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferencesAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeCoreAsync(
            request,
            PlatformViewDemand.Reference,
            items,
            [],
            consumedWork,
            identityPrefix,
            targetSelection: null,
            retainedSettlements: null,
            currentWork: null,
            materializationCancellation:
                request.CancellationToken);

    internal static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeSelectedReferencesAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement>
                retainedSettlements,
            Func<PlatformHouseConsumedWork> currentWork,
            CancellationToken materializationCancellation)
    {
        ArgumentNullException.ThrowIfNull(targetSelection);
        ArgumentNullException.ThrowIfNull(retainedSettlements);
        ArgumentNullException.ThrowIfNull(currentWork);
        return MaterializeCoreAsync(
            request,
            PlatformViewDemand.Reference,
            items,
            [],
            consumedWork,
            identityPrefix,
            targetSelection,
            retainedSettlements,
            currentWork,
            materializationCancellation);
    }

    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeReferenceAndImplementationAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                    references,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                    implementations,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeCoreAsync(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            references,
            implementations,
            consumedWork,
            identityPrefix,
            targetSelection: null,
            retainedSettlements: null,
            currentWork: null,
            materializationCancellation:
                request.CancellationToken);

    public static ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeImplementationsAsync(
            PlatformHouseRequest request,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix) =>
        MaterializeCoreAsync(
            request,
            PlatformViewDemand.Implementation,
            [],
            items,
            consumedWork,
            identityPrefix,
            targetSelection: null,
            retainedSettlements: null,
            currentWork: null,
            materializationCancellation:
                request.CancellationToken);

    static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        MaterializeCoreAsync(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                    references,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                    implementations,
            PlatformHouseConsumedWork consumedWork,
            string identityPrefix,
            PlatformTargetSelectionContext? targetSelection,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements,
            Func<PlatformHouseConsumedWork>? currentWork,
            CancellationToken materializationCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(implementations);
        ArgumentNullException.ThrowIfNull(consumedWork);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);
        request.CancellationToken.ThrowIfCancellationRequested();

        PlatformHouseConsumedWork CurrentWork() =>
            currentWork?.Invoke() ?? consumedWork;

        consumedWork = CurrentWork();
        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumedWork,
                request))
        {
            return Terminal(
                Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.work-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }

        PopulationMaterializationPlan? plan =
            PopulationMaterializationPlan.TryCreate(
                request,
                expectedView,
                references,
                implementations,
                consumedWork,
                targetSelection);
        if (plan is null)
        {
            return Terminal(
                Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-result",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }
        if (plan.ExceedsMaterializationBudget)
        {
            return Terminal(
                Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }

        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = plan.Items.Count,
                MaxArtifactBytes = plan.MaxContentLength,
                MaxRetainedBytes = plan.TotalContentLength,
            });
        var contentLeases = new List<ArtifactContentLease>(
            plan.Items.Count);
        ArtifactQueryLease? queryLease = null;
        try
        {
            var artifactIdentities = new List<ArtifactIdentity>(
                plan.Items.Count);
            await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        var contributions =
                            new List<ArtifactContribution>(
                                plan.Items.Count);
                        foreach (
                            PlatformPopulationLibraryArtifactMaterializationItem
                                item
                            in plan.Items)
                        {
                            PlatformLibraryArtifactMaterializationItem
                                library = item.Library;
                            ArtifactContribution contribution =
                                scope.Register(
                                    new PlatformLibraryArtifactProvenance(
                                        library.Contribution,
                                        library.Provenance),
                                    library.OpenRead);
                            contributions.Add(contribution);
                            library.ArtifactIdentity =
                                contribution.Descriptor.Identity;
                            artifactIdentities.Add(
                                contribution.Descriptor.Identity);
                        }
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    contributions,
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken:
                        materializationCancellation)
                .ConfigureAwait(false);

            var projections =
                new Dictionary<
                    ArtifactIdentity,
                    ArtifactAssemblyProjection>(
                        ReferenceEqualityComparer.Instance);
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, cancellationToken) =>
                            Project(
                                view,
                                plan,
                                projections,
                                identityPrefix,
                                cancellationToken),
                        materializationCancellation)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished notPublished)
            {
                consumedWork = CurrentWork();
                PlatformPopulationRealizationResult failure =
                    PublicationFailure(
                        request,
                        consumedWork,
                        plan,
                        notPublished,
                        identityPrefix,
                        targetSelection,
                        retainedSettlements);
                IReadOnlyList<Exception> cleanup =
                    await CleanupAsync(
                            session,
                            queryLease,
                            contentLeases)
                        .ConfigureAwait(false);
                if (cleanup.Count != 0)
                {
                    failure = CleanupFailure(
                        request,
                        consumedWork,
                        plan,
                        failure,
                        cancellationObserved: false,
                        identityPrefix,
                        targetSelection,
                        retainedSettlements);
                }
                return Terminal(failure);
            }

            queryLease = session.IssueLease(
                session.CreateQueryAuthorization());
            var selections =
                new List<PlatformPopulationLibraryContentSelection>(
                    plan.Items.Count);
            for (int index = 0; index < plan.Items.Count; index++)
            {
                ArtifactContentReference content =
                    session.GetContentReference(
                        artifactIdentities[index],
                        queryLease);
                selections.Add(
                    new PlatformPopulationLibraryContentSelection(
                        content,
                        projections[content.Artifact],
                        plan.Items[index].Attribution));
                contentLeases.Add(
                    session.IssueContentLease(
                        content,
                        queryLease));
            }
            queryLease.Dispose();
            queryLease = null;

            consumedWork = CurrentWork();
            PlatformPopulationRealizationResult realization =
                PlatformHouseLibraryRealizer.ExceedsBudget(
                    consumedWork,
                    request)
                    ? PlatformHousePopulationRealizer.Incomplete(
                        request,
                        consumedWork,
                        $"{identityPrefix}.materialization-duration-incomplete",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    : await RealizePopulationAsync(
                            request,
                            expectedView,
                            selections,
                            contentLeases,
                            consumedWork,
                            plan.ReferenceCount,
                            targetSelection,
                            retainedSettlements)
                        .ConfigureAwait(false);
            if (realization
                is PlatformPopulationRealizationResult.Completed completed)
            {
                return new PlatformPopulationArtifactMaterializationOutcome
                    .Completed(completed, session);
            }

            static ValueTask<PlatformPopulationRealizationResult>
                RealizePopulationAsync(
                    PlatformHouseRequest request,
                    PlatformViewDemand expectedView,
                    IReadOnlyList<PlatformPopulationLibraryContentSelection>
                        selections,
                    IReadOnlyList<ArtifactContentLease> contentLeases,
                    PlatformHouseConsumedWork consumedWork,
                    int referenceCount,
                    PlatformTargetSelectionContext? targetSelection,
                    IReadOnlyList<PlatformSourceSettlement>?
                        retainedSettlements)
            {
                if (targetSelection is not null)
                {
                    if (expectedView != PlatformViewDemand.Reference
                        || retainedSettlements is null)
                    {
                        throw new InvalidOperationException(
                            "Selected population materialization supports only a retained Reference population.");
                    }
                    return PlatformHousePopulationRealizer
                        .RealizeSelectedReferencesAsync(
                            request,
                            selections,
                            contentLeases,
                            consumedWork,
                            targetSelection,
                            retainedSettlements);
                }

                return expectedView switch
                {
                    PlatformViewDemand.Reference =>
                        PlatformHousePopulationRealizer.RealizeReferencesAsync(
                            request,
                            selections,
                            contentLeases,
                            consumedWork),
                    PlatformViewDemand.ReferenceAndImplementation =>
                        PlatformHousePopulationRealizer
                            .RealizeReferenceAndImplementationAsync(
                                request,
                                selections.Take(referenceCount).ToArray(),
                                contentLeases.Take(referenceCount).ToArray(),
                                selections.Skip(referenceCount).ToArray(),
                                contentLeases.Skip(referenceCount).ToArray(),
                                consumedWork),
                    PlatformViewDemand.Implementation =>
                        PlatformHousePopulationRealizer.RealizeImplementationsAsync(
                            request,
                            selections,
                            contentLeases,
                            consumedWork),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(expectedView)),
                };
            }

            IReadOnlyList<Exception> terminalCleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (terminalCleanup.Count != 0)
            {
                return Terminal(
                    CleanupFailure(
                        request,
                        consumedWork,
                        plan,
                        realization,
                        cancellationObserved: false,
                        identityPrefix,
                        targetSelection,
                        retainedSettlements));
            }
            return Terminal(realization);
        }
        catch (OperationCanceledException)
            when (request.CancellationToken.IsCancellationRequested)
        {
            consumedWork = CurrentWork();
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (cleanup.Count != 0)
            {
                return Terminal(
                    PlatformHousePopulationRealizer.Failed(
                        request,
                        consumedWork,
                        plan.Contributions,
                        [PlatformHouseFailureKind.ArtifactRetirement],
                        cancellationObserved: true,
                        $"{identityPrefix}.cancellation-cleanup-failed",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements)));
            }
            throw;
        }
        catch (OperationCanceledException)
            when (materializationCancellation.IsCancellationRequested)
        {
            consumedWork = CurrentWork();
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            if (cleanup.Count != 0)
            {
                return Terminal(
                    PlatformHousePopulationRealizer.Failed(
                        request,
                        consumedWork,
                        plan.Contributions,
                        [PlatformHouseFailureKind.ArtifactRetirement],
                        cancellationObserved: false,
                        $"{identityPrefix}.duration-cleanup-failed",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements)));
            }
            return Terminal(
                PlatformHousePopulationRealizer.Incomplete(
                    request,
                    consumedWork,
                    $"{identityPrefix}.materialization-duration-incomplete",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements)));
        }
        catch (Exception failure)
        {
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(failure, cleanup);
            throw;
        }
    }

    static ArtifactSetAdmissionFailure? Project(
        scoped ArtifactAdmissionContentView view,
        PopulationMaterializationPlan plan,
        IDictionary<ArtifactIdentity, ArtifactAssemblyProjection> projections,
        string identityPrefix,
        CancellationToken cancellationToken)
    {
        ArtifactAssemblyProjectionOutcome outcome =
            ArtifactAssemblyInspection.Project(
                view,
                cancellationToken);
        if (outcome
            is not ArtifactAssemblyProjectionOutcome.Projected projected)
        {
            return Failure(
                $"{identityPrefix}.metadata-rejected",
                "Metadata could not project one Platform population assembly.");
        }

        ArtifactIdentity artifact = view.Artifact;
        PlatformPopulationLibraryArtifactMaterializationItem item =
            plan.Items.Single(
                candidate => ReferenceEquals(
                    candidate.Library.ArtifactIdentity,
                    artifact));
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                item.Library.Identity,
                projected.Value.Identity))
        {
            return Failure(
                $"{identityPrefix}.identity-mismatch",
                "Metadata projected an identity different from the Platform source identity.");
        }

        projections.Add(artifact, projected.Value);
        return null;
    }

    static PlatformPopulationRealizationResult PublicationFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PopulationMaterializationPlan plan,
        ArtifactSetPublicationOutcome.NotPublished publication,
        string identityPrefix,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements)
    {
        var failures = new List<PlatformHouseFailureKind>();
        if (publication.Failures.Any(
                failure => failure.Diagnostic.Code.StartsWith(
                    $"{identityPrefix}.metadata",
                    StringComparison.Ordinal)
                    || failure.Diagnostic.Code.EndsWith(
                        "identity-mismatch",
                        StringComparison.Ordinal)))
        {
            failures.Add(PlatformHouseFailureKind.Metadata);
        }
        else
        {
            failures.Add(PlatformHouseFailureKind.ArtifactPublication);
        }
        if (publication.CleanupFailures.Count != 0)
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);

        return PlatformHousePopulationRealizer.Failed(
            request,
            consumedWork,
            plan.Contributions,
            failures,
            cancellationObserved: false,
            $"{identityPrefix}.publication-failed",
            targetSelection?.TargetSettlement,
            TerminalRetainedSettlements(
                targetSelection,
                retainedSettlements));
    }

    static PlatformPopulationRealizationResult CleanupFailure(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PopulationMaterializationPlan plan,
        PlatformPopulationRealizationResult primary,
        bool cancellationObserved,
        string identityPrefix,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements)
    {
        var failures = new List<PlatformHouseFailureKind>();
        bool primaryCancellationObserved = false;
        if (primary.Outcome
            is PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Failed failed)
        {
            failures.AddRange(failed.Evidence.Failures);
            primaryCancellationObserved =
                failed.Evidence.CancellationObserved;
        }
        failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
        return PlatformHousePopulationRealizer.Failed(
            request,
            consumedWork,
            plan.Contributions,
            failures.Distinct(),
            cancellationObserved || primaryCancellationObserved,
            $"{identityPrefix}.cleanup-failed",
            targetSelection?.TargetSettlement,
            TerminalRetainedSettlements(
                targetSelection,
                retainedSettlements));
    }

    static PlatformPopulationRealizationResult Rejected(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseRejectionKind kind,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements = null) =>
        PlatformHousePopulationRealizer.Rejected(
            request,
            consumedWork,
            kind,
            evidenceName,
            targetSettlement,
            retainedSettlements);

    static PlatformPopulationRealizationResult Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements = null) =>
        PlatformHousePopulationRealizer.Incomplete(
            request,
            consumedWork,
            evidenceName,
            targetSettlement,
            retainedSettlements);

    static IReadOnlyList<PlatformSourceSettlement>?
        TerminalRetainedSettlements(
            PlatformTargetSelectionContext? targetSelection,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements) =>
        targetSelection is null || retainedSettlements is null
            ? null
            :
            [.. retainedSettlements.Select(
                settlement =>
                    settlement.Disposition
                            == PlatformSourceSettlementDisposition.Selected
                        && settlement.Contribution.Facet
                            != PlatformSourceFacet.TargetDiscovery
                        ? new PlatformSourceSettlement(
                            settlement.Contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant)
                        : settlement)];

    static async ValueTask<IReadOnlyList<Exception>> CleanupAsync(
        ArtifactSetSession session,
        ArtifactQueryLease? queryLease,
        IEnumerable<ArtifactContentLease> contentLeases)
    {
        var failures = new List<Exception>();
        try
        {
            queryLease?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        foreach (ArtifactContentLease lease in contentLeases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(session.CleanupFailures);
        return failures;
    }

    static PlatformPopulationArtifactMaterializationOutcome.Terminal Terminal(
        PlatformPopulationRealizationResult result) =>
        new((PlatformPopulationRealizationResult.Terminal)result);

    static async ValueTask<PlatformHouseFailureKind[]> RetireCompletedAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed)
    {
        var failures = new List<PlatformHouseFailureKind>();
        foreach (LibraryContentOwner owner in completed.Population.Owners)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                failures.Add(PlatformHouseFailureKind.LibraryRetirement);
            }
        }
        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);
        }
        if (completed.Artifacts.CleanupFailures.Count != 0)
            failures.Add(PlatformHouseFailureKind.ArtifactRetirement);

        return [.. failures.Distinct()];
    }

    static PlatformPopulationArtifactMaterializationOutcome Rejected(
        PlatformHouseReceipt priorReceipt,
        PlatformHouseRejectionKind kind,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        PlatformHouseReceipt receipt = TerminalReceipt(
            priorReceipt,
            termination);
        return new PlatformPopulationArtifactMaterializationOutcome.Terminal(
            new PlatformPopulationRealizationResult.Terminal(
                new PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Rejected(
                        termination,
                        receipt),
                new PlatformPopulationRealizationReceipt(receipt)));
    }

    static PlatformPopulationArtifactMaterializationOutcome Failed(
        PlatformHouseReceipt priorReceipt,
        IEnumerable<PlatformHouseFailureKind> failures,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failures);
        PlatformHouseReceipt receipt = TerminalReceipt(
            priorReceipt,
            termination);
        return new PlatformPopulationArtifactMaterializationOutcome.Terminal(
            new PlatformPopulationRealizationResult.Terminal(
                new PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Failed(
                        termination,
                        receipt),
                new PlatformPopulationRealizationReceipt(receipt)));
    }

    static PlatformHouseReceipt TerminalReceipt(
        PlatformHouseReceipt priorReceipt,
        PlatformHouseTermination termination) =>
        new(
            priorReceipt.Request,
            PlatformHouseLibraryRealizer.TargetSettlement(
                priorReceipt.Request.Target),
            [],
            priorReceipt.ConsumedWork,
            termination: termination);

    static ArtifactSetAdmissionFailure Failure(
        string code,
        string summary) =>
        new(
            ArtifactSetAdmissionFailureKind.Failed,
            new MaterializationDiagnostic(code, summary));

    sealed record MaterializationDiagnostic(
        string Code,
        string Summary) : IArtifactAcquisitionDiagnostic;

    sealed class PopulationMaterializationPlan
    {
        private PopulationMaterializationPlan(
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            int referenceCount,
            long totalContentLength,
            int maxContentLength,
            bool exceedsMaterializationBudget)
        {
            Items = items;
            ReferenceCount = referenceCount;
            TotalContentLength = totalContentLength;
            MaxContentLength = maxContentLength;
            ExceedsMaterializationBudget =
                exceedsMaterializationBudget;
        }

        internal IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> Items
        { get; }

        internal int ReferenceCount { get; }
        internal long TotalContentLength { get; }
        internal int MaxContentLength { get; }
        internal bool ExceedsMaterializationBudget { get; }
        internal IEnumerable<PlatformSourceContribution> Contributions =>
            Items.Select(static item => item.Library.Contribution)
                .Distinct<PlatformSourceContribution.Realization>(
                    ReferenceEqualityComparer.Instance);

        internal static PopulationMaterializationPlan? TryCreate(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                references,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem>
                implementations,
            PlatformHouseConsumedWork consumedWork,
            PlatformTargetSelectionContext? targetSelection)
        {
            PlatformPopulationLibraryArtifactMaterializationItem[] items =
                [.. references, .. implementations];
            bool validShape = expectedView switch
            {
                PlatformViewDemand.Reference =>
                    references.Count != 0 && implementations.Count == 0,
                PlatformViewDemand.Implementation =>
                    references.Count == 0 && implementations.Count != 0,
                PlatformViewDemand.ReferenceAndImplementation =>
                    references.Count != 0 && implementations.Count != 0,
                _ => false,
            };
            PlatformFamilyTarget? target = request.Target switch
            {
                PlatformTargetDemand.Exact exact
                    when targetSelection is null => exact.Target,
                PlatformTargetDemand.FamilyDefault demand
                    when targetSelection is not null
                        && ReferenceEquals(
                            targetSelection.TargetSettlement.Demand,
                            demand) =>
                    targetSelection.Target,
                _ => null,
            };
            if (target is null
                || request.Operation is not PlatformHouseOperation.Realize
                {
                    View: var view,
                    Population:
                        PlatformPopulationDemand.CompletePopulation,
                } operation
                || view != expectedView
                || !validShape
                || items.Any(
                    static item => item.Library.ContentLength <= 0)
                || consumedWork.Assemblies < items.Length)
            {
                return null;
            }

            if (!ValidFacet(
                    PlatformSourceFacet.Reference,
                    references,
                    request,
                    target,
                    operation.Population)
                || !ValidFacet(
                    PlatformSourceFacet.Implementation,
                    implementations,
                    request,
                    target,
                    operation.Population))
                return null;

            int sourceOperations = items.Select(
                    static item => item.Library.Contribution)
                .Distinct<PlatformSourceContribution.Realization>(
                    ReferenceEqualityComparer.Instance)
                .Count();
            if (consumedWork.SourceOperations < sourceOperations)
                return null;

            long totalContentLength;
            try
            {
                totalContentLength = checked(items.Sum(
                    static item => item.Library.ContentLength));
            }
            catch (OverflowException)
            {
                return new(
                    items,
                    references.Count,
                    long.MaxValue,
                    int.MaxValue,
                    exceedsMaterializationBudget: true);
            }
            if (consumedWork.Bytes < totalContentLength)
                return null;

            bool exceedsBudget =
                items.Length > request.Work.MaxAssemblies
                || totalContentLength > request.Work.MaxBytes
                || totalContentLength > int.MaxValue
                || items.Any(
                    static item =>
                        item.Library.ContentLength > int.MaxValue);
            int maxContentLength = exceedsBudget
                ? 0
                : checked((int)items.Max(
                    static item => item.Library.ContentLength));
            return new(
                items,
                references.Count,
                totalContentLength,
                maxContentLength,
                exceedsBudget);
        }

        static bool ValidFacet(
            PlatformSourceFacet expectedFacet,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformPopulationDemand population)
        {
            var identities = new HashSet<AssemblyReferenceIdentity>(
                AssemblyReferenceIdentity.EquivalentComparer);
            PlatformSourceContribution.Realization? populationContribution =
                items.Count == 0
                    ? null
                    : items[0].Library.Contribution;
            foreach (
                PlatformPopulationLibraryArtifactMaterializationItem item
                in items)
            {
                PlatformSourceContribution.Realization contribution =
                    item.Library.Contribution;
                if (!ReferenceEquals(
                        contribution,
                        populationContribution)
                    || contribution.Facet != expectedFacet
                    || contribution.RealizationCompleteness
                        != PlatformSourceContributionCompleteness
                            .Authoritative
                    || !ReferenceEquals(
                        contribution.Request,
                        request.Snapshot)
                    || contribution.Target != target
                    || !ReferenceEquals(
                        contribution.Population,
                        population)
                    || !request.Sources.Authorizes(
                        expectedFacet,
                        contribution.Capability)
                    || !identities.Add(item.Library.Identity))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
