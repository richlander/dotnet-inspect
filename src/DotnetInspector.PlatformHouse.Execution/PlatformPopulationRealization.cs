using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Resource-free selection of one assembly from an authoritative Platform
/// population.
/// </summary>
public sealed class PlatformPopulationLibraryContentSelection
{
    public PlatformPopulationLibraryContentSelection(
        ArtifactContentReference content,
        ArtifactAssemblyProjection projection,
        PlatformPopulationMemberAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(attribution);
        if (content.Provenance
                is not PlatformLibraryArtifactProvenance provenance)
        {
            throw new ArgumentException(
                "Selected population content must retain its Platform source realization as Artifact provenance.",
                nameof(content));
        }
        PlatformSourceContribution.Realization contribution =
            provenance.Contribution;
        if (!ReferenceEquals(
                projection.Registration.Generation,
                content.Generation)
            || !ReferenceEquals(
                projection.Registration.Artifact,
                content.Artifact))
        {
            throw new ArgumentException(
                "Selected population content requires the Metadata projection issued for its exact Artifact.",
                nameof(projection));
        }
        if (contribution.Request.Operation
                is not PlatformHouseOperationSnapshot.Realize
                {
                    Population:
                        PlatformPopulationDemand.CompletePopulation,
                } operation
            || !ReferenceEquals(
                contribution.Population,
                operation.Population)
            || contribution.RealizationCompleteness
                != PlatformSourceContributionCompleteness.Authoritative)
        {
            throw new ArgumentException(
                "Selected population content requires an authoritative complete-population realization contribution.",
                nameof(content));
        }

        var assemblyIdentity =
            new ManagedMetadataIdentity.Assembly(projection.Identity);
        if (assemblyIdentity.Identity.Version is null)
        {
            throw new ArgumentException(
                "Selected population content requires an exact assembly version.",
                nameof(projection));
        }

        Contribution = contribution;
        Content = content;
        Projection = projection;
        AssemblyIdentity = assemblyIdentity;
        Attribution = attribution;
    }

    public PlatformSourceContribution.Realization Contribution { get; }
    public ArtifactContentReference Content { get; }
    public ArtifactAssemblyProjection Projection { get; }
    public ManagedMetadataIdentity.Assembly AssemblyIdentity { get; }
    public PlatformPopulationMemberAttribution Attribution { get; }
}

/// <summary>
/// Resource-free value for one completed Platform population.
/// </summary>
public sealed class PlatformPopulationRealizationValue
{
    internal PlatformPopulationRealizationValue(
        IReadOnlyList<PlatformPopulationMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
        {
            throw new ArgumentException(
                "A completed Platform population requires at least one Library.",
                nameof(members));
        }
        Members = Array.AsReadOnly([.. members]);
        Libraries = Array.AsReadOnly(
            members.Select(static member => member.Library).ToArray());
    }

    public IReadOnlyList<PlatformPopulationMember> Members { get; }
    public IReadOnlyList<LibraryReference> Libraries { get; }
}

/// <summary>
/// Resource-free population receipt composed with the House settlement.
/// </summary>
public sealed class PlatformPopulationRealizationReceipt
{
    internal PlatformPopulationRealizationReceipt(
        PlatformHouseReceipt houseReceipt,
        IReadOnlyList<PlatformPopulationMember>? realizedMembers = null)
    {
        ArgumentNullException.ThrowIfNull(houseReceipt);
        bool completed = houseReceipt.SettlementKind
            == PlatformHouseSettlementKind.Completed;
        if (completed != (realizedMembers is not null))
        {
            throw new ArgumentException(
                "Only a completed population receipt may retain realized members.",
                nameof(realizedMembers));
        }
        if (realizedMembers is { Count: 0 })
        {
            throw new ArgumentException(
                "A completed population receipt requires at least one member.",
                nameof(realizedMembers));
        }

        HouseReceipt = houseReceipt;
        RealizedMembers = realizedMembers is null
            ? null
            : Array.AsReadOnly([.. realizedMembers]);
        RealizedLibraries = realizedMembers is null
            ? null
            : Array.AsReadOnly(
                realizedMembers
                    .Select(static member => member.Library)
                    .ToArray());
    }

