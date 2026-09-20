using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Artifact provenance binding one published content item to its exact
/// Platform source realization.
/// </summary>
public sealed class PlatformLibraryArtifactProvenance : IArtifactProvenance
{
    public PlatformLibraryArtifactProvenance(
        PlatformSourceContribution.Realization contribution,
        IArtifactProvenance sourceProvenance)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(sourceProvenance);
        Contribution = contribution;
        SourceProvenance = sourceProvenance;
    }

    public PlatformSourceContribution.Realization Contribution { get; }
    public IArtifactProvenance SourceProvenance { get; }
}

/// <summary>
/// Resource-free source-owner evidence for one selected assembly content item.
/// </summary>
/// <remarks>
/// The Artifact registration supplies the exact source contribution, while
/// Metadata's owner-issued projection supplies the exact physical managed
/// identity. The corresponding <see cref="ArtifactContentLease"/> remains a
/// separate operation input and is never retained here.
/// </remarks>
public sealed class PlatformLibraryContentSelection
{
    public PlatformLibraryContentSelection(
        ArtifactContentReference content,
        ArtifactAssemblyProjection projection)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(projection);
        if (content.Provenance
                is not PlatformLibraryArtifactProvenance provenance)
        {
            throw new ArgumentException(
                "Selected Library content must retain its Platform source realization as Artifact provenance.",
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
                "Selected Library content requires the Metadata projection issued for its exact Artifact.",
                nameof(projection));
        }
        var assemblyIdentity =
            new ManagedMetadataIdentity.Assembly(projection.Identity);
        if (assemblyIdentity.Identity.Version is null)
        {
            throw new ArgumentException(
                "Selected Library content requires an exact assembly version.",
                nameof(assemblyIdentity));
        }
        if (contribution.Population
                is not PlatformPopulationDemand.Library population
            || !OperationAcceptsSelection(
                contribution,
                population))
        {
            throw new ArgumentException(
                "Selected Library content must retain one exact one-Library realization demand.",
                nameof(contribution));
        }
        if (!DemandMatches(population.Value, assemblyIdentity))
        {
            throw new ArgumentException(
                "Selected Library content does not identify the demanded Library.",
                nameof(assemblyIdentity));
        }

        Contribution = contribution;
        Demand = population.Value;
        Content = content;
        Projection = projection;
        AssemblyIdentity = assemblyIdentity;
    }

    public PlatformSourceContribution.Realization Contribution { get; }
    public PlatformLibraryDemand Demand { get; }
    public ArtifactContentReference Content { get; }
    public ArtifactAssemblyProjection Projection { get; }
    public ManagedMetadataIdentity.Assembly AssemblyIdentity { get; }

    static bool OperationAcceptsSelection(
        PlatformSourceContribution.Realization contribution,
        PlatformPopulationDemand.Library population) =>
        contribution.Request.Operation switch
        {
            PlatformHouseOperationSnapshot.Realize operation =>
                ReferenceEquals(
                    contribution.Population,
                    operation.Population),
            PlatformHouseOperationSnapshot.ResolveAssemblyReference operation =>
                operation.RequiredView == PlatformViewDemand.Reference
                && contribution.Facet == PlatformSourceFacet.Reference
                && population.Value is PlatformLibraryDemand.Assembly,
            _ => false,
        };

    static bool DemandMatches(
        PlatformLibraryDemand demand,
        ManagedMetadataIdentity.Assembly identity) =>
        demand switch
        {
            PlatformLibraryDemand.Assembly assembly =>
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    assembly.Identity,
                    identity.Identity),
            PlatformLibraryDemand.PlatformLibrary => true,
            _ => false,
        };
}

/// <summary>
/// How PlatformHouse closes API and implementation roles over selected content.
/// </summary>
public enum PlatformLibraryViewCorrespondenceKind
{
    ReferenceAndImplementation,
    ImplementationDeclarationSurface,
}

