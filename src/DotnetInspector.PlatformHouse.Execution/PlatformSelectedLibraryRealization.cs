using System.Diagnostics;
using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// One ephemeral source attempt to prepare a selected Platform Library view.
/// </summary>
public abstract class PlatformLibraryRealizationSourceAttempt :
    IPlatformSourcePolicyAttempt
{
    private protected PlatformLibraryRealizationSourceAttempt(
        PlatformSourceContribution contribution,
        PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.Facet
            is not PlatformSourceFacet.Reference
                and not PlatformSourceFacet.Implementation)
        {
            throw new ArgumentException(
                "A Library realization attempt requires reference or implementation evidence.",
                nameof(contribution));
        }
        ArgumentNullException.ThrowIfNull(consumedWork);
        if (consumedWork.SourceOperations != 0
            || consumedWork.TargetCandidates != 0
            || consumedWork.PortablePdbs != 0
            || consumedWork.SourceDocuments != 0
            || consumedWork.ForwardingHops != 0
            || consumedWork.TargetComparisons != 0)
        {
            throw new ArgumentException(
                "A realization attempt may report only assembly, XML, byte, and elapsed work; the executor owns operation and target accounting.",
                nameof(consumedWork));
        }
        Contribution = contribution;
        ConsumedWork = consumedWork;
    }

    public PlatformSourceContribution Contribution { get; }
    public PlatformHouseConsumedWork ConsumedWork { get; }

    bool IPlatformSourcePolicyAttempt.Succeeded =>
        this is Succeeded;

    PlatformHouseCandidateIdentity?
        IPlatformSourcePolicyAttempt.Candidate =>
        (this as Succeeded)?.Candidate;

    PlatformHouseRejectionKind?
        IPlatformSourcePolicyAttempt.RejectionKind =>
        (this as NotSucceeded)?.RejectionKind;

    public sealed class Succeeded :
        PlatformLibraryRealizationSourceAttempt
    {
        public Succeeded(
            PlatformSourceContribution.Realization contribution,
            PlatformHouseCandidateIdentity candidate,
            PlatformLibraryArtifactMaterializationItem item,
            PlatformHouseConsumedWork? consumedWork = null)
            : base(
                contribution,
                consumedWork ?? ItemWork(item))
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(item);
            if (!ReferenceEquals(item.Contribution, contribution))
            {
                throw new ArgumentException(
                    "A successful realization attempt requires the materialization item issued for its exact contribution.",
                    nameof(item));
            }
            Candidate = candidate;
            Item = item;
        }

        public new PlatformSourceContribution.Realization Contribution =>
            (PlatformSourceContribution.Realization)base.Contribution;
        public PlatformHouseCandidateIdentity Candidate { get; }
        public PlatformLibraryArtifactMaterializationItem Item { get; }

        static PlatformHouseConsumedWork ItemWork(
            PlatformLibraryArtifactMaterializationItem item) =>
            new(
                sourceOperations: 0,
                targetCandidates: 0,
                assemblies: 1,
                xmlDocuments:
                    item.CompiledXmlDocumentation is null ? 0 : 1,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes:
                    item.ContentLength
                    + (item.CompiledXmlDocumentation?.ContentLength
                        ?? 0),
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);
    }

    public sealed class NotSucceeded :
        PlatformLibraryRealizationSourceAttempt
    {
        public NotSucceeded(
            PlatformSourceContribution contribution,
            PlatformHouseRejectionKind? rejectionKind = null,
            PlatformHouseConsumedWork? consumedWork = null)
            : base(
                contribution,
                consumedWork ?? ZeroConsumed())
        {
            bool rejected =
                contribution is PlatformSourceContribution.Rejected;
            if (contribution
                    is not PlatformSourceContribution.Unavailable
                        and not PlatformSourceContribution.Rejected
                        and not PlatformSourceContribution.Incomplete
                        and not PlatformSourceContribution.Failed
                || rejected != rejectionKind.HasValue
                || rejectionKind is { } kind && !Enum.IsDefined(kind))
            {
                throw new ArgumentException(
                    "A terminal attempt requires matching terminal contribution and rejection evidence.",
                    nameof(contribution));
            }
            RejectionKind = rejectionKind;
        }

        public PlatformHouseRejectionKind? RejectionKind { get; }

        static PlatformHouseConsumedWork ZeroConsumed() =>
            new(
                sourceOperations: 0,
                targetCandidates: 0,
                assemblies: 0,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: 0,
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);
    }
}