    public PlatformHouseReceipt HouseReceipt { get; }
    public IReadOnlyList<PlatformPopulationMember>? RealizedMembers { get; }
    public IReadOnlyList<LibraryReference>? RealizedLibraries { get; }
}

/// <summary>
/// Pairs one closed population outcome with owners transferred only by
/// completion.
/// </summary>
public abstract class PlatformPopulationRealizationResult
{
    private protected PlatformPopulationRealizationResult(
        PlatformHouseOutcome<PlatformPopulationRealizationValue> outcome,
        PlatformPopulationRealizationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!ReferenceEquals(outcome.Receipt, receipt.HouseReceipt))
        {
            throw new ArgumentException(
                "The population result must retain its outcome's exact House receipt.",
                nameof(receipt));
        }

        Outcome = outcome;
        Receipt = receipt;
    }

    public PlatformHouseOutcome<PlatformPopulationRealizationValue> Outcome
    {
        get;
    }

    public PlatformPopulationRealizationReceipt Receipt { get; }

    public sealed class Completed : PlatformPopulationRealizationResult
    {
        internal Completed(
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Completed outcome,
            PlatformPopulationRealizationReceipt receipt,
            IReadOnlyList<LibraryContentOwner> owners)
            : base(outcome, receipt)
        {
            ArgumentNullException.ThrowIfNull(owners);
            if (owners.Count != outcome.Value.Members.Count
                || receipt.RealizedMembers is null
                || receipt.RealizedMembers.Count
                    != outcome.Value.Members.Count)
            {
                throw new ArgumentException(
                    "A completed population requires one owner and receipt reference per Library.",
                    nameof(owners));
            }
            for (int index = 0; index < owners.Count; index++)
            {
                PlatformPopulationMember member =
                    outcome.Value.Members[index];
                if (!ReferenceEquals(
                        owners[index].Reference,
                        member.Library)
                    || !ReferenceEquals(
                        receipt.RealizedMembers[index],
                        member))
                {
                    throw new ArgumentException(
                        "Population owners, values, and receipt references must correspond by exact index.",
                        nameof(owners));
                }
            }

            Value = outcome.Value;
            Owners = Array.AsReadOnly([.. owners]);
        }

        public PlatformPopulationRealizationValue Value { get; }
        public IReadOnlyList<LibraryContentOwner> Owners { get; }
    }

    public sealed class Terminal : PlatformPopulationRealizationResult
    {
        internal Terminal(
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue> outcome,
            PlatformPopulationRealizationReceipt receipt)
            : base(outcome, receipt)
        {
            if (outcome
                is PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Completed)
            {
                throw new ArgumentException(
                    "A completed population outcome requires transferred owners.",
                    nameof(outcome));
            }
            if (receipt.RealizedLibraries is not null)
            {
                throw new ArgumentException(
                    "A terminal population receipt cannot retain realized Libraries.",
                    nameof(receipt));
            }
        }
    }
}

