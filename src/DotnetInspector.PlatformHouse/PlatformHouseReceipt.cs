using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>How one contribution affected settlement for its exact facet.</summary>
public enum PlatformSourceSettlementDisposition
{
    Selected,
    Shadowed,
    OutcomeRelevant,
}

/// <summary>One contribution and its explicit settlement disposition.</summary>
public sealed class PlatformSourceSettlement
{
    internal PlatformSourceSettlement(
        PlatformSourceContribution contribution,
        PlatformSourceSettlementDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        Contribution = contribution;
        Disposition = disposition;
    }

    public PlatformSourceContribution Contribution { get; }
    public PlatformSourceSettlementDisposition Disposition { get; }
}

/// <summary>Target demand, owner evidence, and exact settlement.</summary>
public abstract class PlatformTargetSettlement
{
    private protected PlatformTargetSettlement(
        PlatformTargetDemand demand,
        PlatformFamilyTarget? settledTarget)
    {
        ArgumentNullException.ThrowIfNull(demand);
        if (settledTarget is not null)
            ValidateCorrespondence(settledTarget, demand, nameof(settledTarget));
        Demand = demand;
        SettledTarget = settledTarget;
    }

    public PlatformTargetDemand Demand { get; }
    public PlatformFamilyTarget? SettledTarget { get; }
    internal virtual IReadOnlyList<PlatformSourceContribution>
        SelectedDiscoveries => Array.Empty<PlatformSourceContribution>();

    public sealed class Exact : PlatformTargetSettlement
    {
        internal Exact(PlatformTargetDemand.Exact demand)
            : base(demand, demand.Target)
        {
        }
    }

    public sealed class Selected : PlatformTargetSettlement
    {
        internal Selected(
            PlatformTargetDemand demand,
            PlatformFamilyTarget target,
            IEnumerable<PlatformSourceContribution> discoveries)
            : base(demand, target)
        {
            if (!demand.RequiresDiscovery)
            {
                throw new ArgumentException(
                    "Target selection requires a selecting target demand.",
                    nameof(demand));
            }
            ArgumentNullException.ThrowIfNull(discoveries);
            PlatformSourceContribution[] snapshot = [.. discoveries];
            if (snapshot.Length == 0)
            {
                throw new ArgumentException(
                    "Target selection requires retained discovery contributions.",
                    nameof(discoveries));
            }
            var seen = new HashSet<PlatformSourceContribution>(
                ReferenceEqualityComparer.Instance);
            foreach (PlatformSourceContribution discovery in snapshot)
            {
                ArgumentNullException.ThrowIfNull(discovery, nameof(discoveries));
                if (!seen.Add(discovery))
                {
                    throw new ArgumentException(
                        "Target selection cannot retain one discovery contribution more than once.",
                        nameof(discoveries));
                }
                if (discovery.Kind
                        != PlatformSourceContributionKind.TargetDiscovery
                    || !ReferenceEquals(discovery.RequestedTarget, demand))
                {
                    throw new ArgumentException(
                        "Target selection must retain discovery evidence for the same demand.",
                        nameof(discoveries));
                }
            }
            PlatformSourceContribution[] offeringDiscoveries =
                [.. snapshot.Where(
                    discovery =>
                        discovery.DiscoveredCandidates.Contains(target))];
            if (offeringDiscoveries.Length == 0)
            {
                throw new ArgumentException(
                    "The selected target must occur in retained discovery evidence.",
                    nameof(target));
            }
            if (offeringDiscoveries.Any(
                discovery => !demand.IsEligibleSelection(
                    discovery.Capability,
                    target)))
            {
                throw new ArgumentException(
                    "The selected target is not eligible under its demand stage.",
                    nameof(target));
            }

            Discoveries = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<PlatformSourceContribution> Discoveries { get; }

        internal override IReadOnlyList<PlatformSourceContribution>
            SelectedDiscoveries => Discoveries;
    }

    public sealed class Unsettled : PlatformTargetSettlement
    {
        internal Unsettled(PlatformTargetDemand demand)
            : base(demand, null)
        {
        }
    }

    static void ValidateCorrespondence(
        PlatformFamilyTarget target,
        PlatformTargetDemand demand,
        string parameterName)
    {
        if (!demand.CorrespondsToTarget(target))
        {
            throw new ArgumentException(
                "Target settlement evidence does not correspond to the retained demand.",
                parameterName);
        }
    }
}

/// <summary>The terminal status represented by a receipt.</summary>
public enum PlatformHouseSettlementKind
{
    Completed,
    Unavailable,
    Ambiguous,
    Rejected,
    Incomplete,
    Failed,
}

/// <summary>Which required House stage could not settle.</summary>
public enum PlatformHouseFailureKind
{
    Source,
    ArtifactPublication,
    ArtifactRetirement,
    Metadata,
    LibraryConstruction,
    LibraryBorrow,
    LibraryLeaseSettlement,
    LibraryRetirement,
    LibraryChildRelease,
}

/// <summary>Terminal assembly-reference forms that count as completed.</summary>
public enum PlatformAssemblyReferenceCompletionKind
{
    Resolved,
    NoNameOwner,
}

/// <summary>The closed completed shapes of type-definition resolution.</summary>
public enum PlatformTypeDefinitionCompletionKind
{
    Reference,
    Implementation,
    ReferenceAndImplementation,
}

/// <summary>
/// House-controlled, resource-free proof that the exact operation reached its
/// completed contract.
/// </summary>
public abstract class PlatformHouseCompletion
{
    private protected PlatformHouseCompletion(
        PlatformHouseOperationSnapshot operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Operation = operation;
        Identity = new PlatformHouseCompletionIdentity();
    }

