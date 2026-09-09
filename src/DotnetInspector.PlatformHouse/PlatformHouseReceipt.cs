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
            PlatformTargetDemand.Selecting demand,
            PlatformFamilyTarget target,
            IEnumerable<PlatformSourceContribution> discoveries,
            PlatformSourceEvidenceIdentity selectionEvidence)
            : base(demand, target)
        {
            ArgumentNullException.ThrowIfNull(discoveries);
            ArgumentNullException.ThrowIfNull(selectionEvidence);
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
            if (!snapshot.Any(
                    discovery =>
                        discovery.DiscoveredCandidates.Contains(target)))
            {
                throw new ArgumentException(
                    "The selected target must occur in retained discovery evidence.",
                    nameof(target));
            }

            Discoveries = Array.AsReadOnly(snapshot);
            SelectionEvidence = selectionEvidence;
        }

        public IReadOnlyList<PlatformSourceContribution> Discoveries { get; }
        public PlatformSourceEvidenceIdentity SelectionEvidence { get; }

        internal override IReadOnlyList<PlatformSourceContribution>
            SelectedDiscoveries => Discoveries;
    }

    public sealed class Unsettled : PlatformTargetSettlement
    {
        internal Unsettled(
            PlatformTargetDemand demand,
            PlatformSourceEvidenceIdentity evidence)
            : base(demand, null)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public PlatformSourceEvidenceIdentity Evidence { get; }
    }

    static void ValidateCorrespondence(
        PlatformFamilyTarget target,
        PlatformTargetDemand demand,
        string parameterName)
    {
        if (target.Family != demand.Family
            || target.TargetFramework != demand.TargetFramework)
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
}

/// <summary>The terminal status of one requested documentation channel.</summary>
public enum PlatformDocumentationAttemptKind
{
    Available,
    Absent,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

/// <summary>One resource-free documentation-channel attempt.</summary>
public sealed class PlatformDocumentationAttempt
{
    public PlatformDocumentationAttempt(
        PlatformSourceFacet facet,
        PlatformDocumentationAttemptKind kind,
        IEnumerable<PlatformSourceSettlement> sourceSettlements,
        PlatformSourceEvidenceIdentity evidence)
    {
        if (facet is not PlatformSourceFacet.CompiledXml
            and not PlatformSourceFacet.SourceDerivedDocumentation)
        {
            throw new ArgumentException(
                "A documentation attempt must use a documentation facet.",
                nameof(facet));
        }
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(sourceSettlements);
        ArgumentNullException.ThrowIfNull(evidence);
        PlatformSourceSettlement[] snapshot = [.. sourceSettlements];
        if (snapshot.Length == 0
            && kind != PlatformDocumentationAttemptKind.Unavailable)
        {
            throw new ArgumentException(
                "Only an unavailable documentation channel may omit source settlement evidence.",
                nameof(sourceSettlements));
        }
        var seen = new HashSet<PlatformSourceContribution>(
            ReferenceEqualityComparer.Instance);
        foreach (PlatformSourceSettlement settlement in snapshot)
        {
            ArgumentNullException.ThrowIfNull(
                settlement,
                nameof(sourceSettlements));
            if (settlement.Contribution.Facet != facet)
            {
                throw new ArgumentException(
                    "Documentation attempt settlements must match the requested facet.",
                    nameof(sourceSettlements));
            }
            if (!seen.Add(settlement.Contribution))
            {
                throw new ArgumentException(
                    "A documentation attempt cannot retain one contribution more than once.",
                    nameof(sourceSettlements));
            }
        }
        Facet = facet;
        Kind = kind;
        SourceSettlements = Array.AsReadOnly(snapshot);
        Evidence = evidence;
    }

    public PlatformSourceFacet Facet { get; }
    public PlatformDocumentationAttemptKind Kind { get; }
    public IReadOnlyList<PlatformSourceSettlement> SourceSettlements { get; }
    public PlatformSourceEvidenceIdentity Evidence { get; }
}

/// <summary>Terminal assembly-reference forms that count as completed.</summary>
public enum PlatformAssemblyReferenceCompletionKind
{
    Resolved,
    NoNameOwner,
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
            IEnumerable<PlatformSourceSettlement> selectedContributions,
            PlatformViewCorrespondenceEvidence? viewCorrespondence = null)
            : base(operation)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ArgumentNullException.ThrowIfNull(metadataOutcome);
            ArgumentNullException.ThrowIfNull(selectedContributions);