/// <summary>
/// Resource-free Platform-owned evidence closing one Library's required roles.
/// </summary>
public sealed class PlatformLibraryViewCorrespondence :
    PlatformViewCorrespondenceEvidence
{
    public PlatformLibraryViewCorrespondence(
        PlatformLibraryContentSelection reference,
        PlatformLibraryContentSelection implementation,
        string identityName)
        : this(
            PlatformLibraryViewCorrespondenceKind
                .ReferenceAndImplementation,
            reference,
            implementation,
            identityName)
    {
    }

    PlatformLibraryViewCorrespondence(
        PlatformLibraryViewCorrespondenceKind kind,
        PlatformLibraryContentSelection api,
        PlatformLibraryContentSelection implementation,
        string identityName)
        : base(PlatformViewCorrespondenceIdentity.Issue(identityName))
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(implementation);
        if (kind
                == PlatformLibraryViewCorrespondenceKind
                    .ReferenceAndImplementation
            && api.Contribution.Facet != PlatformSourceFacet.Reference)
        {
            throw new ArgumentException(
                "Reference-and-implementation correspondence requires reference content.",
                nameof(api));
        }
        if (implementation.Contribution.Facet
            != PlatformSourceFacet.Implementation)
        {
            throw new ArgumentException(
                "View correspondence requires implementation content.",
                nameof(implementation));
        }
        if (kind
                == PlatformLibraryViewCorrespondenceKind
                    .ImplementationDeclarationSurface
            && !ReferenceEquals(api, implementation))
        {
            throw new ArgumentException(
                "Implementation declaration-surface evidence must assign both roles to one selected content item.",
                nameof(api));
        }
        if (!ReferenceEquals(api.Demand, implementation.Demand)
            || api.Contribution.Target != implementation.Contribution.Target
            || !ReferenceEquals(
                api.Content.Generation,
                implementation.Content.Generation)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                api.AssemblyIdentity.Identity,
                implementation.AssemblyIdentity.Identity))
        {
            throw new ArgumentException(
                "View correspondence requires the same demanded Library, target, Artifact generation, and managed identity.",
                nameof(implementation));
        }

        Kind = kind;
        Api = api;
        Implementation = implementation;
    }

    public PlatformLibraryViewCorrespondenceKind Kind { get; }
    public PlatformLibraryContentSelection Api { get; }
    public PlatformLibraryContentSelection Implementation { get; }

    public static PlatformLibraryViewCorrespondence
        CreateImplementationDeclarationSurface(
            PlatformLibraryContentSelection implementation,
            string identityName) =>
        new(
            PlatformLibraryViewCorrespondenceKind
                .ImplementationDeclarationSurface,
            implementation,
            implementation,
            identityName);
}

/// <summary>
/// Resource-free value for one completed exact Platform Library realization.
/// </summary>
public sealed class PlatformLibraryRealizationValue
{
    internal PlatformLibraryRealizationValue(LibraryReference reference) =>
        Reference = reference;

    public LibraryReference Reference { get; }
}

/// <summary>
/// Resource-free one-Library receipt composed with the House settlement.
/// </summary>
public sealed class PlatformLibraryRealizationReceipt
{
    internal PlatformLibraryRealizationReceipt(
        PlatformHouseReceipt houseReceipt,
        LibraryReference? realizedLibrary = null)
    {
        ArgumentNullException.ThrowIfNull(houseReceipt);
        if ((houseReceipt.SettlementKind
                == PlatformHouseSettlementKind.Completed)
            != (realizedLibrary is not null))
        {
            throw new ArgumentException(
                "Only a completed one-Library receipt may retain its realized Library reference.",
                nameof(realizedLibrary));
        }

        HouseReceipt = houseReceipt;
        RealizedLibrary = realizedLibrary;
    }

    public PlatformHouseReceipt HouseReceipt { get; }
    public LibraryReference? RealizedLibrary { get; }
}