    public PlatformHouseOperationSnapshot Operation { get; }
    public PlatformHouseCompletionIdentity Identity { get; }
    internal virtual IReadOnlyList<PlatformSourceSettlement>
        RequiredSourceSettlements => Array.Empty<PlatformSourceSettlement>();

    public sealed class Realization : PlatformHouseCompletion
    {
        internal Realization(
            PlatformHouseOperationSnapshot.Realize operation,
            IEnumerable<PlatformSourceSettlement> selectedContributions,
            PlatformViewCorrespondenceEvidence? viewCorrespondence = null)
            : base(operation)
        {
            SelectedContributions = ValidateSelectedRealizations(
                operation.View,
                operation.Population,
                selectedContributions,
                viewCorrespondence?.Identity);
            ViewCorrespondence = viewCorrespondence?.Identity;
        }

        public IReadOnlyList<PlatformSourceSettlement> SelectedContributions
        {
            get;
        }
        public PlatformViewCorrespondenceIdentity? ViewCorrespondence { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements => SelectedContributions;

        internal PlatformHouseCompletedValue<TValue> Bind<TValue>(TValue value)
            where TValue : notnull =>
            new(Identity, value);
    }

    public sealed class AssemblyReference : PlatformHouseCompletion
    {
        internal AssemblyReference(
            PlatformHouseOperationSnapshot.ResolveAssemblyReference operation,
            PlatformAssemblyReferenceCompletionKind kind,
            PlatformMetadataOutcomeEvidence metadataOutcome,
            IEnumerable<PlatformSourceSettlement> sourceSettlements,
            PlatformViewCorrespondenceEvidence? viewCorrespondence = null)
            : base(operation)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ArgumentNullException.ThrowIfNull(metadataOutcome);
            ArgumentNullException.ThrowIfNull(sourceSettlements);

            PlatformSourceSettlement[] snapshot = [.. sourceSettlements];
            if (kind == PlatformAssemblyReferenceCompletionKind.NoNameOwner)
            {
                if (viewCorrespondence is not null
                    || metadataOutcome.TerminalSupplier is not null
                    || metadataOutcome.Correspondence is not null)
                {
                    throw new ArgumentException(
                        "NoNameOwner completion has no selected platform supplier.",
                        nameof(sourceSettlements));
                }
            }
            else
            {
                _ = ValidateSelectedRealizations(
                    operation.RequiredView,
                    population: null,
                    snapshot,
                    viewCorrespondence?.Identity);
                if (!snapshot.Any(
                        settlement => ReferenceEquals(
                            settlement.Contribution,
                            metadataOutcome.TerminalSupplier)
                            && PlatformHouseReceipt.SupplierSatisfiesView(
                                settlement.Contribution,
                                operation.RequiredView))
                    || !ReferenceEquals(
                        metadataOutcome.Correspondence,
                        viewCorrespondence?.Identity))
                {
                    throw new ArgumentException(
                        "Resolved Metadata evidence must retain its selected physical supplier and view correspondence.",
                        nameof(metadataOutcome));
                }
            }

            Kind = kind;
            MetadataOutcome = metadataOutcome.Identity;
            SourceSettlements = Array.AsReadOnly(snapshot);
            ViewCorrespondence = viewCorrespondence?.Identity;
        }

        public PlatformAssemblyReferenceCompletionKind Kind { get; }
        public PlatformMetadataOutcomeIdentity MetadataOutcome { get; }
        public IReadOnlyList<PlatformSourceSettlement> SourceSettlements
        {
            get;
        }
        public PlatformViewCorrespondenceIdentity? ViewCorrespondence { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements => SourceSettlements;

        internal PlatformHouseCompletedValue<TOutcome> Bind<TOutcome>(
            PlatformMetadataOutcomeEvidence<TOutcome> outcome)
            where TOutcome : notnull
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (!ReferenceEquals(MetadataOutcome, outcome.Identity))
            {
                throw new ArgumentException(
                    "The live Metadata outcome must match the completion evidence.",
                    nameof(outcome));
            }
            return new(Identity, outcome.Value);
        }
    }