public delegate ValueTask<PlatformLibraryRealizationSourceAttempt>
    PlatformLibraryRealizationSourceOperation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformHouseWorkBudget remainingWork,
        PlatformTargetDiscoveryCandidate? selectedAssociation);

/// <summary>
/// One lazily invoked source capability for a selected Library view.
/// </summary>
public sealed class PlatformLibraryRealizationSource
{
    private readonly PlatformLibraryRealizationSourceOperation _realize;

    public PlatformLibraryRealizationSource(
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet,
        PlatformLibraryRealizationSourceOperation realize,
        PlatformSourceCapabilityIdentity?
            selectedAssociationCapability = null,
        PlatformSourceAssociationRouteIdentity?
            selectedAssociationRoute = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (facet
            is not PlatformSourceFacet.Reference
                and not PlatformSourceFacet.Implementation)
        {
            throw new ArgumentOutOfRangeException(nameof(facet));
        }
        ArgumentNullException.ThrowIfNull(realize);
        if ((selectedAssociationCapability is null)
            != (selectedAssociationRoute is null))
        {
            throw new ArgumentException(
                "Selected association routing requires both an authorized discovery capability and its owner-issued route.",
                nameof(selectedAssociationRoute));
        }
        Capability = capability;
        Facet = facet;
        SelectedAssociationCapability =
            selectedAssociationCapability;
        SelectedAssociationRoute = selectedAssociationRoute;
        _realize = realize;
    }

    public PlatformSourceCapabilityIdentity Capability { get; }
    public PlatformSourceFacet Facet { get; }
    public PlatformSourceCapabilityIdentity?
        SelectedAssociationCapability { get; }
    public PlatformSourceAssociationRouteIdentity?
        SelectedAssociationRoute { get; }

    internal ValueTask<PlatformLibraryRealizationSourceAttempt>
        RealizeAsync(
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformHouseWorkBudget remainingWork,
            PlatformTargetDiscoveryCandidate? selectedAssociation) =>
        _realize(request, target, remainingWork, selectedAssociation);
}