/// <summary>
/// Performs the atomic ownership handoff for one complete population.
/// </summary>
public static class PlatformHousePopulationRealizer
{
    /// <summary>
    /// Accepts every supplied content lease and either transfers all resulting
    /// Library owners or retires every accepted authority before termination.
    /// </summary>
    public static async ValueTask<PlatformPopulationRealizationResult>
        RealizeReferencesAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections,
            IReadOnlyList<ArtifactContentLease> contentLeases,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformSourceContribution>?
                priorContributions = null)
        => await RealizeAsync(
                request,
                PlatformViewDemand.Reference,
                selections,
                contentLeases,
                [],
                [],
                consumedWork,
                priorContributions,
                targetSelection: null,
                retainedSettlements: null)
            .ConfigureAwait(false);

    internal static ValueTask<PlatformPopulationRealizationResult>
        RealizeSelectedReferencesAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections,
            IReadOnlyList<ArtifactContentLease> contentLeases,
            PlatformHouseConsumedWork consumedWork,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement> retainedSettlements) =>
        RealizeAsync(
            request,
            PlatformViewDemand.Reference,
            selections,
            contentLeases,
            [],
            [],
            consumedWork,
            priorContributions: null,
            targetSelection,
            retainedSettlements);

    /// <summary>
    /// Accepts every supplied reference and implementation content lease and
    /// either transfers their lossless paired population or retires every
    /// accepted authority before termination.
    /// </summary>
    public static async ValueTask<PlatformPopulationRealizationResult>
        RealizeReferenceAndImplementationAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                references,
            IReadOnlyList<ArtifactContentLease> referenceLeases,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                implementations,
            IReadOnlyList<ArtifactContentLease> implementationLeases,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformSourceContribution>?
                priorContributions = null)
        => await RealizeAsync(
                request,
                PlatformViewDemand.ReferenceAndImplementation,
                references,
                referenceLeases,
                implementations,
                implementationLeases,
                consumedWork,
                priorContributions,
                targetSelection: null,
                retainedSettlements: null)
            .ConfigureAwait(false);

    /// <summary>
    /// Accepts every supplied implementation content lease and either
    /// transfers all resulting Library owners or retires every accepted
    /// authority before termination.
    /// </summary>
    public static async ValueTask<PlatformPopulationRealizationResult>
        RealizeImplementationsAsync(
            PlatformHouseRequest request,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections,
            IReadOnlyList<ArtifactContentLease> contentLeases,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformSourceContribution>?
                priorContributions = null)
        => await RealizeAsync(
                request,
                PlatformViewDemand.Implementation,
                [],
                [],
                selections,
                contentLeases,
                consumedWork,
                priorContributions,
                targetSelection: null,
                retainedSettlements: null)
            .ConfigureAwait(false);

    static async ValueTask<PlatformPopulationRealizationResult>
        RealizeAsync(
            PlatformHouseRequest request,
            PlatformViewDemand expectedView,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                references,
            IReadOnlyList<ArtifactContentLease> referenceLeases,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                implementations,
            IReadOnlyList<ArtifactContentLease> implementationLeases,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformSourceContribution>?
                priorContributions,
            PlatformTargetSelectionContext? targetSelection,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(referenceLeases);
        ArgumentNullException.ThrowIfNull(implementations);
        ArgumentNullException.ThrowIfNull(implementationLeases);
        ArgumentNullException.ThrowIfNull(consumedWork);

        PlatformFamilyTarget? target = request.Target switch
        {
            PlatformTargetDemand.Exact exact
                when targetSelection is null
                    && retainedSettlements is null => exact.Target,
            PlatformTargetDemand.FamilyDefault demand
                when targetSelection is not null
                    && retainedSettlements is { Count: > 0 }
                    && ReferenceEquals(
                        targetSelection.TargetSettlement.Demand,
                        demand) =>
                targetSelection.Target,
            _ => null,
        };
        PlatformPopulationLibraryContentSelection[] referenceSelections =
            [.. references];
        ArtifactContentLease[] acceptedReferenceLeases =
            [.. referenceLeases];
        PlatformPopulationLibraryContentSelection[] implementationSelections =
            [.. implementations];
        ArtifactContentLease[] acceptedImplementationLeases =
            [.. implementationLeases];
        PlatformPopulationLibraryContentSelection[] selected =
            [.. referenceSelections, .. implementationSelections];
        ArtifactContentLease[] leases =
            [.. acceptedReferenceLeases, .. acceptedImplementationLeases];
        PlatformSourceContribution[] prior =
            [.. priorContributions ?? []];
        var owners = new List<LibraryContentOwner>(selected.Length);

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (target is null
                || request.Operation is not PlatformHouseOperation.Realize
                {
                    View: var view,
                    Population:
                        PlatformPopulationDemand.CompletePopulation,
                } operation)
            {
                return await RejectAfterCleanupAsync(
                        request,
                        consumedWork,
                        selected,
                        leases,
                        owners,
                        PlatformHouseRejectionKind.InvalidRequest,
                        "platform-population.invalid-request",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    .ConfigureAwait(false);
            }
            if (view != expectedView)
            {
                return await RejectAfterCleanupAsync(
                        request,
                        consumedWork,
                        selected,
                        leases,
                        owners,
                        PlatformHouseRejectionKind.InvalidRequest,
                        "platform-population.invalid-view",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    .ConfigureAwait(false);
            }
            if (PlatformHouseLibraryRealizer.ExceedsBudget(
                    consumedWork,
                    request))
            {
                return await IncompleteAfterCleanupAsync(
                        request,
                        consumedWork,
                        selected,
                        leases,
                        owners,
                        "platform-population.work-incomplete",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    .ConfigureAwait(false);
            }
            if (!ValidInputs(
                    request,
                    target,
                    operation.Population,
                    expectedView,
                    referenceSelections,
                    acceptedReferenceLeases,
                    implementationSelections,
                    acceptedImplementationLeases,
                    prior,
                    consumedWork))
            {
                return await RejectAfterCleanupAsync(
                        request,
                        consumedWork,
                        selected,
                        leases,
                        owners,
                        PlatformHouseRejectionKind.InvalidOwnerResult,
                        "platform-population.invalid-content",
                        targetSelection?.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    .ConfigureAwait(false);
            }

            PlatformSourceContribution.Realization[] contributions =
                DistinctContributions(selected);
            var selectedSettlements = contributions.Select(
                    contribution => new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition.Selected))
                .ToArray();
            List<PlatformSourceSettlement> settlements =
                retainedSettlements is null
                ? new List<PlatformSourceSettlement>(
                    prior.Length + selectedSettlements.Length)
                : [.. retainedSettlements];
            if (retainedSettlements is null)
            {
                settlements.AddRange(
                    prior.Select(
                        contribution => new PlatformSourceSettlement(
                            contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant)));
                settlements.AddRange(selectedSettlements);
            }
            else if (contributions.Any(
                contribution => !settlements.Any(
                    settlement =>
                        ReferenceEquals(
                            settlement.Contribution,
                            contribution)
                        && settlement.Disposition
                            == PlatformSourceSettlementDisposition
                                .Selected)))
            {
                return await RejectAfterCleanupAsync(
                        request,
                        consumedWork,
                        selected,
                        leases,
                        owners,
                        PlatformHouseRejectionKind.InvalidRetainedEvidence,
                        "platform-population.missing-selected-evidence",
                        targetSelection!.TargetSettlement,
                        TerminalRetainedSettlements(
                            targetSelection,
                            retainedSettlements))
                    .ConfigureAwait(false);
            }

            var members = new List<PlatformPopulationMember>(
                selected.Length);
            if (expectedView == PlatformViewDemand.Reference)
            {
                for (int index = 0;
                    index < referenceSelections.Length;
                    index++)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();
                    AddLibrary(
                        referenceSelections[index],
                        acceptedReferenceLeases[index],
                        implementation: null,
                        implementationLease: null,
                        owners,
                        members);
                }
            }
            else if (expectedView == PlatformViewDemand.Implementation)
            {
                for (int index = 0;
                    index < implementationSelections.Length;
                    index++)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();
                    AddLibrary(
                        implementationSelections[index],
                        acceptedImplementationLeases[index],
                        implementationSelections[index],
                        acceptedImplementationLeases[index],
                        owners,
                        members);
                }
            }
            else
            {
                AddPairedLibraries(
                    request,
                    referenceSelections,
                    acceptedReferenceLeases,
                    implementationSelections,
                    acceptedImplementationLeases,
                    owners,
                    members);
            }

            PlatformViewCorrespondenceEvidence? viewCorrespondence =
                expectedView switch
                {
                    PlatformViewDemand.Reference => null,
                    PlatformViewDemand.Implementation =>
                        new PlatformPopulationImplementationDeclarationSurface(
                            implementationSelections),
                    PlatformViewDemand.ReferenceAndImplementation =>
                        new PlatformPopulationReferenceAndImplementationCorrespondence(
                            referenceSelections,
                            implementationSelections),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(expectedView)),
                };
            var completion = new PlatformHouseCompletion.Realization(
                (PlatformHouseOperationSnapshot.Realize)
                    request.Snapshot.Operation,
                settlements.Where(
                    settlement => settlement.Disposition
                            == PlatformSourceSettlementDisposition.Selected
                        && settlement.Contribution
                            is PlatformSourceContribution.Realization),
                viewCorrespondence);
            var receipt = new PlatformHouseReceipt(
                request.Snapshot,
                (PlatformTargetSettlement?)
                    targetSelection?.TargetSettlement
                    ?? new PlatformTargetSettlement.Exact(
                        (PlatformTargetDemand.Exact)request.Target),
                settlements,
                consumedWork,
                completion);
            var value =
                new PlatformPopulationRealizationValue(members);
            return new PlatformPopulationRealizationResult.Completed(
                new PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Completed(
                        completion.Bind(value),
                        receipt),
                new PlatformPopulationRealizationReceipt(
                    receipt,
                    members),
                owners);
        }
        catch (OperationCanceledException)
            when (request.CancellationToken.IsCancellationRequested)
        {
            CleanupResult cleanup =
                await CleanupAsync(owners, leases).ConfigureAwait(false);
            if (cleanup.Failures.Count != 0)
            {
                return Failed(
                    request,
                    consumedWork,
                    RetainableContributions(request, selected),
                    cleanup.Kinds,
                    cancellationObserved: true,
                    "platform-population.cancellation-cleanup-failed",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements));
            }
            throw;
        }
        catch (ObjectDisposedException)
        {
            return await RejectAfterCleanupAsync(
                    request,
                    consumedWork,
                    selected,
                    leases,
                    owners,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    "platform-population.released-content",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements))
                .ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return await RejectAfterCleanupAsync(
                    request,
                    consumedWork,
                    selected,
                    leases,
                    owners,
                    PlatformHouseRejectionKind.InvalidRetainedEvidence,
                    "platform-population.invalid-retained-evidence",
                    targetSelection?.TargetSettlement,
                    TerminalRetainedSettlements(
                        targetSelection,
                        retainedSettlements))
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            CleanupResult cleanup =
                await CleanupAsync(owners, leases).ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(
                failure,
                cleanup.Failures);
            throw;
        }
    }

    static bool ValidInputs(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        PlatformViewDemand expectedView,
        IReadOnlyList<PlatformPopulationLibraryContentSelection>
            references,
        IReadOnlyList<ArtifactContentLease> referenceLeases,
        IReadOnlyList<PlatformPopulationLibraryContentSelection>
            implementations,
        IReadOnlyList<ArtifactContentLease> implementationLeases,
        IReadOnlyList<PlatformSourceContribution> prior,
        PlatformHouseConsumedWork consumedWork)
    {
        PlatformPopulationLibraryContentSelection[] selections =
            [.. references, .. implementations];
        ArtifactContentLease[] leases =
            [.. referenceLeases, .. implementationLeases];
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
        int sourceOperations = DistinctContributions(selections).Length;
        if (!validShape
            || referenceLeases.Count != references.Count
            || implementationLeases.Count != implementations.Count
            || selections.Length > request.Work.MaxAssemblies
            || consumedWork.Assemblies < selections.Length
            || consumedWork.SourceOperations < sourceOperations
            || prior.Any(
                contribution =>
                    contribution is null
                    || !ReferenceEquals(
                        contribution.Request,
                        request.Snapshot)
                    || contribution.Kind
                        is not PlatformSourceContributionKind.Unavailable
                            and not PlatformSourceContributionKind.Failed))
        {
            return false;
        }

        var contents = new HashSet<ArtifactContentReference>(
            ReferenceEqualityComparer.Instance);
        var authorities = new HashSet<ArtifactContentLease>(
            ReferenceEqualityComparer.Instance);
        ArtifactGenerationIdentity? generation = null;
        return ValidFacet(
                PlatformSourceFacet.Reference,
                references,
                referenceLeases,
                request,
                target,
                population,
                contents,
                authorities,
                ref generation)
            && ValidFacet(
                PlatformSourceFacet.Implementation,
                implementations,
                implementationLeases,
                request,
                target,
                population,
                contents,
                authorities,
                ref generation)
            && (expectedView
                    != PlatformViewDemand.ReferenceAndImplementation
                || ValidPairedAttributions(references, implementations));
    }

    static bool ValidFacet(
        PlatformSourceFacet expectedFacet,
        IReadOnlyList<PlatformPopulationLibraryContentSelection>
            selections,
        IReadOnlyList<ArtifactContentLease> leases,
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        HashSet<ArtifactContentReference> contents,
        HashSet<ArtifactContentLease> authorities,
        ref ArtifactGenerationIdentity? generation)
    {
        var identities = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        PlatformSourceContribution.Realization? populationContribution =
            selections.Count == 0
                ? null
                : selections[0].Contribution;
        for (int index = 0; index < selections.Count; index++)
        {
            PlatformPopulationLibraryContentSelection selection =
                selections[index];
            ArtifactContentLease lease = leases[index];
            PlatformSourceContribution.Realization contribution =
                selection.Contribution;
            generation ??= selection.Content.Generation;
            if (!ReferenceEquals(
                    contribution,
                    populationContribution)
                || contribution.Facet != expectedFacet
                || contribution.RealizationCompleteness
                    != PlatformSourceContributionCompleteness.Authoritative
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
                || !ReferenceEquals(
                    selection.Content.Generation,
                    generation)
                || !ReferenceEquals(
                    lease.Reference,
                    selection.Content)
                || !ValidAttribution(target, selection.Attribution)
                || !identities.Add(
                    selection.AssemblyIdentity.Identity)
                || !contents.Add(selection.Content)
                || !authorities.Add(lease))
            {
                return false;
            }
        }
        return true;
    }

    static bool ValidAttribution(
        PlatformFamilyTarget requestedTarget,
        PlatformPopulationMemberAttribution attribution) =>
        attribution.Role switch
        {
            PlatformPopulationMemberRole.Focus =>
                attribution.Target == requestedTarget,
            PlatformPopulationMemberRole.BindingSupport =>
                requestedTarget.Family == PlatformFamily.AspNetCore
                && attribution.Target.Family
                    == PlatformFamily.DotNetRuntime,
            _ => false,
        };

    static bool ValidPairedAttributions(
        IReadOnlyList<PlatformPopulationLibraryContentSelection> references,
        IReadOnlyList<PlatformPopulationLibraryContentSelection>
            implementations)
    {
        var implementationAttributions =
            implementations.ToDictionary(
                static selection => selection.AssemblyIdentity.Identity,
                static selection => selection.Attribution,
                AssemblyReferenceIdentity.EquivalentComparer);
        return references.All(
            reference =>
                !implementationAttributions.TryGetValue(
                    reference.AssemblyIdentity.Identity,
                    out PlatformPopulationMemberAttribution? implementation)
                || reference.Attribution.Equals(implementation));
    }

    static void AddPairedLibraries(
        PlatformHouseRequest request,
        IReadOnlyList<PlatformPopulationLibraryContentSelection> references,
        IReadOnlyList<ArtifactContentLease> referenceLeases,
        IReadOnlyList<PlatformPopulationLibraryContentSelection>
            implementations,
        IReadOnlyList<ArtifactContentLease> implementationLeases,
        ICollection<LibraryContentOwner> owners,
        ICollection<PlatformPopulationMember> members)
    {
        var implementationIndices =
            new Dictionary<AssemblyReferenceIdentity, int>(
                AssemblyReferenceIdentity.EquivalentComparer);
        for (int index = 0; index < implementations.Count; index++)
        {
            implementationIndices.Add(
                implementations[index].AssemblyIdentity.Identity,
                index);
        }

        var matched = new bool[implementations.Count];
        for (int index = 0; index < references.Count; index++)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            PlatformPopulationLibraryContentSelection reference =
                references[index];
            if (implementationIndices.TryGetValue(
                    reference.AssemblyIdentity.Identity,
                    out int implementationIndex))
            {
                matched[implementationIndex] = true;
                AddLibrary(
                    reference,
                    referenceLeases[index],
                    implementations[implementationIndex],
                    implementationLeases[implementationIndex],
                    owners,
                    members);
            }
            else
            {
                AddLibrary(
                    reference,
                    referenceLeases[index],
                    implementation: null,
                    implementationLease: null,
                    owners,
                    members);
            }
        }

        for (int index = 0; index < implementations.Count; index++)
        {
            if (matched[index])
                continue;
            request.CancellationToken.ThrowIfCancellationRequested();
            AddLibrary(
                implementations[index],
                implementationLeases[index],
                implementations[index],
                implementationLeases[index],
                owners,
                members);
        }
    }

    static void AddLibrary(
        PlatformPopulationLibraryContentSelection api,
        ArtifactContentLease apiLease,
        PlatformPopulationLibraryContentSelection? implementation,
        ArtifactContentLease? implementationLease,
        ICollection<LibraryContentOwner> owners,
        ICollection<PlatformPopulationMember> members)
    {
        if (implementation is not null
            && !api.Attribution.Equals(implementation.Attribution))
        {
            throw new ArgumentException(
                "Paired Platform population views must retain the same member attribution.",
                nameof(implementation));
        }

        var assemblyCorrespondence =
            new LibraryAssemblyCorrespondence(
                api.Content,
                api.AssemblyIdentity,
                implementation?.Content,
                implementation?.AssemblyIdentity);
        LibraryReference library =
            LibraryReference.CreateFromSource(
                new ExactLibrarySourceCoordinate.Platform(
                    new PlatformLibraryPopulationDeclaration(
                        api.Attribution.Target.Family),
                    api.AssemblyIdentity),
                assemblyCorrespondence);
        ArtifactContentLease[] leases =
            implementationLease is null
                || ReferenceEquals(apiLease, implementationLease)
            ? [apiLease]
            : [apiLease, implementationLease];
        owners.Add(new LibraryContentOwner(library, leases));
        members.Add(
            new PlatformPopulationMember(
                library,
                api.Attribution));
    }

    static PlatformSourceContribution.Realization[] DistinctContributions(
        IEnumerable<PlatformPopulationLibraryContentSelection>
            selections) =>
        selections.Select(static selection => selection.Contribution)
            .Distinct<PlatformSourceContribution.Realization>(
                ReferenceEqualityComparer.Instance)
            .ToArray();

    static IEnumerable<PlatformSourceContribution>
        RetainableContributions(
            PlatformHouseRequest request,
            IEnumerable<PlatformPopulationLibraryContentSelection>
                selections) =>
        DistinctContributions(selections)
            .Where(
                contribution => ReferenceEquals(
                    contribution.Request,
                    request.Snapshot));

    static async ValueTask<PlatformPopulationRealizationResult>
        RejectAfterCleanupAsync(
            PlatformHouseRequest request,
            PlatformHouseConsumedWork consumedWork,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections,
            IReadOnlyList<ArtifactContentLease> leases,
            IReadOnlyList<LibraryContentOwner> owners,
            PlatformHouseRejectionKind kind,
            string evidenceName,
            PlatformTargetSettlement? targetSettlement = null,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements = null)
    {
        CleanupResult cleanup =
            await CleanupAsync(owners, leases).ConfigureAwait(false);
        if (cleanup.Failures.Count != 0)
        {
            return Failed(
                request,
                consumedWork,
                RetainableContributions(request, selections),
                cleanup.Kinds,
                cancellationObserved: false,
                $"{evidenceName}.cleanup-failed",
                targetSettlement,
                retainedSettlements);
        }
        return Rejected(
            request,
            consumedWork,
            kind,
            evidenceName,
            targetSettlement,
            retainedSettlements);
    }

    static async ValueTask<PlatformPopulationRealizationResult>
        IncompleteAfterCleanupAsync(
            PlatformHouseRequest request,
            PlatformHouseConsumedWork consumedWork,
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections,
            IReadOnlyList<ArtifactContentLease> leases,
            IReadOnlyList<LibraryContentOwner> owners,
            string evidenceName,
            PlatformTargetSettlement? targetSettlement = null,
            IReadOnlyList<PlatformSourceSettlement>?
                retainedSettlements = null)
    {
        CleanupResult cleanup =
            await CleanupAsync(owners, leases).ConfigureAwait(false);
        if (cleanup.Failures.Count != 0)
        {
            return Failed(
                request,
                consumedWork,
                RetainableContributions(request, selections),
                cleanup.Kinds,
                cancellationObserved: false,
                $"{evidenceName}.cleanup-failed",
                targetSettlement,
                retainedSettlements);
        }
        return Incomplete(
            request,
            consumedWork,
            evidenceName,
            targetSettlement,
            retainedSettlements);
    }

    static async ValueTask<CleanupResult> CleanupAsync(
        IEnumerable<LibraryContentOwner> owners,
        IEnumerable<ArtifactContentLease> leases)
    {
        var failures = new List<Exception>();
        var kinds = new List<PlatformHouseFailureKind>();
        foreach (LibraryContentOwner owner in owners)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
                kinds.Add(PlatformHouseFailureKind.LibraryRetirement);
            }
        }
        foreach (ArtifactContentLease lease in leases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
                kinds.Add(PlatformHouseFailureKind.ArtifactRetirement);
            }
        }
        return new(
            failures,
            kinds.Distinct().ToArray());
    }

    internal static PlatformPopulationRealizationResult Rejected(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseRejectionKind kind,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements = null)
    {
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement
                ?? PlatformHouseLibraryRealizer.TargetSettlement(
                    request.Target),
            retainedSettlements ?? [],
            consumedWork,
            termination: termination);
        return new PlatformPopulationRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected(
                    termination,
                    receipt),
            new PlatformPopulationRealizationReceipt(receipt));
    }

    internal static PlatformPopulationRealizationResult Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements = null)
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement
                ?? PlatformHouseLibraryRealizer.TargetSettlement(
                    request.Target),
            retainedSettlements ?? [],
            consumedWork,
            termination: termination);
        return new PlatformPopulationRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete(
                    termination,
                    receipt),
            new PlatformPopulationRealizationReceipt(receipt));
    }

    internal static PlatformPopulationRealizationResult Failed(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceContribution> contributions,
        IEnumerable<PlatformHouseFailureKind> failures,
        bool cancellationObserved,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        PlatformSourceSettlement[] settlements =
            retainedSettlements?.ToArray()
            ?? contributions
                .Distinct<PlatformSourceContribution>(
                    ReferenceEqualityComparer.Instance)
                .Select(
                    contribution => new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition
                            .OutcomeRelevant))
                .ToArray();
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failures,
            cancellationObserved);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement
                ?? PlatformHouseLibraryRealizer.TargetSettlement(
                    request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformPopulationRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Failed(
                    termination,
                    receipt),
            new PlatformPopulationRealizationReceipt(receipt));
    }

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

    sealed record CleanupResult(
        IReadOnlyList<Exception> Failures,
        IReadOnlyList<PlatformHouseFailureKind> Kinds);

    sealed class PlatformPopulationImplementationDeclarationSurface :
        PlatformViewCorrespondenceEvidence
    {
        internal PlatformPopulationImplementationDeclarationSurface(
            IReadOnlyList<PlatformPopulationLibraryContentSelection>
                selections)
            : base(
                PlatformViewCorrespondenceIdentity.Issue(
                    "platform-population-implementation-declarations"))
        {
            if (selections.Count == 0
                || selections.Any(
                    static selection =>
                        selection.Contribution.Facet
                            != PlatformSourceFacet.Implementation))
            {
                throw new ArgumentException(
                    "Implementation declaration-surface evidence requires a non-empty implementation population.",
                    nameof(selections));
            }

            Selections = Array.AsReadOnly([.. selections]);
        }

        internal IReadOnlyList<
            PlatformPopulationLibraryContentSelection> Selections
        { get; }
    }

    sealed class
        PlatformPopulationReferenceAndImplementationCorrespondence :
        PlatformViewCorrespondenceEvidence
    {
        internal
            PlatformPopulationReferenceAndImplementationCorrespondence(
                IReadOnlyList<PlatformPopulationLibraryContentSelection>
                    references,
                IReadOnlyList<PlatformPopulationLibraryContentSelection>
                    implementations)
            : base(
                PlatformViewCorrespondenceIdentity.Issue(
                    "platform-population-reference-implementation"))
        {
            if (references.Count == 0
                || implementations.Count == 0
                || references.Any(
                    static selection =>
                        selection.Contribution.Facet
                            != PlatformSourceFacet.Reference)
                || implementations.Any(
                    static selection =>
                        selection.Contribution.Facet
                            != PlatformSourceFacet.Implementation))
            {
                throw new ArgumentException(
                    "Paired population correspondence requires non-empty reference and implementation populations.");
            }

            References = Array.AsReadOnly([.. references]);
            Implementations = Array.AsReadOnly([.. implementations]);
        }

        internal IReadOnlyList<
            PlatformPopulationLibraryContentSelection> References
        { get; }

        internal IReadOnlyList<
            PlatformPopulationLibraryContentSelection> Implementations
        { get; }
    }
}