    public sealed class TypeDefinition : PlatformHouseCompletion
    {
        internal TypeDefinition(
            PlatformHouseOperationSnapshot.ResolveTypeDefinition operation,
            PlatformMetadataOutcomeEvidence startingOutcome,
            PlatformMetadataOutcomeEvidence? implementationOutcome,
            IEnumerable<PlatformSourceSettlement> selectedContributions,
            PlatformViewCorrespondenceEvidence? correspondence)
            : base(operation)
        {
            ArgumentNullException.ThrowIfNull(startingOutcome);
            ArgumentNullException.ThrowIfNull(selectedContributions);
            PlatformSourceSettlement[] snapshot = [.. selectedContributions];
            if (operation.StartingView == PlatformViewDemand.Implementation)
            {
                if (operation.RequiredView != PlatformViewDemand.Implementation
                    || implementationOutcome is not null
                    || snapshot.Length != 0
                    || correspondence is not null
                    || startingOutcome.TerminalSupplier is not null
                    || startingOutcome.Correspondence is not null)
                {
                    throw new ArgumentException(
                        "Direct implementation resolution requires one Metadata outcome and no view transition evidence.",
                        nameof(startingOutcome));
                }
                Kind = PlatformTypeDefinitionCompletionKind.Implementation;
                ReferenceOutcome = null;
                ImplementationOutcome = startingOutcome.Identity;
            }
            else if (operation.StartingView == PlatformViewDemand.Reference
                && operation.RequiredView
                    is PlatformViewDemand.Implementation
                        or PlatformViewDemand.ReferenceAndImplementation)
            {
                ArgumentNullException.ThrowIfNull(implementationOutcome);
                if (ReferenceEquals(
                        startingOutcome.Identity,
                        implementationOutcome.Identity))
                {
                    throw new ArgumentException(
                        "Reference and implementation resolution require distinct Metadata outcomes.",
                        nameof(implementationOutcome));
                }
                ArgumentNullException.ThrowIfNull(correspondence);
                _ = ValidateSelectedRealizations(
                    PlatformViewDemand.Implementation,
                    population: null,
                    snapshot,
                    viewCorrespondence: null);
                PlatformSourceSettlement[] matchingSuppliers = [.. snapshot
                    .Where(
                        settlement => ReferenceEquals(
                            settlement.Contribution,
                            implementationOutcome.TerminalSupplier))];
                if (matchingSuppliers.Length != 1
                    || matchingSuppliers[0].Contribution.SuppliedView
                        != PlatformViewDemand.Implementation
                    || !ReferenceEquals(
                        implementationOutcome.Correspondence,
                        correspondence.Identity))
                {
                    throw new ArgumentException(
                        "The implementation Metadata outcome must retain the selected physical supplier and exact view correspondence.",
                        nameof(implementationOutcome));
                }
                Kind = PlatformTypeDefinitionCompletionKind
                    .ReferenceAndImplementation;
                ReferenceOutcome = startingOutcome.Identity;
                ImplementationOutcome = implementationOutcome.Identity;
            }
            else if (operation.StartingView == PlatformViewDemand.Reference
                && operation.RequiredView == PlatformViewDemand.Reference)
            {
                if (implementationOutcome is not null
                    || snapshot.Length != 0
                    || correspondence is not null
                    || startingOutcome.TerminalSupplier is not null
                    || startingOutcome.Correspondence is not null)
                {
                    throw new ArgumentException(
                        "Reference-only type resolution requires one Metadata outcome and no implementation evidence.",
                        nameof(startingOutcome));
                }
                Kind = PlatformTypeDefinitionCompletionKind.Reference;
                ReferenceOutcome = startingOutcome.Identity;
                ImplementationOutcome = null;
            }
            else
            {
                throw new ArgumentException(
                    "The type-resolution start and required views are unsupported.",
                    nameof(operation));
            }
            SelectedContributions = Array.AsReadOnly(snapshot);
            Correspondence = correspondence?.Identity;
        }

        public PlatformTypeDefinitionCompletionKind Kind { get; }
        public PlatformMetadataOutcomeIdentity? ReferenceOutcome { get; }
        public PlatformMetadataOutcomeIdentity? ImplementationOutcome { get; }
        public IReadOnlyList<PlatformSourceSettlement> SelectedContributions
        {
            get;
        }
        public PlatformViewCorrespondenceIdentity? Correspondence { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements => SelectedContributions;

        internal PlatformHouseCompletedValue<
            PlatformTypeDefinitionValue.Reference<TOutcome>>
            BindReference<TOutcome>(
                PlatformMetadataOutcomeEvidence<TOutcome> referenceOutcome)
            where TOutcome : notnull
        {
            ArgumentNullException.ThrowIfNull(referenceOutcome);
            if (ImplementationOutcome is not null
                || !ReferenceEquals(
                    ReferenceOutcome,
                    referenceOutcome.Identity))
            {
                throw new ArgumentException(
                    "The live reference outcome must match a reference-only completion.",
                    nameof(referenceOutcome));
            }
            return new(
                Identity,
                new PlatformTypeDefinitionValue.Reference<TOutcome>(
                    referenceOutcome.Value));
        }

        internal PlatformHouseCompletedValue<
            PlatformTypeDefinitionValue.Implementation<TOutcome>>
            BindImplementation<TOutcome>(
                PlatformMetadataOutcomeEvidence<TOutcome>
                    implementationOutcome)
            where TOutcome : notnull
        {
            ArgumentNullException.ThrowIfNull(implementationOutcome);
            if (Kind != PlatformTypeDefinitionCompletionKind.Implementation
                || ReferenceOutcome is not null
                || !ReferenceEquals(
                    ImplementationOutcome,
                    implementationOutcome.Identity))
            {
                throw new ArgumentException(
                    "The live implementation outcome must match an implementation-only completion.",
                    nameof(implementationOutcome));
            }
            return new(
                Identity,
                new PlatformTypeDefinitionValue.Implementation<TOutcome>(
                    implementationOutcome.Value));
        }

        internal PlatformHouseCompletedValue<
            PlatformTypeDefinitionValue.ReferenceAndImplementation<
                TReferenceOutcome,
                TImplementationOutcome>>
            BindReferenceAndImplementation<
                TReferenceOutcome,
                TImplementationOutcome>(
                PlatformMetadataOutcomeEvidence<TReferenceOutcome>
                    referenceOutcome,
                PlatformMetadataOutcomeEvidence<TImplementationOutcome>
                    implementationOutcome)
            where TReferenceOutcome : notnull
            where TImplementationOutcome : notnull
        {
            ArgumentNullException.ThrowIfNull(referenceOutcome);
            ArgumentNullException.ThrowIfNull(implementationOutcome);
            if (!ReferenceEquals(
                    ReferenceOutcome,
                    referenceOutcome.Identity)
                || !ReferenceEquals(
                    ImplementationOutcome,
                    implementationOutcome.Identity))
            {
                throw new ArgumentException(
                    "Both live Metadata outcomes must match the completion evidence.",
                    nameof(implementationOutcome));
            }
            return new(
                Identity,
                new PlatformTypeDefinitionValue.ReferenceAndImplementation<
                    TReferenceOutcome,
                    TImplementationOutcome>(
                        referenceOutcome.Value,
                        implementationOutcome.Value));
        }
    }

    static IReadOnlyList<PlatformSourceSettlement>
        ValidateSelectedRealizations(
            PlatformViewDemand view,
            PlatformPopulationDemand? population,
            IEnumerable<PlatformSourceSettlement> selectedContributions,
            PlatformViewCorrespondenceIdentity? viewCorrespondence)
    {
        ArgumentNullException.ThrowIfNull(selectedContributions);
        PlatformSourceSettlement[] snapshot = [.. selectedContributions];
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "Completed resolution requires selected source contributions.",
                nameof(selectedContributions));
        }