/// <summary>
/// Pairs a closed House outcome with the owner transferred only by completion.
/// </summary>
public abstract class PlatformLibraryRealizationResult
{
    private protected PlatformLibraryRealizationResult(
        PlatformHouseOutcome<PlatformLibraryRealizationValue> outcome,
        PlatformLibraryRealizationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!ReferenceEquals(outcome.Receipt, receipt.HouseReceipt))
        {
            throw new ArgumentException(
                "The owning result must retain its outcome's exact House receipt.",
                nameof(receipt));
        }

        Outcome = outcome;
        Receipt = receipt;
    }

    public PlatformHouseOutcome<PlatformLibraryRealizationValue> Outcome
    {
        get;
    }
    public PlatformLibraryRealizationReceipt Receipt { get; }

    public sealed class Completed : PlatformLibraryRealizationResult
    {
        internal Completed(
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Completed outcome,
            PlatformLibraryRealizationReceipt receipt,
            LibraryContentOwner owner)
            : base(outcome, receipt)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (!ReferenceEquals(
                    receipt.RealizedLibrary,
                    outcome.Value.Reference)
                || !ReferenceEquals(
                    owner.Reference,
                    outcome.Value.Reference))
            {
                throw new ArgumentException(
                    "The completed owner and value must match the receipt's exact realized Library.",
                    nameof(owner));
            }

            Value = outcome.Value;
            Owner = owner;
        }

        public PlatformLibraryRealizationValue Value { get; }

        /// <summary>
        /// Gets the caller-owned Library authority transferred by completion.
        /// </summary>
        public LibraryContentOwner Owner { get; }
    }

    public sealed class Terminal : PlatformLibraryRealizationResult
    {
        internal Terminal(
            PlatformHouseOutcome<PlatformLibraryRealizationValue> outcome,
            PlatformLibraryRealizationReceipt receipt)
            : base(outcome, receipt)
        {
            if (outcome
                is PlatformHouseOutcome<
                    PlatformLibraryRealizationValue>.Completed)
            {
                throw new ArgumentException(
                    "A completed outcome requires an owning realization result.",
                    nameof(outcome));
            }
            if (receipt.RealizedLibrary is not null)
            {
                throw new ArgumentException(
                    "A terminal realization receipt cannot retain a realized Library.",
                    nameof(receipt));
            }
        }
    }
}

/// <summary>
/// Performs the atomic one-Library ownership handoff for an exact target.
/// </summary>
/// <remarks>
/// Every method validates all resource-free evidence and constructs the House
/// receipt before Library ownership acceptance. A completed result transfers
/// the supplied content leases into <see cref="LibraryContentOwner"/>. On a
/// terminal result or cancellation, the caller retains those leases. The
/// adjacent Artifact owner remains separately owned by the caller.
/// </remarks>
public static class PlatformHouseLibraryRealizer
{
    /// <summary>Realizes one reference-only Library.</summary>
    /// <remarks>
    /// Lease ownership transfers only on <see
    /// cref="PlatformLibraryRealizationResult.Completed"/>; otherwise the
    /// caller retains the lease and its Artifact owner.
    /// </remarks>
    public static PlatformLibraryRealizationResult RealizeReference(
        PlatformHouseRequest request,
        PlatformLibraryContentSelection reference,
        ArtifactContentLease referenceLease,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceContribution>? priorContributions = null) =>
        Realize(
            request,
            PlatformViewDemand.Reference,
            reference,
            referenceLease,
            compiledXmlDocumentation: null,
            compiledXmlDocumentationLease: null,
            implementation: null,
            implementationLease: null,
            correspondence: null,
            consumedWork,
            priorContributions,
            targetSelection: null,
            retainedSettlements: null);

    /// <summary>Realizes one Library with separate API and runtime views.</summary>
    /// <remarks>
    /// Lease ownership transfers only on <see
    /// cref="PlatformLibraryRealizationResult.Completed"/>; otherwise the
    /// caller retains both leases and their Artifact owner.
    /// </remarks>
    public static PlatformLibraryRealizationResult
        RealizeReferenceAndImplementation(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection reference,
            ArtifactContentLease referenceLease,
            PlatformLibraryContentSelection implementation,
            ArtifactContentLease implementationLease,
            PlatformLibraryViewCorrespondence correspondence,
            PlatformHouseConsumedWork consumedWork,
            IEnumerable<PlatformSourceContribution>?
                priorContributions = null) =>
        Realize(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            referenceLease,
            compiledXmlDocumentation: null,
            compiledXmlDocumentationLease: null,
            implementation,
            implementationLease,
            correspondence,
            consumedWork,
            priorContributions,
            targetSelection: null,
            retainedSettlements: null);