/// <summary>
/// Selects a versionless target and realizes one Library in the same closed
/// PlatformHouse operation.
/// </summary>
public static class PlatformHouseSelectedLibraryExecutor
{
    public static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome> ExecuteAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformTargetDiscoverySource> discoverySources,
            IReadOnlyList<PlatformLibraryRealizationSource>
                realizationSources,
            string identityPrefix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discoverySources);
        ArgumentNullException.ThrowIfNull(realizationSources);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);

        request.CancellationToken.ThrowIfCancellationRequested();
        if (!TryIndexSources(
                request,
                realizationSources,
                out IReadOnlyDictionary<
                    SourceKey,
                    PlatformLibraryRealizationSource> indexed)
            || !AssociationRoutesCorrespond(
                discoverySources,
                realizationSources))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    ZeroConsumed(),
                    PlatformHouseRejectionKind.InvalidSourcePlan,
                    $"{identityPrefix}.source-set-invalid"));
        }

        PlatformLibraryArtifactMaterializationOutcome? owningResult = null;
        PlatformHouseOutcome<PlatformLibraryRealizationValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<
                PlatformLibraryRealizationValue>(
                    request,
                    discoverySources,
                    async selection =>
                    {
                        PlatformLibraryArtifactMaterializationOutcome result =
                            await ContinueAsync(
                                request,
                                selection,
                                indexed,
                                identityPrefix)
                            .ConfigureAwait(false);
                        owningResult = result;
                        return result.Realization.Outcome;
                    })
                .ConfigureAwait(false);

        if (owningResult is not null)
        {
            if (!ReferenceEquals(
                    owningResult.Realization.Outcome,
                    outcome))
            {
                throw new InvalidOperationException(
                    "Target selection did not preserve the continuation's exact terminal outcome.");
            }
            return owningResult;
        }

        return Terminal(
            new PlatformLibraryRealizationResult.Terminal(
                outcome,
                new PlatformLibraryRealizationReceipt(outcome.Receipt)));
    }

    static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome> ContinueAsync(
            PlatformHouseRequest request,
            PlatformTargetSelectionContext selection,
            IReadOnlyDictionary<
                SourceKey,
                PlatformLibraryRealizationSource> sources,
            string identityPrefix)
    {
        var work = new RealizationWork(selection.ConsumedWork);
        var selectedItems =
            new List<PlatformLibraryArtifactMaterializationItem>(2);
        var realizationSettlements =
            new List<PlatformSourceSettlement>();

        PlatformViewDemand view =
            ((PlatformHouseOperation.Realize)request.Operation).View;
        PlatformSourceFacet[] facets = view switch
        {
            PlatformViewDemand.Reference =>
                [PlatformSourceFacet.Reference],
            PlatformViewDemand.Implementation =>
                [PlatformSourceFacet.Implementation],
            PlatformViewDemand.ReferenceAndImplementation =>
            [
                PlatformSourceFacet.Reference,
                PlatformSourceFacet.Implementation,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };

        foreach (PlatformSourceFacet facet in facets)
        {
            PlatformSourceSelection plan =
                request.Sources.SelectionFor(facet)!;
            var attempts = new Dictionary<
                PlatformSourceCapabilityIdentity,
                PlatformLibraryRealizationSourceAttempt>(
                    ReferenceEqualityComparer.Instance);

            foreach (PlatformSourceCapabilityIdentity capability
                in plan.Capabilities)
            {
                if (!work.CanInvoke(request))
                {
                    return Incomplete(
                        request,
                        selection,
                        work.Consumed,
                        TerminalRealizationSettlements(
                            realizationSettlements),
                        $"{identityPrefix}.work-incomplete");
                }

                PlatformLibraryRealizationSource source =
                    sources[new(facet, capability)];
                PlatformHouseWorkBudget remainingWork =
                    work.Remaining(request);
                work.ChargeSourceOperation();
                PlatformLibraryRealizationSourceAttempt attempt =
                    await source.RealizeAsync(
                            request,
                            selection.Target,
                            remainingWork,
                            source.SelectedAssociationCapability is null
                                ? null
                                : selection.CandidateFor(
                                    source
                                        .SelectedAssociationCapability,
                                    source.SelectedAssociationRoute!))
                        .ConfigureAwait(false);
                bool withinBudget =
                    work.Charge(attempt.ConsumedWork, request);
                if (!AttemptCorresponds(
                        request,
                        selection.Target,
                        source,
                        attempt))
                {
                    if (!withinBudget)
                    {
                        return Incomplete(
                            request,
                            selection,
                            work.Consumed,
                            TerminalRealizationSettlements(
                                realizationSettlements),
                            $"{identityPrefix}.work-incomplete");
                    }
                    return Rejected(
                        request,
                        selection,
                        work.Consumed,
                        TerminalRealizationSettlements(
                            realizationSettlements),
                        PlatformHouseRejectionKind.InvalidOwnerResult,
                        $"{identityPrefix}.invalid-source-attempt");
                }
                attempts.Add(capability, attempt);
                if (!withinBudget)
                {
                    PlatformSourcePolicyDecision<
                        PlatformLibraryRealizationSourceAttempt> charged =
                        PlatformSourcePolicyReducer.Select(
                            plan,
                            attempts);
                    realizationSettlements.AddRange(
                        charged.Settlements);
                    return Incomplete(
                        request,
                        selection,
                        work.Consumed,
                        TerminalRealizationSettlements(
                            realizationSettlements),
                        $"{identityPrefix}.work-incomplete");
                }

                if (plan.Mode == PlatformSourceSelectionMode.Aggregation)
                    continue;
                if (attempt
                    is PlatformLibraryRealizationSourceAttempt.Succeeded)
                {
                    break;
                }
                if (attempt.Contribution
                    is PlatformSourceContribution.Rejected
                        or PlatformSourceContribution.Incomplete)
                {
                    break;
                }
                if (attempt.Contribution
                        is PlatformSourceContribution.Failed
                    && plan.Mode
                        == PlatformSourceSelectionMode.Precedence)
                {
                    break;
                }
            }

            PlatformSourcePolicyDecision<
                PlatformLibraryRealizationSourceAttempt> decision =
                PlatformSourcePolicyReducer.Select(plan, attempts);
            realizationSettlements.AddRange(decision.Settlements);
            if (decision.Kind != PlatformSourcePolicyDecisionKind.Selected)
            {
                return TerminalDecision(
                    request,
                    selection,
                    work.Consumed,
                    realizationSettlements,
                    decision,
                    identityPrefix);
            }

            selectedItems.Add(
                ((PlatformLibraryRealizationSourceAttempt.Succeeded)
                    decision.Selected!).Item);
        }

        IReadOnlyList<PlatformSourceSettlement> settlements =
        [
            .. selection.SourceSettlements,
            .. realizationSettlements,
        ];
        using CancellationTokenSource materializationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                request.CancellationToken);
        TimeSpan remainingDuration =
            work.Remaining(request).MaxDuration;
        if (remainingDuration
            <= TimeSpan.FromMilliseconds(uint.MaxValue - 1))
        {
            materializationCancellation.CancelAfter(
                remainingDuration);
        }
        return await PlatformHouseArtifactMaterializer
            .MaterializeSelectedAsync(
                request,
                view,
                selectedItems,
                work.Consumed,
                identityPrefix,
                selection,
                settlements,
                () => work.Consumed,
                materializationCancellation.Token)
            .ConfigureAwait(false);
    }

    static PlatformLibraryArtifactMaterializationOutcome TerminalDecision(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork consumedWork,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        PlatformSourcePolicyDecision<
            PlatformLibraryRealizationSourceAttempt> decision,
        string identityPrefix)
    {
        IReadOnlyList<PlatformSourceSettlement> terminalSettlements =
            TerminalRealizationSettlements(realizationSettlements);
        return decision.Kind switch
        {
            PlatformSourcePolicyDecisionKind.Unavailable =>
                Unavailable(
                    request,
                    selection,
                    consumedWork,
                    terminalSettlements,
                    $"{identityPrefix}.unavailable"),
            PlatformSourcePolicyDecisionKind.Rejected =>
                Rejected(
                    request,
                    selection,
                    consumedWork,
                    terminalSettlements,
                    decision.RejectionKind!.Value,
                    $"{identityPrefix}.rejected"),
            PlatformSourcePolicyDecisionKind.Incomplete =>
                Incomplete(
                    request,
                    selection,
                    consumedWork,
                    terminalSettlements,
                    $"{identityPrefix}.incomplete"),
            PlatformSourcePolicyDecisionKind.Failed =>
                Failed(
                    request,
                    selection,
                    consumedWork,
                    terminalSettlements,
                    $"{identityPrefix}.failed"),
            PlatformSourcePolicyDecisionKind.Ambiguous =>
                Ambiguous(
                    request,
                    selection,
                    consumedWork,
                    terminalSettlements,
                    decision.Candidates,
                    $"{identityPrefix}.ambiguous"),
            _ => throw new InvalidOperationException(
                "A selected policy decision requires materialization."),
        };
    }

    static bool AssociationRoutesCorrespond(
        IEnumerable<PlatformTargetDiscoverySource> discoverySources,
        IEnumerable<PlatformLibraryRealizationSource> realizationSources)
    {
        PlatformTargetDiscoverySource[] discovery = [.. discoverySources];
        foreach (PlatformLibraryRealizationSource realization
            in realizationSources)
        {
            if (realization?.SelectedAssociationCapability is not
                { } capability)
            {
                continue;
            }

            PlatformTargetDiscoverySource[] matches =
            [
                .. discovery.Where(
                    source => source is not null
                        && ReferenceEquals(
                            source.Capability,
                            capability)),
            ];
            if (matches.Length != 1
                || !ReferenceEquals(
                    matches[0].AssociationRoute,
                    realization.SelectedAssociationRoute))
            {
                return false;
            }
        }
        return true;
    }

    static bool TryIndexSources(
        PlatformHouseRequest request,
        IEnumerable<PlatformLibraryRealizationSource> sources,
        out IReadOnlyDictionary<
            SourceKey,
            PlatformLibraryRealizationSource> indexed)
    {
        var result = new Dictionary<
            SourceKey,
            PlatformLibraryRealizationSource>();
        foreach (PlatformLibraryRealizationSource source in sources)
        {
            if (source is null
                || !request.Sources.Authorizes(
                    source.Facet,
                    source.Capability)
                || source.SelectedAssociationCapability is not null
                    && !request.Target.AuthorizesDiscoveryCapability(
                        source.SelectedAssociationCapability)
                || !result.TryAdd(
                    new(source.Facet, source.Capability),
                    source))
            {
                indexed = result;
                return false;
            }
        }

        PlatformSourceFacet[] requiredFacets =
            request.Operation is PlatformHouseOperation.Realize operation
                ? operation.View switch
                {
                    PlatformViewDemand.Reference =>
                        [PlatformSourceFacet.Reference],
                    PlatformViewDemand.Implementation =>
                        [PlatformSourceFacet.Implementation],
                    PlatformViewDemand.ReferenceAndImplementation =>
                    [
                        PlatformSourceFacet.Reference,
                        PlatformSourceFacet.Implementation,
                    ],
                    _ => [],
                }
                : [];
        int requiredCount = requiredFacets.Sum(
            facet =>
                request.Sources.SelectionFor(facet)?.Capabilities.Count
                ?? 0);
        if (result.Count != requiredCount
            || requiredFacets.Any(
                facet =>
                    request.Sources.SelectionFor(facet) is null
                    || request.Sources.SelectionFor(facet)!.Capabilities.Any(
                        capability =>
                            !result.ContainsKey(new(facet, capability)))))
        {
            indexed = result;
            return false;
        }

        indexed = result;
        return true;
    }

    static bool AttemptCorresponds(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformLibraryRealizationSource source,
        PlatformLibraryRealizationSourceAttempt attempt) =>
        attempt is not null
        && ReferenceEquals(attempt.Contribution.Request, request.Snapshot)
        && attempt.Contribution.ExactTarget == target
        && attempt.Contribution.Facet == source.Facet
        && ReferenceEquals(
            attempt.Contribution.Capability,
            source.Capability);

    static IReadOnlyList<PlatformSourceSettlement>
        TerminalRealizationSettlements(
            IEnumerable<PlatformSourceSettlement> settlements) =>
        [.. settlements.Select(
            settlement =>
                settlement.Disposition
                    == PlatformSourceSettlementDisposition.Selected
                    ? new PlatformSourceSettlement(
                        settlement.Contribution,
                        PlatformSourceSettlementDisposition.OutcomeRelevant)
                    : settlement)];

    static PlatformLibraryArtifactMaterializationOutcome Rejected(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        PlatformHouseRejectionKind kind,
        string evidenceName) =>
        Terminal(
            PlatformHouseLibraryRealizer.Rejected(
                request,
                work,
                kind,
                evidenceName,
                selection.TargetSettlement,
                [
                    .. selection.SourceSettlements,
                    .. realizationSettlements,
                ]));

    static PlatformLibraryArtifactMaterializationOutcome Incomplete(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        string evidenceName) =>
        Terminal(
            PlatformHouseLibraryRealizer.Incomplete(
                request,
                work,
                evidenceName,
                selection.TargetSettlement,
                [
                    .. selection.SourceSettlements,
                    .. realizationSettlements,
                ]));

    static PlatformLibraryArtifactMaterializationOutcome Unavailable(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Unavailable(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        return TerminalOutcome(
            request,
            selection,
            work,
            realizationSettlements,
            termination,
            (receipt, terminal) =>
                new PlatformHouseOutcome<
                    PlatformLibraryRealizationValue>.Unavailable(
                        (PlatformHouseTermination.Unavailable)terminal,
                        receipt));
    }

    static PlatformLibraryArtifactMaterializationOutcome Failed(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        string evidenceName)
    {
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            [PlatformHouseFailureKind.Source],
            cancellationObserved: false);
        return TerminalOutcome(
            request,
            selection,
            work,
            realizationSettlements,
            termination,
            (receipt, terminal) =>
                new PlatformHouseOutcome<
                    PlatformLibraryRealizationValue>.Failed(
                        (PlatformHouseTermination.Failed)terminal,
                        receipt));
    }

    static PlatformLibraryArtifactMaterializationOutcome Ambiguous(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        IReadOnlyList<PlatformHouseCandidateIdentity> candidates,
        string evidenceName)
    {
        _ = evidenceName;
        var termination =
            new PlatformHouseTermination.Ambiguous(candidates);
        return TerminalOutcome(
            request,
            selection,
            work,
            realizationSettlements,
            termination,
            (receipt, terminal) =>
                new PlatformHouseOutcome<
                    PlatformLibraryRealizationValue>.Ambiguous(
                        (PlatformHouseTermination.Ambiguous)terminal,
                        receipt));
    }

    static PlatformLibraryArtifactMaterializationOutcome TerminalOutcome(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        PlatformHouseTermination termination,
        Func<
            PlatformHouseReceipt,
            PlatformHouseTermination,
            PlatformHouseOutcome<PlatformLibraryRealizationValue>> create)
    {
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            selection.TargetSettlement,
            [
                .. selection.SourceSettlements,
                .. realizationSettlements,
            ],
            work,
            termination: termination);
        return Terminal(
            new PlatformLibraryRealizationResult.Terminal(
                create(receipt, termination),
                new PlatformLibraryRealizationReceipt(receipt)));
    }

    static PlatformLibraryArtifactMaterializationOutcome Terminal(
        PlatformLibraryRealizationResult realization)
    {
        if (realization
            is not PlatformLibraryRealizationResult.Terminal terminal)
        {
            throw new ArgumentException(
                "A terminal materialization outcome requires a terminal realization.",
                nameof(realization));
        }
        return new PlatformLibraryArtifactMaterializationOutcome.Terminal(
            terminal);
    }

    static PlatformHouseConsumedWork ZeroConsumed() =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    readonly record struct SourceKey(
        PlatformSourceFacet Facet,
        PlatformSourceCapabilityIdentity Capability);

    sealed class RealizationWork
    {
        private readonly PlatformHouseConsumedWork _baseline;
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _sourceOperations;
        private int _assemblies;
        private int _xmlDocuments;
        private long _bytes;

        internal RealizationWork(
            PlatformHouseConsumedWork baseline) =>
            _baseline = baseline;

        internal PlatformHouseConsumedWork Consumed =>
            new(
                SaturatingAdd(
                    _baseline.SourceOperations,
                    _sourceOperations),
                _baseline.TargetCandidates,
                SaturatingAdd(
                    _baseline.Assemblies,
                    _assemblies),
                SaturatingAdd(
                    _baseline.XmlDocuments,
                    _xmlDocuments),
                _baseline.PortablePdbs,
                _baseline.SourceDocuments,
                SaturatingAdd(_baseline.Bytes, _bytes),
                _baseline.ForwardingHops,
                _baseline.TargetComparisons,
                SaturatingAdd(
                    _baseline.Elapsed,
                    Stopwatch.GetElapsedTime(_started)));

        internal bool CanInvoke(PlatformHouseRequest request)
        {
            PlatformHouseConsumedWork consumed = Consumed;
            return consumed.SourceOperations
                    < request.Work.MaxSourceOperations
                && consumed.Assemblies
                    < request.Work.MaxAssemblies
                && consumed.Bytes < request.Work.MaxBytes
                && consumed.Elapsed < request.Work.MaxDuration;
        }

        internal void ChargeSourceOperation() =>
            _sourceOperations = checked(_sourceOperations + 1);

        internal bool Charge(
            PlatformHouseConsumedWork consumed,
            PlatformHouseRequest request)
        {
            _assemblies = SaturatingAdd(
                _assemblies,
                consumed.Assemblies);
            _xmlDocuments = SaturatingAdd(
                _xmlDocuments,
                consumed.XmlDocuments);
            _bytes = SaturatingAdd(
                _bytes,
                consumed.Bytes);
            return !PlatformHouseLibraryRealizer.ExceedsBudget(
                Consumed,
                request);
        }

        internal PlatformHouseWorkBudget Remaining(
            PlatformHouseRequest request)
        {
            PlatformHouseConsumedWork consumed = Consumed;
            return new(
                Remaining(
                    request.Work.MaxSourceOperations,
                    consumed.SourceOperations),
                Remaining(
                    request.Work.MaxTargetCandidates,
                    consumed.TargetCandidates),
                Remaining(
                    request.Work.MaxAssemblies,
                    consumed.Assemblies),
                Remaining(
                    request.Work.MaxXmlDocuments,
                    consumed.XmlDocuments),
                Remaining(
                    request.Work.MaxPortablePdbs,
                    consumed.PortablePdbs),
                Remaining(
                    request.Work.MaxSourceDocuments,
                    consumed.SourceDocuments),
                Remaining(
                    request.Work.MaxBytes,
                    consumed.Bytes),
                Remaining(
                    request.Work.MaxForwardingHops,
                    consumed.ForwardingHops),
                Remaining(
                    request.Work.MaxDuration,
                    consumed.Elapsed));
        }

        static int SaturatingAdd(int left, int right)
        {
            long total = (long)left + right;
            return total > int.MaxValue
                ? int.MaxValue
                : (int)total;
        }

        static long SaturatingAdd(long left, long right) =>
            left > long.MaxValue - right
                ? long.MaxValue
                : left + right;

        static TimeSpan SaturatingAdd(
            TimeSpan left,
            TimeSpan right) =>
            left.Ticks > TimeSpan.MaxValue.Ticks - right.Ticks
                ? TimeSpan.MaxValue
                : left + right;

        static int Remaining(int maximum, int consumed) =>
            Math.Max(0, maximum - consumed);

        static long Remaining(long maximum, long consumed) =>
            Math.Max(0, maximum - consumed);

        static TimeSpan Remaining(
            TimeSpan maximum,
            TimeSpan consumed) =>
            consumed >= maximum
                ? TimeSpan.Zero
                : maximum - consumed;
    }
}