        bool hasReference = false;
        bool hasImplementation = false;
        foreach (PlatformSourceSettlement settlement in snapshot)
        {
            ArgumentNullException.ThrowIfNull(
                settlement,
                nameof(selectedContributions));
            PlatformSourceContribution contribution = settlement.Contribution;
            if (settlement.Disposition
                    != PlatformSourceSettlementDisposition.Selected
                || contribution.Kind
                    != PlatformSourceContributionKind.Realization)
            {
                throw new ArgumentException(
                    "Completion requires selected successful realization contributions.",
                    nameof(selectedContributions));
            }
            if (population is not null
                && !ReferenceEquals(
                    contribution.SuppliedPopulation,
                    population))
            {
                throw new ArgumentException(
                    "A realization contribution must retain the operation's exact population demand.",
                    nameof(selectedContributions));
            }
            if (population is PlatformPopulationDemand.CompletePopulation
                && contribution.Completeness
                    != PlatformSourceContributionCompleteness.Authoritative)
            {
                throw new ArgumentException(
                    "Complete-population realization requires authoritative contributions.",
                    nameof(selectedContributions));
            }

            hasReference |= contribution.SuppliedView
                == PlatformViewDemand.Reference;
            hasImplementation |= contribution.SuppliedView
                == PlatformViewDemand.Implementation;
        }

        bool requiredViewSettled = view switch
        {
            PlatformViewDemand.Reference => hasReference,
            PlatformViewDemand.Implementation => hasImplementation,
            PlatformViewDemand.ReferenceAndImplementation =>
                hasReference
                && hasImplementation
                && viewCorrespondence is not null,
            _ => false,
        };
        if (!requiredViewSettled)
        {
            throw new ArgumentException(
                "Selected contributions do not settle the requested view.",
                nameof(selectedContributions));
        }

        return Array.AsReadOnly(snapshot);
    }
}

/// <summary>Resource-free terminal evidence for a non-completed outcome.</summary>
public abstract class PlatformHouseTermination
{
    private protected PlatformHouseTermination(
        PlatformHouseSettlementKind kind)
    {
        Kind = kind;
    }

    public PlatformHouseSettlementKind Kind { get; }

    public sealed class Unavailable : PlatformHouseTermination
    {
        public Unavailable(PlatformHouseTerminalEvidenceIdentity evidence)
            : base(PlatformHouseSettlementKind.Unavailable)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public PlatformHouseTerminalEvidenceIdentity Evidence { get; }
    }

    public sealed class Ambiguous : PlatformHouseTermination
    {
        public Ambiguous(IEnumerable<PlatformHouseCandidateIdentity> candidates)
            : base(PlatformHouseSettlementKind.Ambiguous)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            PlatformHouseCandidateIdentity[] snapshot = [.. candidates];
            if (snapshot.Length < 2)
            {
                throw new ArgumentException(
                    "Ambiguous termination requires at least two candidates.",
                    nameof(candidates));
            }
            for (int index = 0; index < snapshot.Length; index++)
            {
                ArgumentNullException.ThrowIfNull(
                    snapshot[index],
                    nameof(candidates));
            }
            var seen = new HashSet<PlatformHouseCandidateIdentity>(
                ReferenceEqualityComparer.Instance);
            if (snapshot.Any(candidate => !seen.Add(candidate)))
            {
                throw new ArgumentException(
                    "Ambiguous termination requires distinct candidate identities.",
                    nameof(candidates));
            }
            Candidates = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<PlatformHouseCandidateIdentity> Candidates { get; }
    }

    public sealed class Rejected : PlatformHouseTermination
    {
        public Rejected(PlatformHouseRejection rejection)
            : base(PlatformHouseSettlementKind.Rejected)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public PlatformHouseRejection Rejection { get; }
    }

    public sealed class Incomplete : PlatformHouseTermination
    {
        public Incomplete(PlatformHouseTerminalEvidenceIdentity evidence)
            : base(PlatformHouseSettlementKind.Incomplete)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public PlatformHouseTerminalEvidenceIdentity Evidence { get; }
    }

    public sealed class Failed : PlatformHouseTermination
    {
        public Failed(
            PlatformHouseTerminalEvidenceIdentity evidence,
            IEnumerable<PlatformHouseFailureKind> failures,
            bool cancellationObserved = false)
            : base(PlatformHouseSettlementKind.Failed)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(failures);
            PlatformHouseFailureKind[] snapshot = [.. failures];
            if (snapshot.Length == 0)
            {
                throw new ArgumentException(
                    "Failed termination requires at least one failure stage.",
                    nameof(failures));
            }
            if (snapshot.Any(static failure => !Enum.IsDefined(failure)))
            {
                throw new ArgumentOutOfRangeException(nameof(failures));
            }
            if (snapshot.Distinct().Count() != snapshot.Length)
            {
                throw new ArgumentException(
                    "Failed termination cannot repeat one failure stage.",
                    nameof(failures));
            }

            Evidence = evidence;
            Failures = Array.AsReadOnly(snapshot);
            CancellationObserved = cancellationObserved;
        }

        public PlatformHouseTerminalEvidenceIdentity Evidence { get; }
        public IReadOnlyList<PlatformHouseFailureKind> Failures { get; }
        public bool CancellationObserved { get; }
    }
}

sealed class PlatformHouseCompletedValue<TValue>
    where TValue : notnull
{
    public PlatformHouseCompletedValue(
        PlatformHouseCompletionIdentity identity,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(value);
        Identity = identity;
        Value = value;
    }

    public PlatformHouseCompletionIdentity Identity { get; }
    public TValue Value { get; }
}