    /// <summary>Realizes one implementation content item in both roles.</summary>
    /// <remarks>
    /// Lease ownership transfers only on <see
    /// cref="PlatformLibraryRealizationResult.Completed"/>; otherwise the
    /// caller retains the lease and its Artifact owner.
    /// </remarks>
    public static PlatformLibraryRealizationResult RealizeImplementation(
        PlatformHouseRequest request,
        PlatformLibraryContentSelection implementation,
        ArtifactContentLease implementationLease,
        PlatformLibraryViewCorrespondence? declarationSurface,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceContribution>? priorContributions = null) =>
        Realize(
            request,
            PlatformViewDemand.Implementation,
            reference: null,
            referenceLease: null,
            compiledXmlDocumentation: null,
            compiledXmlDocumentationLease: null,
            implementation,
            implementationLease,
            declarationSurface,
            consumedWork,
            priorContributions,
            targetSelection: null,
            retainedSettlements: null);

    internal static PlatformLibraryRealizationResult
        RealizeReferenceWithCompanion(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection reference,
            ArtifactContentLease referenceLease,
            ArtifactContentReference? compiledXmlDocumentation,
            ArtifactContentLease? compiledXmlDocumentationLease,
            PlatformHouseConsumedWork consumedWork) =>
        Realize(
            request,
            PlatformViewDemand.Reference,
            reference,
            referenceLease,
            compiledXmlDocumentation,
            compiledXmlDocumentationLease,
            implementation: null,
            implementationLease: null,
            correspondence: null,
            consumedWork,
            priorContributions: null,
            targetSelection: null,
            retainedSettlements: null);

    internal static PlatformLibraryRealizationResult
        RealizeReferenceAndImplementationWithCompanion(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection reference,
            ArtifactContentLease referenceLease,
            PlatformLibraryContentSelection implementation,
            ArtifactContentLease implementationLease,
            ArtifactContentReference? compiledXmlDocumentation,
            ArtifactContentLease? compiledXmlDocumentationLease,
            PlatformLibraryViewCorrespondence correspondence,
            PlatformHouseConsumedWork consumedWork) =>
        Realize(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            referenceLease,
            compiledXmlDocumentation,
            compiledXmlDocumentationLease,
            implementation,
            implementationLease,
            correspondence,
            consumedWork,
            priorContributions: null,
            targetSelection: null,
            retainedSettlements: null);

    internal static PlatformLibraryRealizationResult
        RealizeSelectedReferenceWithCompanion(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection reference,
            ArtifactContentLease referenceLease,
            ArtifactContentReference? compiledXmlDocumentation,
            ArtifactContentLease? compiledXmlDocumentationLease,
            PlatformHouseConsumedWork consumedWork,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement>
                retainedSettlements) =>
        Realize(
            request,
            PlatformViewDemand.Reference,
            reference,
            referenceLease,
            compiledXmlDocumentation,
            compiledXmlDocumentationLease,
            implementation: null,
            implementationLease: null,
            correspondence: null,
            consumedWork,
            priorContributions: null,
            targetSelection,
            retainedSettlements);

    internal static PlatformLibraryRealizationResult
        RealizeSelectedReferenceAndImplementationWithCompanion(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection reference,
            ArtifactContentLease referenceLease,
            PlatformLibraryContentSelection implementation,
            ArtifactContentLease implementationLease,
            ArtifactContentReference? compiledXmlDocumentation,
            ArtifactContentLease? compiledXmlDocumentationLease,
            PlatformLibraryViewCorrespondence correspondence,
            PlatformHouseConsumedWork consumedWork,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement>
                retainedSettlements) =>
        Realize(
            request,
            PlatformViewDemand.ReferenceAndImplementation,
            reference,
            referenceLease,
            compiledXmlDocumentation,
            compiledXmlDocumentationLease,
            implementation,
            implementationLease,
            correspondence,
            consumedWork,
            priorContributions: null,
            targetSelection,
            retainedSettlements);