            PlatformSourceSettlement[] snapshot = [.. selectedContributions];
            if (kind == PlatformAssemblyReferenceCompletionKind.NoNameOwner)
            {
                if (snapshot.Length != 0
                    || viewCorrespondence is not null
                    || metadataOutcome.TerminalSupplier is not null
                    || metadataOutcome.Correspondence is not null)
                {
                    throw new ArgumentException(
                        "NoNameOwner completion has no platform contribution.",
                        nameof(selectedContributions));
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
            SelectedContributions = Array.AsReadOnly(snapshot);
            ViewCorrespondence = viewCorrespondence?.Identity;
        }

        public PlatformAssemblyReferenceCompletionKind Kind { get; }
        public PlatformMetadataOutcomeIdentity MetadataOutcome { get; }
        public IReadOnlyList<PlatformSourceSettlement> SelectedContributions
        {
            get;
        }
        public PlatformViewCorrespondenceIdentity? ViewCorrespondence { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements => SelectedContributions;

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
            PlatformMetadataOutcomeEvidence referenceOutcome,
            PlatformMetadataOutcomeEvidence? implementationOutcome,
            IEnumerable<PlatformSourceSettlement> selectedContributions,
            PlatformViewCorrespondenceEvidence? correspondence)
            : base(operation)
        {
            ArgumentNullException.ThrowIfNull(referenceOutcome);
            ArgumentNullException.ThrowIfNull(selectedContributions);
            PlatformSourceSettlement[] snapshot = [.. selectedContributions];
            bool implementationRequired = operation.RequiredView
                is PlatformViewDemand.Implementation
                    or PlatformViewDemand.ReferenceAndImplementation;
            if (implementationRequired)
            {
                ArgumentNullException.ThrowIfNull(implementationOutcome);
                if (ReferenceEquals(
                        referenceOutcome.Identity,
                        implementationOutcome.Identity))
                {
                    throw new ArgumentException(
                        "Reference and implementation resolution require distinct Metadata outcomes.",
                        nameof(implementationOutcome));
                }
                if (correspondence is null)
                {
                    throw new ArgumentNullException(
                        nameof(correspondence),
                        "Implementation resolution requires view correspondence.");
                }
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
            }
            else if (implementationOutcome is not null
                || snapshot.Length != 0
                || correspondence is not null)
            {
                throw new ArgumentException(
                    "Reference-only type resolution does not require an implementation supplier.",
                    nameof(selectedContributions));
            }
            ReferenceOutcome = referenceOutcome.Identity;
            ImplementationOutcome = implementationOutcome?.Identity;
            SelectedContributions = Array.AsReadOnly(snapshot);
            Correspondence = correspondence?.Identity;
        }

        public PlatformMetadataOutcomeIdentity ReferenceOutcome { get; }
        public PlatformMetadataOutcomeIdentity? ImplementationOutcome { get; }
        public IReadOnlyList<PlatformSourceSettlement> SelectedContributions
        {
            get;
        }
        public PlatformViewCorrespondenceIdentity? Correspondence { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements => SelectedContributions;

        internal PlatformHouseCompletedValue<TOutcome> BindReference<TOutcome>(
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
            return new(Identity, referenceOutcome.Value);
        }

        internal PlatformHouseCompletedValue<TImplementationOutcome>
            BindImplementation<TReferenceOutcome, TImplementationOutcome>(
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
            return new(Identity, implementationOutcome.Value);
        }
    }

    public sealed class Documentation : PlatformHouseCompletion
    {
        internal Documentation(
            PlatformHouseOperationSnapshot.ResolveDocumentationEvidence operation,
            IEnumerable<PlatformDocumentationAttempt> attempts)
            : base(operation)
        {
            ArgumentNullException.ThrowIfNull(attempts);
            PlatformDocumentationAttempt[] snapshot = [.. attempts];
            var seen = new HashSet<PlatformSourceFacet>();
            foreach (PlatformDocumentationAttempt attempt in snapshot)
            {
                ArgumentNullException.ThrowIfNull(attempt, nameof(attempts));
                if (!seen.Add(attempt.Facet))
                {
                    throw new ArgumentException(
                        $"Documentation facet {attempt.Facet} appears more than once.",
                        nameof(attempts));
                }
            }

            bool hasXml = seen.Contains(PlatformSourceFacet.CompiledXml);
            bool hasSource = seen.Contains(
                PlatformSourceFacet.SourceDerivedDocumentation);
            bool complete = operation.Demand switch
            {
                PlatformDocumentationDemand.CompiledXml =>
                    hasXml && !hasSource,
                PlatformDocumentationDemand.SourceDerived =>
                    !hasXml && hasSource,
                PlatformDocumentationDemand.CompiledXmlAndSourceDerived =>
                    hasXml && hasSource,
                _ => false,
            };
            if (!complete)
            {
                throw new ArgumentException(
                    "Documentation completion requires one attempt for every requested channel.",
                    nameof(attempts));
            }

            Attempts = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<PlatformDocumentationAttempt> Attempts { get; }

        internal override IReadOnlyList<PlatformSourceSettlement>
            RequiredSourceSettlements =>
            Attempts.SelectMany(attempt => attempt.SourceSettlements).ToArray();

        internal PlatformHouseCompletedValue<TValue> Bind<TValue>(TValue value)
            where TValue : notnull =>
            new(Identity, value);
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
                && request.Target is PlatformTargetDemand.Selecting selecting
                && !selecting.DiscoveryCapabilities.Any(
                    capability => ReferenceEquals(
                        capability,
                        contribution.Capability)))
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
            ValidateSuccessfulFacetPolicy(
                PlatformSourceFacet.TargetDiscovery,
                selectedDiscoveries,
                sourceSnapshot,
                request.Sources,
                nameof(targetSettlement));
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
        bool exceedsTargetBudget =
            request.Target is PlatformTargetDemand.Selecting selecting
            && (consumed.TargetCandidates > selecting.Work.MaxCandidates
                || consumed.TargetComparisons
                    > selecting.Work.MaxComparisons);

        if (settlementKind != PlatformHouseSettlementKind.Incomplete
            && (exceedsOperationBudget || exceedsTargetBudget))
        {
            throw new ArgumentException(
                "Consumed work exceeds the retained request budget.",
                nameof(consumed));
        }
    }

    static void ValidateCompletionSourcePolicy(
        PlatformHouseCompletion completion,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        PlatformSourcePlan sourcePlan)
    {
        if (completion is PlatformHouseCompletion.Documentation)
        {
            var documentation =
                (PlatformHouseCompletion.Documentation)completion;
            foreach (PlatformDocumentationAttempt attempt
                in documentation.Attempts)
            {
                if (attempt.SourceSettlements.Count == 0)
                {
                    if (attempt.Kind
                            != PlatformDocumentationAttemptKind.Unavailable
                        || sourcePlan.SelectionFor(attempt.Facet) is not null)
                    {
                        throw new ArgumentException(
                            "Only an unauthorized documentation channel may settle unavailable without source evidence.",
                            nameof(completion));
                    }
                    continue;
                }
                ValidateDocumentationAttemptStatus(
                    attempt,
                    sourcePlan);
            }
            foreach (
                IGrouping<PlatformSourceFacet, PlatformSourceSettlement> group
                in completion.RequiredSourceSettlements.GroupBy(
                    settlement => settlement.Contribution.Facet))
            {
                ValidateTerminalFacetPolicy(
                    group.Key,
                    [.. group],
                    sourceSettlements,
                    sourcePlan);
            }
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
            if (selected.Count != selection.Capabilities.Count)
            {
                throw new ArgumentException(
                    $"Aggregation settlement requires exactly one selected contribution from every {facet} capability.",
                    parameterName);
            }
            foreach (PlatformSourceCapabilityIdentity capability
                in selection.Capabilities)
            {
                if (selected.Count(
                        settlement => ReferenceEquals(
                            settlement.Contribution.Capability,
                            capability)) != 1)
                {
                    throw new ArgumentException(
                        $"Aggregation settlement requires a selected contribution from every {facet} capability.",
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

    static void ValidateTerminalFacetPolicy(
        PlatformSourceFacet facet,
        IReadOnlyList<PlatformSourceSettlement> settlements,
        IReadOnlyList<PlatformSourceSettlement> sourceSettlements,
        PlatformSourcePlan sourcePlan)
    {
        PlatformSourceSelection selection =
            sourcePlan.SelectionFor(facet)
            ?? throw new ArgumentException(
                $"Documentation settlement requires an unauthorized source facet {facet}.",
                nameof(settlements));
        if (selection.Mode == PlatformSourceSelectionMode.Aggregation)
        {
            if (settlements.Count != selection.Capabilities.Count
                || selection.Capabilities.Any(
                    capability => settlements.Count(
                        settlement => ReferenceEquals(
                            settlement.Contribution.Capability,
                            capability)) != 1)
                || settlements.Any(
                    settlement => settlement.Disposition
                        == PlatformSourceSettlementDisposition.Shadowed))
            {
                throw new ArgumentException(
                    $"Aggregation documentation settlement requires one outcome-relevant contribution from every {facet} capability.",
                    nameof(settlements));
            }
            return;
        }

        PlatformSourceSettlement[] selected = [.. settlements.Where(
            settlement => settlement.Disposition
                == PlatformSourceSettlementDisposition.Selected)];
        if (selected.Length == 1)
        {
            int selectedIndex = IndexOf(
                selection.Capabilities,
                selected[0].Contribution.Capability);
            if (selectedIndex < 0)
            {
                throw new ArgumentException(
                    "Documentation settlement selected a capability outside the source policy.",
                    nameof(settlements));
            }
            ValidatePriorFailures(
                facet,
                selectedIndex,
                selection,
                sourceSettlements,
                nameof(settlements));
            return;
        }
        if (selected.Length != 0
            || settlements.Count != selection.Capabilities.Count)
        {
            throw new ArgumentException(
                $"Terminal {selection.Mode} documentation settlement requires all capabilities when none succeeds.",
                nameof(settlements));
        }
        for (int index = 0; index < selection.Capabilities.Count; index++)
        {
            PlatformSourceCapabilityIdentity capability =
                selection.Capabilities[index];
            PlatformSourceSettlement[] matches = [.. settlements.Where(
                settlement => ReferenceEquals(
                    settlement.Contribution.Capability,
                    capability))];
            if (matches.Length != 1
                || matches[0].Disposition
                    != PlatformSourceSettlementDisposition.OutcomeRelevant
                || matches[0].Contribution.Kind
                    is PlatformSourceContributionKind.TargetDiscovery
                        or PlatformSourceContributionKind.Realization
                        or PlatformSourceContributionKind.Documentation)
            {
                throw new ArgumentException(
                    $"Terminal documentation settlement requires one retained non-success result from every {facet} capability.",
                    nameof(settlements));
            }
        }
    }

    static void ValidateDocumentationAttemptStatus(
        PlatformDocumentationAttempt attempt,
        PlatformSourcePlan sourcePlan)
    {
        PlatformSourceSelection selection =
            sourcePlan.SelectionFor(attempt.Facet)
            ?? throw new ArgumentException(
                $"Documentation attempt uses an unauthorized source facet {attempt.Facet}.",
                nameof(attempt));
        IReadOnlyList<PlatformSourceSettlement> settlements =
            attempt.SourceSettlements;
        if (selection.Mode == PlatformSourceSelectionMode.Aggregation)
        {
            PlatformDocumentationAttemptKind aggregate =
                AggregateAttemptKind(settlements);
            if (attempt.Kind != aggregate)
            {
                throw new ArgumentException(
                    "An aggregation documentation attempt must describe the aggregate retained contribution set.",
                    nameof(attempt));
            }
            return;
        }

        PlatformSourceSettlement[] selected = [.. settlements.Where(
            settlement => settlement.Disposition
                == PlatformSourceSettlementDisposition.Selected)];
        PlatformSourceSettlement terminal;
        if (selected.Length == 1)
        {
            terminal = selected[0];
        }
        else if (selected.Length == 0)
        {
            terminal = settlements
                .OrderBy(
                    settlement => IndexOf(
                        selection.Capabilities,
                        settlement.Contribution.Capability))
                .Last();
        }
        else
        {
            throw new ArgumentException(
                "A precedence or fallback documentation attempt may select only one contribution.",
                nameof(attempt));
        }

        if (!SupportsAttemptKind(
                terminal.Contribution,
                attempt.Kind))
        {
            throw new ArgumentException(
                "Documentation attempt status must describe the policy-selected terminal contribution.",
                nameof(attempt));
        }
    }

    static bool SupportsAttemptKind(
        PlatformSourceContributionKind contribution,
        PlatformDocumentationAttemptKind attempt) =>
        contribution != PlatformSourceContributionKind.Unavailable
            ? attempt switch
            {
                PlatformDocumentationAttemptKind.Available =>
                    contribution
                        == PlatformSourceContributionKind.Documentation,
                PlatformDocumentationAttemptKind.Rejected =>
                    contribution == PlatformSourceContributionKind.Rejected,
                PlatformDocumentationAttemptKind.Failed =>
                    contribution == PlatformSourceContributionKind.Failed,
                PlatformDocumentationAttemptKind.Incomplete =>
                    contribution == PlatformSourceContributionKind.Incomplete,
                _ => false,
            }
            : false;

    static bool SupportsAttemptKind(
        PlatformSourceContribution contribution,
        PlatformDocumentationAttemptKind attempt) =>
        contribution is PlatformSourceContribution.Unavailable unavailable
            ? attempt switch
            {
                PlatformDocumentationAttemptKind.Absent =>
                    unavailable.Reason
                        == PlatformSourceUnavailabilityKind.Absent,
                PlatformDocumentationAttemptKind.Unavailable =>
                    unavailable.Reason
                        == PlatformSourceUnavailabilityKind.Unavailable,
                _ => false,
            }
            : SupportsAttemptKind(contribution.Kind, attempt);

    static PlatformDocumentationAttemptKind AggregateAttemptKind(
        IReadOnlyList<PlatformSourceSettlement> settlements)
    {
        if (settlements.Any(
                settlement => settlement.Contribution.Kind
                    == PlatformSourceContributionKind.Incomplete))
        {
            return PlatformDocumentationAttemptKind.Incomplete;
        }
        if (settlements.Any(
                settlement => settlement.Contribution.Kind
                    == PlatformSourceContributionKind.Rejected))
        {
            return PlatformDocumentationAttemptKind.Rejected;
        }
        if (settlements.Any(
                settlement => settlement.Contribution.Kind
                    == PlatformSourceContributionKind.Failed))
        {
            return PlatformDocumentationAttemptKind.Failed;
        }
        if (settlements.Any(
                settlement => settlement.Contribution.Kind
                    == PlatformSourceContributionKind.Documentation))
        {
            return PlatformDocumentationAttemptKind.Available;
        }
        return settlements.All(
            settlement =>
                settlement.Contribution
                    is PlatformSourceContribution.Unavailable
                    {
                        Reason: PlatformSourceUnavailabilityKind.Absent,
                    })
            ? PlatformDocumentationAttemptKind.Absent
            : PlatformDocumentationAttemptKind.Unavailable;
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
                || !IsPriorNonMatch(facet, prior[0].Contribution))
            {
                throw new ArgumentException(
                    $"Selecting a later {facet} capability requires retained unavailable or failed evidence for every earlier capability.",
                    parameterName);
            }
        }

        static bool IsPriorNonMatch(
            PlatformSourceFacet facet,
            PlatformSourceContribution contribution) =>
            contribution.Kind
                is PlatformSourceContributionKind.Unavailable
                    or PlatformSourceContributionKind.Failed
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