/// <summary>
/// Resource-free evidence binding one request to its target and source
/// settlement. Construction is House-controlled.
/// </summary>
public sealed class PlatformHouseReceipt
{
    internal PlatformHouseReceipt(
        PlatformHouseRequestSnapshot request,
        PlatformTargetSettlement targetSettlement,
        IEnumerable<PlatformSourceSettlement> sourceSettlements,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseCompletion? completion = null,
        PlatformHouseTermination? termination = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetSettlement);
        ArgumentNullException.ThrowIfNull(sourceSettlements);
        ArgumentNullException.ThrowIfNull(consumedWork);
        if (!ReferenceEquals(request.Target, targetSettlement.Demand))
        {
            throw new ArgumentException(
                "The target settlement must retain the request's exact target demand.",
                nameof(targetSettlement));
        }

        PlatformHouseSettlementKind settlementKind;
        if (completion is not null)
        {
            if (termination is not null)
            {
                throw new ArgumentException(
                    "A receipt cannot be both completed and non-completed.",
                    nameof(termination));
            }
            if (targetSettlement.SettledTarget is null)
            {
                throw new ArgumentException(
                    "A completed settlement requires one exact settled target.",
                    nameof(targetSettlement));
            }
            if (!ReferenceEquals(completion.Operation, request.Operation))
            {
                throw new ArgumentException(
                    "Completion must retain the request's exact operation snapshot.",
                    nameof(completion));
            }
            settlementKind = PlatformHouseSettlementKind.Completed;
        }
        else
        {
            ArgumentNullException.ThrowIfNull(termination);
            settlementKind = termination.Kind;
        }