    internal static PlatformLibraryRealizationResult
        RealizeSelectedImplementation(
            PlatformHouseRequest request,
            PlatformLibraryContentSelection implementation,
            ArtifactContentLease implementationLease,
            PlatformLibraryViewCorrespondence declarationSurface,
            PlatformHouseConsumedWork consumedWork,
            PlatformTargetSelectionContext targetSelection,
            IReadOnlyList<PlatformSourceSettlement>
                retainedSettlements) =>
        Realize(
            request,
            PlatformViewDemand.Implementation,
            reference: null,
            referenceLease: null,
            compiledXmlDocumentation: null,
            compiledXmlDocumentationLease: null,
            implementation,
            implementationLease,
            declarationSurface,
            consumedWork,
            priorContributions: null,
            targetSelection,
            retainedSettlements);

    static PlatformLibraryRealizationResult Realize(
        PlatformHouseRequest request,
        PlatformViewDemand expectedView,
        PlatformLibraryContentSelection? reference,
        ArtifactContentLease? referenceLease,
        ArtifactContentReference? compiledXmlDocumentation,
        ArtifactContentLease? compiledXmlDocumentationLease,
        PlatformLibraryContentSelection? implementation,
        ArtifactContentLease? implementationLease,
        PlatformLibraryViewCorrespondence? correspondence,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceContribution>? priorContributions,
        PlatformTargetSelectionContext? targetSelection,
        IReadOnlyList<PlatformSourceSettlement>?
            retainedSettlements)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        PlatformFamilyTarget? target = request.Target switch
        {
            PlatformTargetDemand.Exact exact
                when targetSelection is null
                    && retainedSettlements is null => exact.Target,
            PlatformTargetDemand.FamilyDefault demand
                when targetSelection is not null
                    && retainedSettlements is not null
                    && ReferenceEquals(
                        targetSelection.TargetSettlement.Demand,
                        demand)
                    && retainedSettlements.Count != 0 =>
                targetSelection.Target,
            _ => null,
        };
        if (target is null
            || request.Operation is not PlatformHouseOperation.Realize operation
            || operation.View != expectedView
            || operation.Population
                is not PlatformPopulationDemand.Library)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRequest,
                "platform-library.invalid-request",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        if (ExceedsBudget(consumedWork, request))
        {
            return Incomplete(
                request,
                consumedWork,
                "platform-library.work-incomplete",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        if (!ValidSelection(
                request,
                target,
                PlatformSourceFacet.Reference,
                reference,
                referenceLease)
            || !ValidSelection(
                request,
                target,
                PlatformSourceFacet.Implementation,
                implementation,
                implementationLease))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                "platform-library.invalid-content",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        if (!ValidCompiledXmlDocumentation(
                request,
                reference,
                compiledXmlDocumentation,
                compiledXmlDocumentationLease))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                "platform-library.invalid-compiled-xml-content",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        if (expectedView == PlatformViewDemand.Implementation
            && correspondence is null)
        {
            return Unavailable(
                request,
                consumedWork,
                implementation!.Contribution,
                "platform-library.api-role-unavailable",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        if (!ValidCorrespondence(
                expectedView,
                reference,
                implementation,
                correspondence))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                "platform-library.invalid-correspondence",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }

        PlatformSourceContribution[] prior =
            [.. priorContributions ?? []];
        if (prior.Any(
                contribution =>
                    contribution is null
                    || !ReferenceEquals(
                        contribution.Request,
                        request.Snapshot)
                    || contribution.Kind
                        is not PlatformSourceContributionKind.Unavailable
                            and not PlatformSourceContributionKind.Failed))
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRetainedEvidence,
                "platform-library.invalid-prior-evidence",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }

        PlatformLibraryContentSelection api = reference ?? implementation!;
        PlatformLibraryContentSelection? runtime = expectedView
            == PlatformViewDemand.Reference
                ? null
                : implementation;
        LibraryReference library;
        PlatformHouseCompletion.Realization completion;
        PlatformHouseReceipt receipt;
        IReadOnlyList<ArtifactContentLease> children;
        try
        {
            var assemblyCorrespondence =
                new LibraryAssemblyCorrespondence(
                    api.Content,
                    api.AssemblyIdentity,
                    runtime?.Content,
                    runtime?.AssemblyIdentity);
            LibraryCompanionCorrespondence[] companions =
                compiledXmlDocumentation is null
                    ? []
                    :
                    [
                        new LibraryCompanionCorrespondence(
                            compiledXmlDocumentation,
                            LibraryContentRole
                                .CompiledXmlDocumentation,
                            api.Content),
                    ];
            library = LibraryReference.CreateFromSource(
                new ExactLibrarySourceCoordinate.Platform(
                    new PlatformLibraryPopulationDeclaration(
                        target.Family),
                    api.AssemblyIdentity),
                assemblyCorrespondence,
                companions);

            var settlements = retainedSettlements is null
                ? new List<PlatformSourceSettlement>(prior.Length + 2)
                : [.. retainedSettlements];
            if (retainedSettlements is null)
            {
                settlements.AddRange(
                    prior.Select(
                        contribution => new PlatformSourceSettlement(
                            contribution,
                            PlatformSourceSettlementDisposition
                                .OutcomeRelevant)));
                if (reference is not null)
                {
                    settlements.Add(
                        new PlatformSourceSettlement(
                            reference.Contribution,
                            PlatformSourceSettlementDisposition.Selected));
                }
                if (implementation is not null
                    && !ReferenceEquals(
                        implementation.Contribution,
                        reference?.Contribution))
                {
                    settlements.Add(
                        new PlatformSourceSettlement(
                            implementation.Contribution,
                            PlatformSourceSettlementDisposition.Selected));
                }
            }
            else if (!SelectionIsRetained(
                    settlements,
                    reference)
                || !SelectionIsRetained(
                    settlements,
                    implementation))
            {
                return Rejected(
                    request,
                    consumedWork,
                    PlatformHouseRejectionKind.InvalidRetainedEvidence,
                    "platform-library.missing-selected-evidence",
                    targetSelection!.TargetSettlement,
                    retainedSettlements);
            }

            completion = new PlatformHouseCompletion.Realization(
                (PlatformHouseOperationSnapshot.Realize)
                    request.Snapshot.Operation,
                settlements.Where(
                    settlement => settlement.Disposition
                            == PlatformSourceSettlementDisposition.Selected
                        && settlement.Contribution
                            is PlatformSourceContribution.Realization),
                correspondence);
            receipt = new PlatformHouseReceipt(
                request.Snapshot,
                (PlatformTargetSettlement?)
                    targetSelection?.TargetSettlement
                    ?? new PlatformTargetSettlement.Exact(
                        (PlatformTargetDemand.Exact)request.Target),
                settlements,
                consumedWork,
                completion);

            if (ReferenceEquals(api.Content, runtime?.Content))
            {
                if (referenceLease is not null
                    && implementationLease is not null
                    && !ReferenceEquals(
                        referenceLease,
                        implementationLease))
                {
                    return Rejected(
                        request,
                        consumedWork,
                        PlatformHouseRejectionKind.InvalidOwnerResult,
                        "platform-library.duplicate-content-authority",
                        targetSelection?.TargetSettlement,
                        retainedSettlements);
                }
                children = [referenceLease ?? implementationLease!];
            }
            else
            {
                children = runtime is null
                    ? [referenceLease!]
                    : [referenceLease!, implementationLease!];
            }
            if (compiledXmlDocumentationLease is not null)
            {
                children =
                    [
                        .. children,
                        compiledXmlDocumentationLease,
                    ];
            }
        }
        catch (ArgumentException)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidRetainedEvidence,
                "platform-library.invalid-retained-evidence",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        LibraryContentOwner owner;
        try
        {
            owner = new LibraryContentOwner(library, children);
        }
        catch (ObjectDisposedException)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                "platform-library.released-content",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }
        catch (ArgumentException)
        {
            return Rejected(
                request,
                consumedWork,
                PlatformHouseRejectionKind.InvalidOwnerResult,
                "platform-library.invalid-content-authority",
                targetSelection?.TargetSettlement,
                retainedSettlements);
        }

        var value = new PlatformLibraryRealizationValue(library);
        return new PlatformLibraryRealizationResult.Completed(
            new PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Completed(
                    completion.Bind(value),
                    receipt),
            new PlatformLibraryRealizationReceipt(
                receipt,
                library),
            owner);
    }

