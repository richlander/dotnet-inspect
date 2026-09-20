using System.Diagnostics;
using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// One ephemeral source attempt to prepare an authoritative selected-target
/// reference population.
/// </summary>
public abstract class PlatformReferencePopulationRealizationSourceAttempt :
    IPlatformSourcePolicyAttempt
{
    private protected PlatformReferencePopulationRealizationSourceAttempt(
        PlatformSourceContribution contribution,
        PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.Facet != PlatformSourceFacet.Reference)
        {
            throw new ArgumentException(
                "A reference-population attempt requires Reference evidence.",
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
                "A reference-population attempt may report only assembly, XML, byte, and elapsed work; the executor owns operation and target accounting.",
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
        PlatformReferencePopulationRealizationSourceAttempt
    {
        public Succeeded(
            PlatformSourceContribution.Realization contribution,
            PlatformHouseCandidateIdentity candidate,
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items,
            PlatformHouseConsumedWork? consumedWork = null)
            : base(
                contribution,
                consumedWork ?? ItemsWork(items))
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(items);
            PlatformPopulationLibraryArtifactMaterializationItem[] snapshot =
                [.. items];
            if (snapshot.Length == 0
                || snapshot.Any(
                    item =>
                        !ReferenceEquals(
                            item.Library.Contribution,
                            contribution)
                        || item.Library.CompiledXmlDocumentation
                            is not null))
            {
                throw new ArgumentException(
                    "A successful reference-population attempt requires a non-empty assembly-only set of items issued for its exact contribution.",
                    nameof(items));
            }

            Candidate = candidate;
            Items = Array.AsReadOnly(snapshot);
        }

        public new PlatformSourceContribution.Realization Contribution =>
            (PlatformSourceContribution.Realization)base.Contribution;
        public PlatformHouseCandidateIdentity Candidate { get; }
        public IReadOnlyList<
            PlatformPopulationLibraryArtifactMaterializationItem> Items
        { get; }

        static PlatformHouseConsumedWork ItemsWork(
            IReadOnlyList<
                PlatformPopulationLibraryArtifactMaterializationItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            long bytes = 0;
            int xmlDocuments = 0;
            foreach (
                PlatformPopulationLibraryArtifactMaterializationItem item
                in items)
            {
                ArgumentNullException.ThrowIfNull(item);
                bytes = checked(bytes + item.Library.ContentLength);
                if (item.Library.CompiledXmlDocumentation
                    is { } documentation)
                {
                    xmlDocuments = checked(xmlDocuments + 1);
                    bytes = checked(
                        bytes + documentation.ContentLength);
                }
            }
            return new(
                sourceOperations: 0,
                targetCandidates: 0,
                assemblies: items.Count,
                xmlDocuments,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes,
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);
        }
    }

    public sealed class NotSucceeded :
        PlatformReferencePopulationRealizationSourceAttempt
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

public delegate ValueTask<
    PlatformReferencePopulationRealizationSourceAttempt>
    PlatformReferencePopulationRealizationSourceOperation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformHouseWorkBudget remainingWork,
        PlatformTargetDiscoveryCandidate? selectedAssociation);

/// <summary>
/// One lazily invoked complete-reference-population source capability.
/// </summary>
public sealed class PlatformReferencePopulationRealizationSource
{
    private readonly
        PlatformReferencePopulationRealizationSourceOperation _realize;

    public PlatformReferencePopulationRealizationSource(
        PlatformSourceCapabilityIdentity capability,
        PlatformReferencePopulationRealizationSourceOperation realize,
        PlatformSourceCapabilityIdentity?
            selectedAssociationCapability = null,
        PlatformSourceAssociationRouteIdentity?
            selectedAssociationRoute = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(realize);
        if ((selectedAssociationCapability is null)
            != (selectedAssociationRoute is null))
        {
            throw new ArgumentException(
                "Selected association routing requires both an authorized discovery capability and its owner-issued route.",
                nameof(selectedAssociationRoute));
        }

        Capability = capability;
        SelectedAssociationCapability =
            selectedAssociationCapability;
        SelectedAssociationRoute = selectedAssociationRoute;
        _realize = realize;
    }

    public PlatformSourceCapabilityIdentity Capability { get; }
    public PlatformSourceCapabilityIdentity?
        SelectedAssociationCapability { get; }
    public PlatformSourceAssociationRouteIdentity?
        SelectedAssociationRoute { get; }

    internal ValueTask<
        PlatformReferencePopulationRealizationSourceAttempt>
        RealizeAsync(
            PlatformHouseRequest request,
            PlatformFamilyTarget target,
            PlatformHouseWorkBudget remainingWork,
            PlatformTargetDiscoveryCandidate? selectedAssociation) =>
        _realize(request, target, remainingWork, selectedAssociation);
}

/// <summary>
/// Selects a versionless target and realizes its authoritative complete
/// reference population in the same closed PlatformHouse operation.
/// </summary>
public static class PlatformHouseSelectedReferencePopulationExecutor
{
    public static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome> ExecuteAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformTargetDiscoverySource> discoverySources,
            IReadOnlyList<PlatformReferencePopulationRealizationSource>
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
                    PlatformSourceCapabilityIdentity,
                    PlatformReferencePopulationRealizationSource> indexed)
            || !AssociationRoutesCorrespond(
                discoverySources,
                realizationSources))
        {
            return Terminal(
                PlatformHousePopulationRealizer.Rejected(
                    request,
                    ZeroConsumed(),
                    PlatformHouseRejectionKind.InvalidSourcePlan,
                    $"{identityPrefix}.source-set-invalid"));
        }

        PlatformPopulationArtifactMaterializationOutcome? owningResult = null;
        PlatformHouseOutcome<PlatformPopulationRealizationValue> outcome =
            await PlatformHouseTargetSelector.ExecuteAsync<
                PlatformPopulationRealizationValue>(
                    request,
                    discoverySources,
                    async selection =>
                    {
                        PlatformPopulationArtifactMaterializationOutcome result =
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
            new PlatformPopulationRealizationResult.Terminal(
                outcome,
                new PlatformPopulationRealizationReceipt(outcome.Receipt)));
    }

    static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome> ContinueAsync(
            PlatformHouseRequest request,
            PlatformTargetSelectionContext selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                PlatformReferencePopulationRealizationSource> sources,
            string identityPrefix)
    {
        var work = new RealizationWork(selection.ConsumedWork);
        PlatformSourceSelection plan =
            request.Sources.SelectionFor(PlatformSourceFacet.Reference)!;
        var attempts = new Dictionary<
            PlatformSourceCapabilityIdentity,
            PlatformReferencePopulationRealizationSourceAttempt>(
                ReferenceEqualityComparer.Instance);

        foreach (PlatformSourceCapabilityIdentity capability
            in plan.Capabilities)
        {
            if (!work.CanInvoke(request))
            {
                if (attempts.Count != 0)
                {
                    PlatformSourcePolicyDecision<
                        PlatformReferencePopulationRealizationSourceAttempt>
                        exhausted =
                            PlatformSourcePolicyReducer.Select(
                                plan,
                                attempts);
                    if (exhausted.Kind
                        == PlatformSourcePolicyDecisionKind.Failed)
                    {
                        return TerminalDecision(
                            request,
                            selection,
                            work.Consumed,
                            exhausted,
                            identityPrefix);
                    }
                }
                return Incomplete(
                    request,
                    selection,
                    work.Consumed,
                    PlatformSourcePolicyReducer.TerminalSettlements(
                        PlatformSourcePolicyReducer.Select(
                            plan,
                            attempts).Settlements),
                    $"{identityPrefix}.work-incomplete");
            }

            PlatformReferencePopulationRealizationSource source =
                sources[capability];
            PlatformHouseWorkBudget remainingWork =
                work.Remaining(request);
            work.ChargeSourceOperation();
            PlatformReferencePopulationRealizationSourceAttempt attempt =
                await source.RealizeAsync(
                        request,
                        selection.Target,
                        remainingWork,
                        source.SelectedAssociationCapability is null
                            ? null
                            : selection.CandidateFor(
                                source.SelectedAssociationCapability,
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
                        PlatformSourcePolicyReducer.TerminalSettlements(
                            PlatformSourcePolicyReducer.Select(
                                plan,
                                attempts).Settlements),
                        $"{identityPrefix}.work-incomplete");
                }
                return Rejected(
                    request,
                    selection,
                    work.Consumed,
                    PlatformSourcePolicyReducer.TerminalSettlements(
                        PlatformSourcePolicyReducer.Select(
                            plan,
                            attempts).Settlements),
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-attempt");
            }
            attempts.Add(capability, attempt);
            if (!withinBudget)
            {
                PlatformSourcePolicyDecision<
                    PlatformReferencePopulationRealizationSourceAttempt>
                    charged =
                        PlatformSourcePolicyReducer.Select(plan, attempts);
                if (charged.Kind
                    == PlatformSourcePolicyDecisionKind.Failed)
                {
                    return TerminalDecision(
                        request,
                        selection,
                        work.Consumed,
                        charged,
                        identityPrefix);
                }
                return Incomplete(
                    request,
                    selection,
                    work.Consumed,
                    PlatformSourcePolicyReducer.TerminalSettlements(
                        charged.Settlements),
                    $"{identityPrefix}.work-incomplete");
            }

            if (plan.Mode == PlatformSourceSelectionMode.Aggregation)
                continue;
            if (attempt
                is PlatformReferencePopulationRealizationSourceAttempt
                    .Succeeded)
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
            PlatformReferencePopulationRealizationSourceAttempt> decision =
                PlatformSourcePolicyReducer.Select(plan, attempts);
        if (decision.Kind != PlatformSourcePolicyDecisionKind.Selected)
        {
            return TerminalDecision(
                request,
                selection,
                work.Consumed,
                decision,
                identityPrefix);
        }

        IReadOnlyList<PlatformSourceSettlement> settlements =
        [
            .. selection.SourceSettlements,
            .. decision.Settlements,
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
        var selected =
            (PlatformReferencePopulationRealizationSourceAttempt.Succeeded)
                decision.Selected!;
        return await PlatformHousePopulationArtifactMaterializer
            .MaterializeSelectedReferencesAsync(
                request,
                selected.Items,
                work.Consumed,
                identityPrefix,
                selection,
                settlements,
                () => work.Consumed,
                materializationCancellation.Token)
            .ConfigureAwait(false);
    }

    static PlatformPopulationArtifactMaterializationOutcome TerminalDecision(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork consumedWork,
        PlatformSourcePolicyDecision<
            PlatformReferencePopulationRealizationSourceAttempt> decision,
        string identityPrefix)
    {
        IReadOnlyList<PlatformSourceSettlement> terminalSettlements =
            PlatformSourcePolicyReducer.TerminalSettlements(
                decision.Settlements);
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
                    decision.Candidates),
            _ => throw new InvalidOperationException(
                "A selected policy decision requires materialization."),
        };
    }

    static bool AssociationRoutesCorrespond(
        IEnumerable<PlatformTargetDiscoverySource> discoverySources,
        IEnumerable<PlatformReferencePopulationRealizationSource>
            realizationSources)
    {
        PlatformTargetDiscoverySource[] discovery = [.. discoverySources];
        foreach (
            PlatformReferencePopulationRealizationSource realization
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
        IEnumerable<PlatformReferencePopulationRealizationSource> sources,
        out IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformReferencePopulationRealizationSource> indexed)
    {
        var result = new Dictionary<
            PlatformSourceCapabilityIdentity,
            PlatformReferencePopulationRealizationSource>(
                ReferenceEqualityComparer.Instance);
        PlatformSourceSelection? plan =
            request.Sources.SelectionFor(PlatformSourceFacet.Reference);
        if (request.Target is not PlatformTargetDemand.FamilyDefault
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: PlatformViewDemand.Reference,
                Population:
                    PlatformPopulationDemand.CompletePopulation,
            }
            || plan is null)
        {
            indexed = result;
            return false;
        }

        foreach (
            PlatformReferencePopulationRealizationSource source
            in sources)
        {
            if (source is null
                || !request.Sources.Authorizes(
                    PlatformSourceFacet.Reference,
                    source.Capability)
                || source.SelectedAssociationCapability is not null
                    && !request.Target.AuthorizesDiscoveryCapability(
                        source.SelectedAssociationCapability)
                || !result.TryAdd(source.Capability, source))
            {
                indexed = result;
                return false;
            }
        }

        if (result.Count != plan.Capabilities.Count
            || plan.Capabilities.Any(
                capability => !result.ContainsKey(capability)))
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
        PlatformReferencePopulationRealizationSource source,
        PlatformReferencePopulationRealizationSourceAttempt attempt) =>
        attempt is not null
        && ReferenceEquals(attempt.Contribution.Request, request.Snapshot)
        && attempt.Contribution.ExactTarget == target
        && attempt.Contribution.Facet == PlatformSourceFacet.Reference
        && ReferenceEquals(
            attempt.Contribution.Capability,
            source.Capability);

    static PlatformPopulationArtifactMaterializationOutcome Rejected(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        PlatformHouseRejectionKind kind,
        string evidenceName) =>
        Terminal(
            PlatformHousePopulationRealizer.Rejected(
                request,
                work,
                kind,
                evidenceName,
                selection.TargetSettlement,
                [
                    .. selection.SourceSettlements,
                    .. realizationSettlements,
                ]));

    static PlatformPopulationArtifactMaterializationOutcome Incomplete(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        string evidenceName) =>
        Terminal(
            PlatformHousePopulationRealizer.Incomplete(
                request,
                work,
                evidenceName,
                selection.TargetSettlement,
                [
                    .. selection.SourceSettlements,
                    .. realizationSettlements,
                ]));

    static PlatformPopulationArtifactMaterializationOutcome Unavailable(
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
                    PlatformPopulationRealizationValue>.Unavailable(
                        (PlatformHouseTermination.Unavailable)terminal,
                        receipt));
    }

    static PlatformPopulationArtifactMaterializationOutcome Failed(
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
                    PlatformPopulationRealizationValue>.Failed(
                        (PlatformHouseTermination.Failed)terminal,
                        receipt));
    }

    static PlatformPopulationArtifactMaterializationOutcome Ambiguous(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        IReadOnlyList<PlatformHouseCandidateIdentity> candidates)
    {
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
                    PlatformPopulationRealizationValue>.Ambiguous(
                        (PlatformHouseTermination.Ambiguous)terminal,
                        receipt));
    }

    static PlatformPopulationArtifactMaterializationOutcome TerminalOutcome(
        PlatformHouseRequest request,
        PlatformTargetSelectionContext selection,
        PlatformHouseConsumedWork work,
        IReadOnlyList<PlatformSourceSettlement> realizationSettlements,
        PlatformHouseTermination termination,
        Func<
            PlatformHouseReceipt,
            PlatformHouseTermination,
            PlatformHouseOutcome<PlatformPopulationRealizationValue>> create)
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
            new PlatformPopulationRealizationResult.Terminal(
                create(receipt, termination),
                new PlatformPopulationRealizationReceipt(receipt)));
    }

    static PlatformPopulationArtifactMaterializationOutcome Terminal(
        PlatformPopulationRealizationResult realization)
    {
        if (realization
            is not PlatformPopulationRealizationResult.Terminal terminal)
        {
            throw new ArgumentException(
                "A terminal materialization outcome requires a terminal realization.",
                nameof(realization));
        }
        return new PlatformPopulationArtifactMaterializationOutcome.Terminal(
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