        PlatformSourceSettlement[] sourceSnapshot = [.. sourceSettlements];
        var seenContributions = new HashSet<PlatformSourceContribution>(
            ReferenceEqualityComparer.Instance);
        foreach (PlatformSourceSettlement source in sourceSnapshot)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(sourceSettlements));
            PlatformSourceContribution contribution = source.Contribution;
            if (!seenContributions.Add(contribution))
            {
                throw new ArgumentException(
                    "A source contribution may have only one settlement disposition.",
                    nameof(sourceSettlements));
            }
            if (!ReferenceEquals(contribution.Request, request))
            {
                throw new ArgumentException(
                    "Every source contribution must retain the exact request snapshot.",
                    nameof(sourceSettlements));
            }
            if (!request.Sources.Authorizes(
                    contribution.Facet,
                    contribution.Capability))
            {
                throw new ArgumentException(
                    "A source contribution is not authorized for its retained facet.",
                    nameof(sourceSettlements));
            }
            if (contribution.Facet == PlatformSourceFacet.TargetDiscovery
                && !request.Target.AuthorizesDiscoveryCapability(
                    contribution.Capability)
                && contribution.Kind
                    != PlatformSourceContributionKind.Rejected)
            {
                throw new ArgumentException(
                    "A target-discovery contribution is not authorized by the selecting demand.",
                    nameof(sourceSettlements));
            }
            if (contribution.ExactTarget is { } exactTarget
                && targetSettlement.SettledTarget != exactTarget)
            {
                throw new ArgumentException(
                    "A post-selection contribution does not match the settled exact target.",
                    nameof(sourceSettlements));
            }
        }

        PlatformSourceSettlement[] selectedDiscoveries =
            targetSettlement.SelectedDiscoveries
                .Select(
                    discovery => sourceSnapshot.SingleOrDefault(
                        source => ReferenceEquals(
                            source.Contribution,
                            discovery)))
                .Where(
                    settlement => settlement is not null)
                .Cast<PlatformSourceSettlement>()
                .ToArray();
        if (selectedDiscoveries.Length
                != targetSettlement.SelectedDiscoveries.Count
            || selectedDiscoveries.Any(
                settlement => settlement.Disposition
                    != PlatformSourceSettlementDisposition.Selected))
        {
            throw new ArgumentException(
                "Every target-selection contribution must be retained as selected evidence.",
                nameof(targetSettlement));
        }
        if (selectedDiscoveries.Length != 0)
        {
            if (request.Target
                is PlatformTargetDemand.FamilyDefault familyDefault)
            {
                ValidateFamilyDefaultTargetPolicy(
                    familyDefault,
                    targetSettlement.SettledTarget!,
                    selectedDiscoveries,
                    sourceSnapshot,
                    nameof(targetSettlement));
            }
            else
            {
                ValidateSuccessfulFacetPolicy(
                    PlatformSourceFacet.TargetDiscovery,
                    selectedDiscoveries,
                    sourceSnapshot,
                    request.Sources,
                    nameof(targetSettlement));
            }
        }

        if (completion is not null)
        {
            foreach (PlatformSourceSettlement required
                in completion.RequiredSourceSettlements)
            {
                if (!sourceSnapshot.Contains(required))
                {
                    throw new ArgumentException(
                        "Completion references a source settlement absent from the receipt.",
                        nameof(completion));
                }
            }
            ValidateCompletionSourcePolicy(
                completion,
                sourceSnapshot,
                request.Sources);
        }

        ValidateConsumedWork(consumedWork, request, settlementKind);

        Request = request;
        TargetSettlement = targetSettlement;
        SourceSettlements = Array.AsReadOnly(sourceSnapshot);
        SettlementKind = settlementKind;
        ConsumedWork = consumedWork;
        Completion = completion;
        Termination = termination;
    }

    public PlatformHouseRequestSnapshot Request { get; }
    public PlatformTargetSettlement TargetSettlement { get; }
    public IReadOnlyList<PlatformSourceSettlement> SourceSettlements { get; }
    public PlatformHouseSettlementKind SettlementKind { get; }
    public PlatformHouseConsumedWork ConsumedWork { get; }
    public PlatformHouseCompletion? Completion { get; }
    public PlatformHouseTermination? Termination { get; }

    static void ValidateConsumedWork(
        PlatformHouseConsumedWork consumed,
        PlatformHouseRequestSnapshot request,
        PlatformHouseSettlementKind settlementKind)
    {
        PlatformHouseWorkBudget budget = request.Work;
        bool exceedsOperationBudget =
            consumed.SourceOperations > budget.MaxSourceOperations
            || consumed.TargetCandidates > budget.MaxTargetCandidates
            || consumed.Assemblies > budget.MaxAssemblies
            || consumed.XmlDocuments > budget.MaxXmlDocuments
            || consumed.PortablePdbs > budget.MaxPortablePdbs
            || consumed.SourceDocuments > budget.MaxSourceDocuments
            || consumed.Bytes > budget.MaxBytes
            || consumed.ForwardingHops > budget.MaxForwardingHops
            || consumed.Elapsed > budget.MaxDuration;
        PlatformTargetDiscoveryBudget? targetBudget =
            request.Target.DiscoveryWork;
        bool exceedsTargetBudget =
            targetBudget is not null
            && (consumed.TargetCandidates > targetBudget.MaxCandidates
                || consumed.TargetComparisons
                    > targetBudget.MaxComparisons);

        if (settlementKind is not PlatformHouseSettlementKind.Incomplete
                and not PlatformHouseSettlementKind.Failed
            && (exceedsOperationBudget || exceedsTargetBudget))
        {
            throw new ArgumentException(
                "Consumed work exceeds the retained request budget.",
                nameof(consumed));
        }
    }

    static void ValidateFamilyDefaultTargetPolicy(
        PlatformTargetDemand.FamilyDefault demand,
        PlatformFamilyTarget target,
        IReadOnlyList<PlatformSourceSettlement> selected,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        string parameterName)
    {
        PlatformTargetDiscoveryStage selectedStage =
            demand.Policy.StageFor(
                selected[0].Contribution.Capability)
            ?? throw new ArgumentException(
                "Selected target evidence is outside the family-default stages.",
                parameterName);
        if (selected.Any(
            settlement => !ReferenceEquals(
                demand.Policy.StageFor(
                    settlement.Contribution.Capability),
                selectedStage)))
        {
            throw new ArgumentException(
                "One family-default target settlement cannot select contributions from different stages.",
                parameterName);
        }

        if (ReferenceEquals(selectedStage, demand.Policy.Preferred))
        {
            ValidateSelectedFamilyDefaultStage(
                selectedStage,
                target,
                selected,
                sourceSettlements,
                parameterName);
            if (sourceSettlements.Any(
                settlement => settlement.Contribution.Facet
                        == PlatformSourceFacet.TargetDiscovery
                    && demand.Policy.Fallback.Contains(
                        settlement.Contribution.Capability)))
            {
                throw new ArgumentException(
                    "Fallback discovery cannot contribute when the preferred stage selects a target.",
                    parameterName);
            }
            return;
        }

        if (!ReferenceEquals(selectedStage, demand.Policy.Fallback))
        {
            throw new ArgumentException(
                "Selected target evidence is outside the family-default stages.",
                parameterName);
        }

        if (demand.Policy.Preferred is { } preferred)
        {
            foreach (PlatformSourceCapabilityIdentity capability
                in preferred.Capabilities)
            {
                PlatformSourceSettlement prior =
                    RequireSingleFamilyDefaultContribution(
                        capability,
                        sourceSettlements,
                        parameterName);
                if (prior.Disposition
                        != PlatformSourceSettlementDisposition.OutcomeRelevant
                    || !IsAuthoritativePreferredAbsence(
                        demand,
                        prior.Contribution))
                {
                    throw new ArgumentException(
                        "Fallback selection requires authoritative absence from every preferred capability.",
                        parameterName);
                }
            }
        }

        ValidateSelectedFamilyDefaultStage(
            selectedStage,
            target,
            selected,
            sourceSettlements,
            parameterName);
    }

    static void ValidateSelectedFamilyDefaultStage(
        PlatformTargetDiscoveryStage stage,
        PlatformFamilyTarget target,
        IReadOnlyList<PlatformSourceSettlement> selected,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        string parameterName)
    {
        foreach (PlatformSourceCapabilityIdentity capability
            in stage.Capabilities)
        {
            PlatformSourceSettlement retained =
                RequireSingleFamilyDefaultContribution(
                    capability,
                    sourceSettlements,
                    parameterName);
            bool isSelected = selected.Contains(retained);
            if (isSelected)
            {
                if (retained.Disposition
                        != PlatformSourceSettlementDisposition.Selected
                    || retained.Contribution
                        is not PlatformSourceContribution.TargetDiscovery
                            discovery
                    || !discovery.Candidates.Contains(target))
                {
                    throw new ArgumentException(
                        "Selected stage evidence must be a completed inventory that offered the exact target.",
                        parameterName);
                }
                continue;
            }

            bool retainedInventory =
                retained.Contribution
                    is PlatformSourceContribution.TargetDiscovery
                && retained.Disposition
                    is PlatformSourceSettlementDisposition.Shadowed
                        or PlatformSourceSettlementDisposition.OutcomeRelevant;
            bool retainedAbsence =
                retained.Contribution
                    is PlatformSourceContribution.Unavailable
                {
                    Reason: PlatformSourceUnavailabilityKind.Absent,
                }
                && retained.Disposition
                    == PlatformSourceSettlementDisposition.OutcomeRelevant;
            if (!retainedInventory && !retainedAbsence)
            {
                throw new ArgumentException(
                    "Target selection requires conclusive evidence from every capability in its stage.",
                    parameterName);
            }
        }
    }

    static PlatformSourceSettlement
        RequireSingleFamilyDefaultContribution(
            PlatformSourceCapabilityIdentity capability,
            IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
            string parameterName)
    {
        PlatformSourceSettlement[] matches = [.. sourceSettlements.Where(
            settlement =>
                settlement.Contribution.Facet
                    == PlatformSourceFacet.TargetDiscovery
                && ReferenceEquals(
                    settlement.Contribution.Capability,
                    capability))];
        if (matches.Length != 1)
        {
            throw new ArgumentException(
                "Family-default settlement requires one retained contribution from every executed stage capability.",
                parameterName);
        }
        return matches[0];
    }

    static bool IsAuthoritativePreferredAbsence(
        PlatformTargetDemand.FamilyDefault demand,
        PlatformSourceContribution contribution) =>
        contribution is PlatformSourceContribution.Unavailable
        {
            Reason: PlatformSourceUnavailabilityKind.Absent,
        }
        || contribution
            is PlatformSourceContribution.TargetDiscovery discovery
            && !discovery.Candidates.Any(
                candidate => demand.IsEligibleSelection(
                    contribution.Capability,
                    candidate));

    static void ValidateCompletionSourcePolicy(
        PlatformHouseCompletion completion,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        PlatformSourcePlan sourcePlan)
    {
        if (completion is PlatformHouseCompletion.AssemblyReference
            {
                Kind: PlatformAssemblyReferenceCompletionKind.NoNameOwner,
                Operation:
                    PlatformHouseOperationSnapshot.ResolveAssemblyReference
                        operation,
            } assemblyReference)
        {
            ValidateNoNameOwnerSourcePolicy(
                operation.RequiredView,
                assemblyReference.SourceSettlements,
                sourceSettlements,
                sourcePlan);
            return;
        }

        foreach (IGrouping<PlatformSourceFacet, PlatformSourceSettlement> group
            in completion.RequiredSourceSettlements.GroupBy(
                settlement => settlement.Contribution.Facet))
        {
            ValidateSuccessfulFacetPolicy(
                group.Key,
                [.. group],
                sourceSettlements,
                sourcePlan,
                nameof(completion));
        }
    }

    static void ValidateNoNameOwnerSourcePolicy(
        PlatformViewDemand requiredView,
        IReadOnlyList<PlatformSourceSettlement> completionSettlements,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        PlatformSourcePlan sourcePlan)
    {
        PlatformSourceFacet[] requiredFacets = requiredView switch
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
            _ => throw new ArgumentOutOfRangeException(nameof(requiredView)),
        };

        foreach (PlatformSourceFacet facet in requiredFacets)
        {
            PlatformSourceSelection? selection =
                sourcePlan.SelectionFor(facet);
            if (selection is null)
                continue;

            if (selection.Mode == PlatformSourceSelectionMode.Aggregation)
            {
                ValidateNoNameOwnerAggregation(
                    facet,
                    completionSettlements,
                    selection);
                continue;
            }

            PlatformSourceSettlement[] selected = [.. completionSettlements.Where(
                settlement => settlement.Contribution.Facet == facet
                    && settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected)];
            if (selected.Length != 0)
            {
                if (selected.Any(
                        settlement => settlement.Contribution
                            is not PlatformSourceContribution.Realization
                            {
                                RealizationCompleteness:
                                    PlatformSourceContributionCompleteness
                                        .Authoritative,
                            }))
                {
                    throw new ArgumentException(
                        $"A completed NoNameOwner selected {facet} settlement must retain only authoritative realized source populations.",
                        nameof(completionSettlements));
                }
                ValidateSuccessfulFacetPolicy(
                    facet,
                    selected,
                    sourceSettlements,
                    sourcePlan,
                    nameof(completionSettlements));
                continue;
            }

            PlatformSourceSettlement[] outcomeRelevant =
                [.. sourceSettlements.Where(
                    settlement => settlement.Contribution.Facet == facet
                        && settlement.Disposition
                            == PlatformSourceSettlementDisposition
                                .OutcomeRelevant)];
            if (outcomeRelevant.Length != selection.Capabilities.Count)
            {
                throw new ArgumentException(
                    $"A completed NoNameOwner {selection.Mode} settlement requires one authoritative absence from every {facet} capability.",
                    nameof(sourceSettlements));
            }
            foreach (PlatformSourceCapabilityIdentity capability
                in selection.Capabilities)
            {
                PlatformSourceSettlement[] matches =
                    [.. outcomeRelevant.Where(
                    settlement => ReferenceEquals(
                        settlement.Contribution.Capability,
                        capability))];
                if (matches.Length != 1
                    || !IsAuthoritativeAbsence(matches[0]))
                {
                    throw new ArgumentException(
                        $"A completed NoNameOwner {selection.Mode} settlement requires authoritative absence from every {facet} capability.",
                        nameof(sourceSettlements));
                }
            }
        }
    }

    static void ValidateNoNameOwnerAggregation(
        PlatformSourceFacet facet,
        IReadOnlyList<PlatformSourceSettlement> completionSettlements,
        PlatformSourceSelection selection)
    {
        PlatformSourceSettlement[] facetSettlements =
            [.. completionSettlements.Where(
                settlement => settlement.Contribution.Facet == facet)];
        if (facetSettlements.Length != selection.Capabilities.Count)
        {
            throw new ArgumentException(
                $"A completed NoNameOwner aggregation requires one conclusive {facet} settlement from every capability.",
                nameof(completionSettlements));
        }

        foreach (PlatformSourceCapabilityIdentity capability
            in selection.Capabilities)
        {
            PlatformSourceSettlement[] matches =
                [.. facetSettlements.Where(
                    settlement => ReferenceEquals(
                        settlement.Contribution.Capability,
                        capability))];
            if (matches.Length != 1
                || !IsAuthoritativeRealization(matches[0])
                    && !IsAuthoritativeAbsence(matches[0]))
            {
                throw new ArgumentException(
                    $"A completed NoNameOwner aggregation requires an authoritative searched realization or absence from every {facet} capability.",
                    nameof(completionSettlements));
            }
        }
    }

    static bool IsAuthoritativeRealization(
        PlatformSourceSettlement settlement) =>
        settlement.Disposition
            == PlatformSourceSettlementDisposition.Selected
        && settlement.Contribution
            is PlatformSourceContribution.Realization
        {
            RealizationCompleteness:
                    PlatformSourceContributionCompleteness.Authoritative,
        };

    static bool IsAuthoritativeAbsence(
        PlatformSourceSettlement settlement) =>
        settlement.Disposition
            == PlatformSourceSettlementDisposition.OutcomeRelevant
        && settlement.Contribution
            is PlatformSourceContribution.Unavailable
        {
            Reason: PlatformSourceUnavailabilityKind.Absent,
        };

    static void ValidateSuccessfulFacetPolicy(
        PlatformSourceFacet facet,
        IReadOnlyList<PlatformSourceSettlement> selected,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        PlatformSourcePlan sourcePlan,
        string parameterName)
    {
        PlatformSourceSelection selection =
            sourcePlan.SelectionFor(facet)
            ?? throw new ArgumentException(
                $"Settlement requires an unauthorized source facet {facet}.",
                parameterName);
        if (selection.Mode == PlatformSourceSelectionMode.Aggregation)
        {
            if (selected.Count == 0)
            {
                throw new ArgumentException(
                    $"A successful aggregation settlement requires at least one selected {facet} contribution.",
                    parameterName);
            }
            if (selected.Any(
                    settlement => settlement.Contribution
                        is PlatformSourceContribution.Realization
                    {
                        RealizationCompleteness:
                                not PlatformSourceContributionCompleteness
                                    .Authoritative,
                    }))
            {
                throw new ArgumentException(
                    $"A successful aggregation settlement requires authoritative selected {facet} realizations.",
                    parameterName);
            }
            foreach (PlatformSourceCapabilityIdentity capability
                in selection.Capabilities)
            {
                int selectedCount = selected.Count(
                    settlement => ReferenceEquals(
                        settlement.Contribution.Capability,
                        capability));
                if (selectedCount == 1)
                    continue;
                if (selectedCount != 0)
                {
                    throw new ArgumentException(
                        $"Aggregation settlement permits at most one selected contribution from each {facet} capability.",
                        parameterName);
                }

                PlatformSourceSettlement[] retained =
                    [.. sourceSettlements.Where(
                        settlement => settlement.Contribution.Facet == facet
                        && ReferenceEquals(
                            settlement.Contribution.Capability,
                            capability))];
                if (retained.Length != 1
                    || !IsAuthoritativeAbsence(retained[0]))
                {
                    throw new ArgumentException(
                        $"Aggregation settlement requires a selected contribution or authoritative absence from every {facet} capability.",
                        parameterName);
                }
            }
            return;
        }

        if (selected.Count != 1)
        {
            throw new ArgumentException(
                $"A {selection.Mode} source facet must select exactly one contribution.",
                parameterName);
        }

        PlatformSourceCapabilityIdentity selectedCapability =
            selected[0].Contribution.Capability;
        int selectedIndex = IndexOf(
            selection.Capabilities,
            selectedCapability);
        if (selectedIndex < 0)
        {
            throw new ArgumentException(
                "Settlement selected a capability outside the source policy.",
                parameterName);
        }
        ValidatePriorFailures(
            facet,
            selectedIndex,
            selection,
            sourceSettlements,
            parameterName);
    }

    internal static bool SupplierSatisfiesView(
        PlatformSourceContribution supplier,
        PlatformViewDemand view) =>
        view switch
        {
            PlatformViewDemand.Reference =>
                supplier.SuppliedView == PlatformViewDemand.Reference,
            PlatformViewDemand.Implementation
                or PlatformViewDemand.ReferenceAndImplementation =>
                    supplier.SuppliedView
                        == PlatformViewDemand.Implementation,
            _ => false,
        };

    static void ValidatePriorFailures(
        PlatformSourceFacet facet,
        int selectedIndex,
        PlatformSourceSelection selection,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        string parameterName)
    {
        for (int index = 0; index < selectedIndex; index++)
        {
            PlatformSourceCapabilityIdentity earlier =
                selection.Capabilities[index];
            PlatformSourceSettlement[] prior = [.. sourceSettlements.Where(
                settlement =>
                    settlement.Contribution.Facet == facet
                    && ReferenceEquals(
                        settlement.Contribution.Capability,
                        earlier))];
            if (prior.Length != 1
                || prior[0].Disposition
                    != PlatformSourceSettlementDisposition.OutcomeRelevant
                || !IsPriorNonMatch(
                    facet,
                    selection.Mode,
                    prior[0].Contribution))
            {
                throw new ArgumentException(
                    $"Selecting a later {facet} capability requires retained evidence permitted by the {selection.Mode} policy for every earlier capability.",
                    parameterName);
            }
        }

        static bool IsPriorNonMatch(
            PlatformSourceFacet facet,
            PlatformSourceSelectionMode mode,
            PlatformSourceContribution contribution) =>
            contribution.Kind == PlatformSourceContributionKind.Unavailable
            || mode == PlatformSourceSelectionMode.Fallback
                && contribution.Kind == PlatformSourceContributionKind.Failed
            || facet == PlatformSourceFacet.TargetDiscovery
                && contribution.Kind
                    == PlatformSourceContributionKind.TargetDiscovery
                && contribution.DiscoveredCandidates.Count == 0;
    }

    static int IndexOf<T>(IReadOnlyList<T> values, T value)
        where T : class
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (ReferenceEquals(values[index], value))
                return index;
        }
        return -1;
    }
}