    static bool ValidSelection(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSourceFacet expectedFacet,
        PlatformLibraryContentSelection? selection,
        ArtifactContentLease? lease)
    {
        bool required = request.Operation is PlatformHouseOperation.Realize
        {
            View: PlatformViewDemand.ReferenceAndImplementation,
        }
            || expectedFacet switch
            {
                PlatformSourceFacet.Reference =>
                    request.Operation is PlatformHouseOperation.Realize
                    {
                        View: PlatformViewDemand.Reference,
                    },
                PlatformSourceFacet.Implementation =>
                    request.Operation is PlatformHouseOperation.Realize
                    {
                        View: PlatformViewDemand.Implementation,
                    },
                _ => false,
            };
        if (!required)
            return selection is null && lease is null;
        return selection is not null
            && lease is not null
            && selection.Contribution.Facet == expectedFacet
            && ReferenceEquals(
                selection.Contribution.Request,
                request.Snapshot)
            && selection.Contribution.Target == target
            && request.Sources.Authorizes(
                expectedFacet,
                selection.Contribution.Capability)
            && ReferenceEquals(lease.Reference, selection.Content);
    }

    static bool ValidCompiledXmlDocumentation(
        PlatformHouseRequest request,
        PlatformLibraryContentSelection? reference,
        ArtifactContentReference? documentation,
        ArtifactContentLease? documentationLease)
    {
        if (documentation is null
            || documentationLease is null)
        {
            return documentation is null
                && documentationLease is null;
        }
        if (request.Operation
                is not PlatformHouseOperation.Realize
                {
                    ContentDemand: var contentDemand,
                }
            || !contentDemand.HasFlag(
                PlatformLibraryContentDemand
                    .CompiledXmlDocumentation)
            || reference is null
            || documentation.Provenance
                is not PlatformLibraryArtifactProvenance provenance)
        {
            return false;
        }

        return ReferenceEquals(
                provenance.Contribution,
                reference.Contribution)
            && ReferenceEquals(
                documentation.Generation,
                reference.Content.Generation)
            && ReferenceEquals(
                documentationLease.Reference,
                documentation);
    }

    static bool ValidCorrespondence(
        PlatformViewDemand view,
        PlatformLibraryContentSelection? reference,
        PlatformLibraryContentSelection? implementation,
        PlatformLibraryViewCorrespondence? correspondence) =>
        view switch
        {
            PlatformViewDemand.Reference => correspondence is null,
            PlatformViewDemand.ReferenceAndImplementation =>
                correspondence is
                {
                    Kind:
                        PlatformLibraryViewCorrespondenceKind
                            .ReferenceAndImplementation,
                }
                && ReferenceEquals(correspondence.Api, reference)
                && ReferenceEquals(
                    correspondence.Implementation,
                    implementation),
            PlatformViewDemand.Implementation =>
                correspondence is
                {
                    Kind:
                        PlatformLibraryViewCorrespondenceKind
                            .ImplementationDeclarationSurface,
                }
                && ReferenceEquals(correspondence.Api, implementation)
                && ReferenceEquals(
                    correspondence.Implementation,
                    implementation),
            _ => false,
        };

    static bool SelectionIsRetained(
        IReadOnlyList<PlatformSourceSettlement> settlements,
        PlatformLibraryContentSelection? selection) =>
        selection is null
        || settlements.Any(
            settlement =>
                ReferenceEquals(
                    settlement.Contribution,
                    selection.Contribution)
                && settlement.Disposition
                    == PlatformSourceSettlementDisposition.Selected);

    internal static bool ExceedsBudget(
        PlatformHouseConsumedWork consumed,
        PlatformHouseRequest request) =>
        consumed.SourceOperations > request.Work.MaxSourceOperations
        || consumed.TargetCandidates > request.Work.MaxTargetCandidates
        || consumed.Assemblies > request.Work.MaxAssemblies
        || consumed.XmlDocuments > request.Work.MaxXmlDocuments
        || consumed.PortablePdbs > request.Work.MaxPortablePdbs
        || consumed.SourceDocuments > request.Work.MaxSourceDocuments
        || consumed.Bytes > request.Work.MaxBytes
        || consumed.ForwardingHops > request.Work.MaxForwardingHops
        || consumed.Elapsed > request.Work.MaxDuration
        || request.Target is PlatformTargetDemand.Selecting selecting
            && (consumed.TargetCandidates
                    > selecting.Work.MaxCandidates
                || consumed.TargetComparisons
                    > selecting.Work.MaxComparisons);

    internal static PlatformLibraryRealizationResult Rejected(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformHouseRejectionKind kind,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>? retainedSettlements = null)
    {
        var termination = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                kind,
                PlatformHouseTerminalEvidenceIdentity.Create(
                    evidenceName)));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement ?? TargetSettlement(request.Target),
            retainedSettlements ?? [],
            consumedWork,
            termination: termination);
        return new PlatformLibraryRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected(
                    termination,
                    receipt),
            new PlatformLibraryRealizationReceipt(receipt));
    }

    static PlatformLibraryRealizationResult Unavailable(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        PlatformSourceContribution contribution,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>? retainedSettlements = null)
    {
        var termination = new PlatformHouseTermination.Unavailable(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement ?? TargetSettlement(request.Target),
            retainedSettlements is null
                ?
                [
                    new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition.OutcomeRelevant),
                ]
                : retainedSettlements,
            consumedWork,
            termination: termination);
        return new PlatformLibraryRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Unavailable(
                    termination,
                    receipt),
            new PlatformLibraryRealizationReceipt(receipt));
    }

    internal static PlatformLibraryRealizationResult Incomplete(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>? retainedSettlements = null)
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement ?? TargetSettlement(request.Target),
            retainedSettlements ?? [],
            consumedWork,
            termination: termination);
        return new PlatformLibraryRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete(
                    termination,
                    receipt),
            new PlatformLibraryRealizationReceipt(receipt));
    }

    internal static PlatformLibraryRealizationResult Failed(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork consumedWork,
        IEnumerable<PlatformSourceContribution> contributions,
        IEnumerable<PlatformHouseFailureKind> failures,
        bool cancellationObserved,
        string evidenceName,
        PlatformTargetSettlement? targetSettlement = null,
        IReadOnlyList<PlatformSourceSettlement>? retainedSettlements = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        PlatformSourceSettlement[] settlements =
            retainedSettlements?.ToArray()
            ?? contributions.Select(
                    contribution => new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition.OutcomeRelevant))
                .ToArray();
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            failures,
            cancellationObserved);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement ?? TargetSettlement(request.Target),
            settlements,
            consumedWork,
            termination: termination);
        return new PlatformLibraryRealizationResult.Terminal(
            new PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Failed(
                    termination,
                    receipt),
            new PlatformLibraryRealizationReceipt(receipt));
    }

    internal static PlatformTargetSettlement TargetSettlement(
        PlatformTargetDemand demand) =>
        demand is PlatformTargetDemand.Exact exact
            ? new PlatformTargetSettlement.Exact(exact)
            : new PlatformTargetSettlement.Unsettled(demand);
}
